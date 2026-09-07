using Assets.Scripts.Network;
using Assets.Scripts.UIEntities;
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

    [Tooltip("Сколько длится разворот карты в полёте, секунд. Вложен в конец полёта, а не добавлен к нему. Ноль — разворота нет, карта просто летит.")]
    [SerializeField] private float _flipDuration = 0.35f;

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
        var flip = FlipOnTheWayToHand(cardView, card);

        var animationTask = AnimateCardToHand(cardView, hand, card, player, flip);
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
        var flip = FlipOnTheWayToHand(cardView, card);

        var animationTask = AnimateCardToHand(cardView, hand, card, player, flip);
        AnimationManager.Instance.Register(animationTask, blocksEventQueue: false);
        await animationTask;
    }

    /// <summary>
    /// Разворот, который карта отыграет по дороге в руку — или <c>null</c>,
    /// если разворачивать нечего.
    ///
    /// <para>Из колоды и из сброса карта вылетает рубашкой вверх — так она
    /// там и лежала. Своя разворачивается лицом на подлёте к руке, чужая
    /// остаётся рубашкой: её лица не знает даже клиент владельца.</para>
    ///
    /// <para>Считать надо <b>до</b> полёта: <c>IsFacedUp</c> переключится в
    /// середине разворота.</para>
    /// </summary>
    private System.Action FlipOnTheWayToHand(CardView cardView, Card card)
    {
        if (cardView == null) return null;

        cardView.FaceDown();

        bool mine = card.Owner != null && card.Owner == _view.LocalPlayer;
        return mine ? cardView.FaceUp : (System.Action)null;
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

        // Обратный разворот: своя карта лежит лицом вверх и поворачивается
        // рубашкой по дороге в колоду. Чужая уже рубашкой — ей вертеться
        // незачем.
        var flip = cardView.IsFacedUp ? cardView.FaceDown : (System.Action)null;

        var animationTask = AnimateCardToDeck(cardView, deck, card, flip);
        // Не блокирует очередь: карты уходят в колоду разом, и ждать их
        // незачем — следующее событие ни на одну из них не наезжает.
        AnimationManager.Instance.Register(animationTask, blocksEventQueue: false, staggerSeconds: _dealStagger);
        await animationTask;
    }

    private async Task AnimateCardToDeck(CardView cardView, Transform deck, Card card,
                                         System.Action onFlipMidpoint)
    {
        // Перехваченный полёт карту не уничтожает: её уже ведёт следующий, и
        // она жива где-то ещё. Уничтоженную вместе со сценой Fly отсеивает
        // тем же ответом.
        if (!await CardFlight.Fly(cardView, deck, _dealDuration, _flipDuration, onFlipMidpoint)) return;

        // Забыть представление обязательно: карта вернулась в колоду и её
        // возьмут снова, а уничтоженный объект в словаре сервиса пережил бы
        // сам себя — см. CardViewService.GetCardView.
        _cardViewService.ForgetCardView(card);
        Destroy(cardView.gameObject);
    }

    private async Task AnimateCardToHand(CardView cardView, Transform hand, Card card, Player owner,
                                         System.Action onFlipMidpoint)
    {
        // Карта может уехать дальше прямо посреди этого полёта — и это не
        // редкость, а штатный ход событий: Волшебник раскрывает карту и ТУТ ЖЕ
        // её разыгрывает, так что «в руку» и «на стол» приходят подряд. Прилёт
        // в руку очередь не блокирует, поэтому оба полёта живут одновременно.
        //
        // Разрешается это в CardFlight.Fly: каждый полёт гасит предыдущий
        // (DOTween сам этого не делает — два твина на одном трансформе просто
        // тянут его в разные стороны), а перехваченный возвращает false. То
        // есть побеждает тот, кто стартовал последним, — он же и есть более
        // свежая правда, потому что события применяются в порядке сервера.
        if (!await CardFlight.Fly(cardView, hand, _dealDuration, _flipDuration, onFlipMidpoint)) return;

        // Вторая проверка того же: полёт мог долететь честно, а событие «карта
        // ушла на стол» прийти уже после него. Без неё карта возвращалась бы
        // со стола обратно в руку.
        if (card != null && (card.Position != CardPosition.InHand || card.Owner != owner))
            return;

        // За время полёта сцену могли перезагрузить — выход в меню посреди
        // партии делает ровно это, — и тогда карты с панелью уже уничтожены.
        // Проверять надо ЗДЕСЬ, после await: на входе они были живы.
        // Уничтоженный объект Unity равен null по своей перегрузке ==, но
        // обращение к нему бросает MissingReferenceException, а бросить его
        // отсюда некуда: метод зовут из async void.
        if (cardView == null || hand == null) return;

        // worldPositionStays: true. Раньше было false, потому что панель руки
        // повёрнута к центру стола и сохранение мировой позы оставляло карте
        // компенсирующий локальный трансформ. Теперь наоборот: карта уже стоит
        // ровно в своём слоте веера (CardFlight замерил его заранее), и
        // мировую позу надо сохранить. Компенсация живёт до пересборки строкой
        // ниже, которая перепишет карте всю позу целиком.
        cardView.transform.SetParent(hand, true);

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
