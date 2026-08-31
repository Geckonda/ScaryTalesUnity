using Assets.Scripts.Network;
using Assets.Scripts.UIEntities;
using DG.Tweening;
using ScaryTales;
using ScaryTales.Enums;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

public class PlayerHandUI : MonoBehaviour
{
    private ClientGameView _view;
    private SeatLayout _seatLayout;

    private CardViewService _cardViewService;

    public Dictionary<Player, Transform> _playerHandPanels;

    [Tooltip("Сколько летит карта в руку, секунд. Прилёт в руку не блокирует очередь событий, поэтому при раздаче карты летят внахлёст и значение НЕ умножается на их число.")]
    [SerializeField] private float _dealDuration = 1f;

    [Tooltip("Пауза между вылетами соседних карт при раздаче, секунд. Ноль — все вылетают разом; 0.15 даёт каскад, при котором карты идут одна за другой, оставаясь в полёте одновременно.")]
    [SerializeField] private float _dealStagger = 0.15f;

    void Awake()
    {
        _cardViewService = CardViewService.Instance;
    }

    /// <summary>
    /// Wires this hand UI to the client mirror and the seat layout.
    /// Called by UnGameManager when GameStartedEvent arrives.
    /// </summary>
    public void Initialize(ClientGameView view, SeatLayout seatLayout)
    {
        _view = view;
        _seatLayout = seatLayout;

        if (_view.LocalPlayer == null)
        {
            Debug.LogError("[PlayerHandUI] LocalPlayer not ready.");
            return;
        }

        _view.OnCardAddedToHand += HandleCardAddedToHand;
        _view.OnCardAddedToHandFromDiscardPile += HandleCardAddedToHandFromDiscardPile;
        _view.OnCardReturnedToDeck += HandleCardReturnedToDeck;

        _playerHandPanels = new Dictionary<Player, Transform>();
        var localSeat = _seatLayout?.LocalSeat;
        if (localSeat?.HandPanel != null)
            _playerHandPanels[_view.LocalPlayer] = localSeat.HandPanel;

        for (int i = 0; i < _view.Opponents.Count; i++)
        {
            var seat = _seatLayout?.GetOpponentSeat(i);
            if (seat?.HandPanel != null)
                _playerHandPanels[_view.Opponents[i]] = seat.HandPanel;
        }
    }

    /// <summary>
    /// Панель руки игрока, если она известна. Нужна не только раздаче:
    /// брошенная мимо стола карта возвращается в руку своего владельца,
    /// определяя её на месте (см. <c>DragAndDrop.ReturnToHand</c>).
    /// </summary>
    public Transform GetHandPanel(Player player)
    {
        if (_playerHandPanels == null || player == null) return null;
        return _playerHandPanels.TryGetValue(player, out var panel) ? panel : null;
    }

    private async void HandleCardAddedToHand(Card card, Player player)
    {
        var unityManager = UnGameManager.Instance;
        if (unityManager == null) return;

        if (!_playerHandPanels.TryGetValue(player, out var hand)) return;

        var deck = unityManager.Deck;
        var cardView = _cardViewService.GetCardView(card) ?? _cardViewService.CreateCardView(card, deck);
        if (card.Owner != null && card.Owner == _view.LocalPlayer)
            cardView.FaceUp();
        else
            cardView.FaceDown();

        var animationTask = AnimateCardToHand(cardView, hand, card, player);
        // blocksEventQueue: false — карты в руку ничему не предшествуют и
        // ничему не мешают. Если бы очередь ждала каждую, раздача из пяти
        // карт превратилась бы в пять последовательных полётов вместо одного
        // общего вылета, каким она была раньше.
        AnimationManager.Instance.Register(animationTask, blocksEventQueue: false, staggerSeconds: _dealStagger);
        await animationTask;
    }

    private async void HandleCardAddedToHandFromDiscardPile(Card card, Player player)
    {
        var unityManager = UnGameManager.Instance;
        if (unityManager == null) return;

        if (!_playerHandPanels.TryGetValue(player, out var hand)) return;

        var discardPile = unityManager._boardUI.DiscardPile;
        var cardView = _cardViewService.CreateCardView(card, discardPile);
        if (card.Owner != null && card.Owner == _view.LocalPlayer)
            cardView.FaceUp();
        else
            cardView.FaceDown();

        var animationTask = AnimateCardToHand(cardView, hand, card, player);
        AnimationManager.Instance.Register(animationTask, blocksEventQueue: false);
        await animationTask;
    }

    /// <summary>
    /// Карта уходит из руки обратно в колоду — так разбирается рука игрока,
    /// вышедшего посреди партии. Летит в колоду и уничтожается: там она
    /// снова становится безличной рубашкой, и следующее взятие построит ей
    /// новое представление.
    /// </summary>
    private async void HandleCardReturnedToDeck(Card card, Player owner)
    {
        var unityManager = UnGameManager.Instance;
        if (unityManager == null) return;

        var cardView = _cardViewService.GetCardView(card);
        if (cardView == null) return;

        var deck = unityManager.Deck;
        if (deck == null)
        {
            _cardViewService.ForgetCardView(card);
            Destroy(cardView.gameObject);
            return;
        }

        cardView.FaceDown();

        var animationTask = AnimateCardToDeck(cardView, deck, card);
        // Не блокирует очередь: карты уходят в колоду разом, и ждать их
        // незачем — следующее событие ни на одну из них не наезжает.
        AnimationManager.Instance.Register(animationTask, blocksEventQueue: false, staggerSeconds: _dealStagger);
        await animationTask;
    }

    private async Task AnimateCardToDeck(CardView cardView, Transform deck, Card card)
    {
        await cardView.transform.DOMove(deck.position, _dealDuration)
            .SetEase(Ease.InQuad)
            .AsyncWaitForCompletion();

        if (cardView == null) return;

        // Забыть представление обязательно: карта вернулась в колоду и её
        // возьмут снова, а уничтоженный объект в словаре сервиса пережил бы
        // сам себя — см. CardViewService.GetCardView.
        _cardViewService.ForgetCardView(card);
        Destroy(cardView.gameObject);
    }

    private async Task AnimateCardToHand(CardView cardView, Transform hand, Card card, Player owner)
    {
        // Гасим предыдущий полёт этой же карты, если он ещё идёт.
        //
        // DOTween сам старый твин не убивает: два DOMove на одном трансформе
        // просто тянут его в разные стороны, и карта уезжает куда-то между.
        // Так и выглядела карта Волшебника — стол успевал её забрать и
        // разложить, а незавершённый полёт в руку ещё полсекунды волок её
        // прочь от разложенного места.
        cardView.transform.DOKill();

        await cardView.transform.DOMove(hand.position, _dealDuration)
            .SetEase(Ease.OutQuad)
            .AsyncWaitForCompletion();

        // За время полёта карта могла уехать дальше — и это не редкость, а
        // штатный ход событий: Волшебник раскрывает карту и ТУТ ЖЕ её
        // разыгрывает, так что «в руку» и «на стол» приходят подряд. Прилёт
        // в руку очередь не блокирует, поэтому обе анимации живут
        // одновременно, а родителя назначает та, что финиширует последней —
        // то есть более длинный полёт в руку. Без этой проверки карта
        // возвращалась бы со стола обратно в руку через полсекунды.
        if (card != null && (card.Position != CardPosition.InHand || card.Owner != owner))
            return;

        // За время полёта сцену могли перезагрузить — выход в меню посреди
        // партии делает ровно это, — и тогда карты с панелью уже уничтожены.
        // Проверять надо ЗДЕСЬ, после await: на входе они были живы.
        // Уничтоженный объект Unity равен null по своей перегрузке ==, но
        // обращение к нему бросает MissingReferenceException, а бросить его
        // отсюда некуда: метод зовут из async void.
        if (cardView == null || hand == null) return;

        // worldPositionStays: false — the hand panel is rotated to face the
        // table centre, so keeping the world pose would leave the card with
        // a compensating local transform that fights the layout group.
        cardView.transform.SetParent(hand, false);

        var handRect = hand.GetComponent<RectTransform>();
        if (handRect != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(handRect);

        await Task.Yield();
    }

    public void ClearAllCardListeners(Player player)
    {
        if (!_playerHandPanels.TryGetValue(player, out var panel)) return;

        foreach (Transform cardTransform in panel)
        {
            var drag = cardTransform.GetComponent<DragAndDrop>();
            if (drag != null) drag.ClearAllListeners();
        }
    }
}
