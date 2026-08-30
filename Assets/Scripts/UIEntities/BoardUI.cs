using Assets.Scripts.Network;
using Assets.Scripts.UIEntities;
using Assets.Scripts.Views;
using DG.Tweening;
using ScaryTales;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

public class BoardUI : MonoBehaviour
{
    private CardViewService _cardViewService;
    private ClientGameView _view;
    private SeatLayout _seatLayout;
    // Each player's BeforePlayer destination, looked up at runtime per
    // CardMovedToBeforePlayer event.
    private Dictionary<Player, RectTransform> _beforePlayerTables = new();

    public Transform GameBoardPanel;
    public Transform TimeOfDaySlot;
    public Transform DiscardPile;
    public GameObject UIBlockerOverlay;

    [Tooltip("Пауза (мс) перед тем, как сброшенная эффектом карта улетит со стола — чтобы игроки успели увидеть, какую именно сбросили. С очередью событий пауза блокирующая, поэтому дорогая: всё остальное её ждёт.")]
    [SerializeField] private int _discardReadDelay = 500;

    [Tooltip("Сколько летит карта до места назначения, секунд.")]
    [SerializeField] private float _moveDuration = 0.5f;
    private void Start()
    {
        if (UIBlockerOverlay != null) UIBlockerOverlay.SetActive(false);
    }

    /// <summary>
    /// Wires this BoardUI to the client mirror and the seat layout.
    /// Each seat owns its own BeforePlayerTable (replacing the old
    /// LocalPlayerTable / OpponentTable pair).
    /// </summary>
    public void Initialize(ClientGameView view, SeatLayout seatLayout)
    {
        _view = view;
        _seatLayout = seatLayout;

        view.OnCardMovedToBoard += HandleCardMovedToBoard;
        view.OnCardMovedToBeforePlayer += HandleCardMovedToBeforePlayer;
        view.OnCardMovedToTimeOfDaySlot += HandleCardMovedToTimeOfDaySlot;
        view.OnCardMovedToDiscardPile += HandleCardMovedToDiscardPile;

        _cardViewService = CardViewService.Instance;

        _beforePlayerTables.Clear();
        var localSeat = _seatLayout?.LocalSeat;
        if (localSeat?.BeforePlayerTable != null)
            _beforePlayerTables[_view.LocalPlayer] = localSeat.BeforePlayerTable;
        for (int i = 0; i < _view.Opponents.Count; i++)
        {
            var seat = _seatLayout?.GetOpponentSeat(i);
            if (seat?.BeforePlayerTable != null)
                _beforePlayerTables[_view.Opponents[i]] = seat.BeforePlayerTable;
        }
    }

    private async void HandleCardMovedToBoard(Card card)
    {
        var unityManager = UnGameManager.Instance;
        var deck = unityManager.Deck;
        var cardView = _cardViewService.GetCardView(card)
            ?? _cardViewService.CreateCardView(card, deck);
        cardView.FaceUp();
        var animationTask = AnimateCardTransformToPositionInLayout(cardView, GameBoardPanel);
        AnimationManager.Instance.Register(animationTask);
        await animationTask;
    }

    private async void HandleCardMovedToBeforePlayer(Card card)
    {
        var unityManager = UnGameManager.Instance;
        var deck = unityManager.Deck;
        var cardView = _cardViewService.GetCardView(card)
            ?? _cardViewService.CreateCardView(card, deck);
        cardView.FaceUp();

        Transform panel = null;
        if (card.Owner != null && _beforePlayerTables.TryGetValue(card.Owner, out var tbl))
            panel = tbl;
        // Fallback: send to general board if we couldn't resolve a seat.
        if (panel == null) panel = GameBoardPanel;

        var animationTask = AnimateCardTransformToPositionInLayout(cardView, panel);
        AnimationManager.Instance.Register(animationTask);
        await animationTask;
    }

    private async void HandleCardMovedToTimeOfDaySlot(Card card)
    {
        var unityManager = UnGameManager.Instance;
        var deck = unityManager.Deck;
        CardView cardView = _cardViewService.GetCardView(card)
            ?? _cardViewService.CreateCardView(card, deck);
        cardView.FaceUp();

        // Единственный обработчик, который раньше не регистрировал свою
        // анимацию — поэтому карту дня/ночи никто не ждал, и раздача карт
        // начиналась поверх ещё летящей карты.
        //
        // Паузы здесь нет намеренно. Карта уже полежала на столе и прочитана,
        // а в начале партии эта же пауза давала две секунды пустого экрана
        // перед вылетом карты Ночи — ту самую «непонятную задержку».
        var animationTask = AnimateCardTransformToPosition(cardView, TimeOfDaySlot, 0);
        AnimationManager.Instance.Register(animationTask);
        await animationTask;
        if (cardView == null || TimeOfDaySlot == null) return;

        cardView.transform.SetParent(TimeOfDaySlot);
        card.Owner = null;
        cardView.transform.localScale = Vector3.one;
    }

    private async void HandleCardMovedToDiscardPile(Card card)
    {
        CardView cardView = _cardViewService.GetCardView(card);
        if (cardView != null)
        {
            // Карта, уходящая из слота дня/ночи, провисела на виду весь
            // прошлый ход — читать её заново незачем, и как только она ушла,
            // на её место сразу летит новая. Пауза нужна только для карт,
            // которые эффект сбрасывает со стола: там игрок должен успеть
            // увидеть, какие именно.
            bool leavingTimeOfDaySlot = TimeOfDaySlot != null
                && cardView.transform.parent == TimeOfDaySlot;

            var animationTask = AnimateCardTransformToPosition(
                cardView, DiscardPile, leavingTimeOfDaySlot ? 0 : _discardReadDelay);
            AnimationManager.Instance.Register(animationTask);
            await animationTask;
            if (cardView == null) return;

            if (DiscardPileView.Instance != null) DiscardPileView.Instance.SetSuit();
            Destroy(cardView.gameObject);
        }
        else
        {
            Debug.LogError($"CardView for {card.Name} not found.");
        }
    }

    public async Task AnimateCardTransformToPosition(CardView card, Transform to, int delayMs)
    {
        if (delayMs > 0) await Task.Delay(delayMs);
        // Пауза и полёт переживают перезагрузку сцены (выход в меню посреди
        // партии), а карта с местом назначения — нет. Проверяем после каждого
        // await: уничтоженный объект Unity равен null по своей перегрузке ==,
        // но обращение к нему бросает MissingReferenceException — а бросать
        // его отсюда некуда, эти задачи ждут из async void.
        if (card == null || to == null) return;

        await card.transform.DOMove(to.position, _moveDuration)
            .SetEase(Ease.OutQuad)
            .AsyncWaitForCompletion();
    }

    public async Task AnimateCardTransformToPositionInLayout(CardView card, Transform to)
    {
        await card.transform.DOMove(to.position, _moveDuration)
            .SetEase(Ease.OutQuad)
            .AsyncWaitForCompletion();
        if (card == null || to == null) return;

        // worldPositionStays: false — the destination panel may be rotated
        // and scaled (seats face the table centre), and keeping world pose
        // would bake that compensation into localRotation/localScale right
        // before the layout group overwrites them anyway.
        card.transform.SetParent(to, false);

        var toRect = to.GetComponent<RectTransform>();
        if (toRect != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(toRect);
        await Task.Yield();
    }
}
