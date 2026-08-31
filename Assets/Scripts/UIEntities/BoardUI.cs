using Assets.Scripts.Network;
using Assets.Scripts.UIEntities;
using Assets.Scripts.Views;
using DG.Tweening;
using ScaryTales;
using System.Collections.Generic;
using TMPro;
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

    [Header("Пустая колода")]
    [Tooltip("Необязательно. Картинка опустевшей колоды. Если не задана, рубашка просто гаснет до цвета ниже — так фича работает без единой привязки в сцене.")]
    [SerializeField] private Sprite _emptyDeckSprite;

    [Tooltip("Во что перекрасить рубашку, когда колода пуста, а картинки для пустой колоды нет.")]
    [SerializeField] private Color _emptyDeckTint = new Color(1f, 1f, 1f, 0.25f);

    [Tooltip("Текст с числом оставшихся карт. Можно не привязывать: если оставить пустым, берётся первый TMP_Text внутри слота колоды (объект CardLeftCount).")]
    [SerializeField] private TMP_Text _deckCountText;

    [Tooltip("С какого остатка показывать счётчик. Он нужен не всю партию, а только когда колода на исходе: пока карт много, число лишь шумит, а под конец именно оно говорит, сколько ходов осталось.")]
    [SerializeField] private int _deckCountVisibleFrom = 5;

    // Исходный вид колоды, чтобы вернуть его, когда карты в неё вернутся
    // (рука вышедшего игрока уходит обратно в колоду — см. пункт 4).
    private Image _deckImage;
    private Sprite _deckSprite;
    private Color _deckColor;
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
        view.OnDeckCountChanged += UpdateDeckVisual;

        _cardViewService = CardViewService.Instance;

        CacheDeckVisual();
        UpdateDeckVisual(_view.DeckRemaining);

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

    /// <summary>
    /// Запоминает исходный вид колоды, чтобы было к чему возвращаться.
    /// Колода не «кончается навсегда»: рука вышедшего игрока уезжает обратно
    /// в неё, и рубашка должна вернуться.
    /// </summary>
    private void CacheDeckVisual()
    {
        var deck = UnGameManager.Instance != null ? UnGameManager.Instance.Deck : null;
        if (deck == null) return;

        // Счётчик ищем сами, если его не привязали: он живёт внутри слота
        // колоды, и другого текста там нет. includeInactive обязателен —
        // объект стартует выключенным, пока карт много.
        if (_deckCountText == null)
            _deckCountText = deck.GetComponentInChildren<TMP_Text>(true);

        _deckImage = deck.GetComponent<Image>();
        if (_deckImage == null) return;

        _deckSprite = _deckImage.sprite;
        _deckColor = _deckImage.color;
    }

    /// <summary>
    /// Показывает, есть ли ещё карты в колоде и сколько именно.
    ///
    /// <para>Сброс отличает пустоту от непустоты подменой спрайта
    /// (<c>DiscardPileView.SetSuit</c>), и колода делает то же самое — но
    /// умеет обойтись и без картинки: если её не задали, рубашка просто
    /// гаснет. Так признак появляется сразу, а не после работ в сцене.</para>
    ///
    /// <para>Счётчик показывается только на исходе колоды. Пока карт много,
    /// число ничего не сообщает и лишь шумит; когда их осталось несколько —
    /// это буквально счётчик оставшихся ходов, потому что партия кончается
    /// по опустевшей колоде. На нуле он снова прячется: там уже говорит сама
    /// колода, и «0» рядом с погасшей рубашкой — повтор.</para>
    /// </summary>
    private void UpdateDeckVisual(int remaining)
    {
        if (_deckCountText != null)
        {
            bool showCount = remaining > 0 && remaining <= _deckCountVisibleFrom;
            _deckCountText.gameObject.SetActive(showCount);
            if (showCount) _deckCountText.text = remaining.ToString();
        }

        if (_deckImage == null) return;

        bool empty = remaining <= 0;
        if (empty)
        {
            if (_emptyDeckSprite != null)
            {
                _deckImage.sprite = _emptyDeckSprite;
                _deckImage.color = Color.white;
            }
            else
            {
                _deckImage.color = _emptyDeckTint;
            }
        }
        else
        {
            _deckImage.sprite = _deckSprite;
            _deckImage.color = _deckColor;
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

        // Гасим предыдущий полёт этой же карты — см. пояснение в
        // PlayerHandUI.AnimateCardToHand. Два твина на одном трансформе
        // тянут его в разные стороны.
        card.transform.DOKill();

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
