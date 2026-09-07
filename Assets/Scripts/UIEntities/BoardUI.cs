using Assets.Scripts.Network;
using Assets.Scripts.UIEntities;
using Assets.Scripts.Views;
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

    [Tooltip("Сколько длится разворот карты лицом вверх, секунд. Разворот вложен в конец полёта, а не добавлен к нему: очередь событий ждёт анимации стола, поэтому удлинение полёта замедлило бы всю партию. Ноль — разворота нет, карта просто летит.")]
    [SerializeField] private float _flipDuration = 0.35f;

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
        var flip = FlipToFront(cardView);

        var animationTask = AnimateCardTransformToPositionInLayout(cardView, GameBoardPanel, flip);
        AnimationManager.Instance.Register(animationTask);
        await animationTask;
    }

    /// <summary>
    /// Разворот, который карта должна отыграть по дороге на стол — или
    /// <c>null</c>, если разворачивать нечего.
    ///
    /// <para>Карта оппонента лежит в его веере рубашкой вверх и должна
    /// повернуться лицом; своя уже лежит лицом вверх, и её разворот был бы
    /// неправдой. Карту, созданную только что (её представления не было —
    /// например, она пришла из колоды), кладём рубашкой: без этого она
    /// вспыхивает лицом прямо в колоде, и переворачивать в полёте уже
    /// нечего.</para>
    ///
    /// <para>Считать это надо <b>до</b> полёта: <c>IsFacedUp</c> переключится
    /// в середине разворота.</para>
    /// </summary>
    private static System.Action FlipToFront(CardView cardView)
    {
        if (cardView == null) return null;
        if (cardView.IsFacedUp) return null;

        cardView.FaceDown();
        return cardView.FaceUp;
    }

    private async void HandleCardMovedToBeforePlayer(Card card)
    {
        var unityManager = UnGameManager.Instance;
        var deck = unityManager.Deck;
        var cardView = _cardViewService.GetCardView(card)
            ?? _cardViewService.CreateCardView(card, deck);
        var flip = FlipToFront(cardView);

        Transform panel = null;
        if (card.Owner != null && _beforePlayerTables.TryGetValue(card.Owner, out var tbl))
            panel = tbl;
        // Fallback: send to general board if we couldn't resolve a seat.
        if (panel == null) panel = GameBoardPanel;

        var animationTask = AnimateCardTransformToPositionInLayout(cardView, panel, flip);
        AnimationManager.Instance.Register(animationTask);
        await animationTask;
    }

    private async void HandleCardMovedToTimeOfDaySlot(Card card)
    {
        var unityManager = UnGameManager.Instance;
        var deck = unityManager.Deck;
        CardView cardView = _cardViewService.GetCardView(card)
            ?? _cardViewService.CreateCardView(card, deck);
        var flip = FlipToFront(cardView);

        // Единственный обработчик, который раньше не регистрировал свою
        // анимацию — поэтому карту дня/ночи никто не ждал, и раздача карт
        // начиналась поверх ещё летящей карты.
        //
        // Паузы здесь нет намеренно. Карта уже полежала на столе и прочитана,
        // а в начале партии эта же пауза давала две секунды пустого экрана
        // перед вылетом карты Ночи — ту самую «непонятную задержку».
        var animationTask = AnimateCardTransformToPosition(cardView, TimeOfDaySlot, 0, flip);
        AnimationManager.Instance.Register(animationTask);
        if (!await animationTask) return;
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

            // В сброс карта уходит рубашкой вверх: стопка сброса — рубашка, и
            // карта должна лечь в неё так же, как легла бы на столе. Разворот
            // идёт в конце полёта, поэтому прочитать карту он не мешает — на
            // это есть пауза выше, и всю её карта лежит лицом.
            //
            // Уже перевёрнутой вертеться незачем: так уходит карта, сброшенная
            // из чужой руки, — её лица никто и не видел.
            var flip = cardView.IsFacedUp ? cardView.FaceDown : (System.Action)null;

            var animationTask = AnimateCardTransformToPosition(
                cardView, DiscardPile, leavingTimeOfDaySlot ? 0 : _discardReadDelay, flip);
            AnimationManager.Instance.Register(animationTask);
            // Не долетела — значит карту перехватил следующий полёт и она жива
            // где-то ещё. Уничтожать её здесь нельзя.
            if (!await animationTask) return;
            if (cardView == null) return;

            if (DiscardPileView.Instance != null) DiscardPileView.Instance.SetSuit();
            Destroy(cardView.gameObject);
        }
        else
        {
            Debug.LogError($"CardView for {card.Name} not found.");
        }
    }

    /// <returns>Долетела ли карта — см. <see cref="CardFlight.Fly"/>.</returns>
    public async Task<bool> AnimateCardTransformToPosition(CardView card, Transform to, int delayMs,
                                                           System.Action onFlipMidpoint = null)
    {
        if (delayMs > 0) await Task.Delay(delayMs);
        // Пауза и полёт переживают перезагрузку сцены (выход в меню посреди
        // партии), а карта с местом назначения — нет. Проверяем после каждого
        // await: уничтоженный объект Unity равен null по своей перегрузке ==,
        // но обращение к нему бросает MissingReferenceException — а бросать
        // его отсюда некуда, эти задачи ждут из async void.
        if (card == null || to == null) return false;

        // Гашение предыдущего полёта той же карты — внутри CardFlight.Fly:
        // два твина на одном трансформе тянут его в разные стороны.
        return await CardFlight.Fly(card, to, _moveDuration, _flipDuration, onFlipMidpoint);
    }

    /// <returns>Села ли карта в раскладку назначения.</returns>
    public async Task<bool> AnimateCardTransformToPositionInLayout(CardView card, Transform to,
                                                                   System.Action onFlipMidpoint = null)
    {
        // Перехваченный полёт сажать нельзя: карту уже ведёт следующий, и
        // назначенный здесь родитель дрался бы с ним.
        if (!await CardFlight.Fly(card, to, _moveDuration, _flipDuration, onFlipMidpoint)) return false;
        if (card == null || to == null) return false;

        // Полёт оставил карту в корне канваса — родителя назначаем здесь.
        //
        // worldPositionStays: true. Раньше здесь было false, потому что панель
        // повёрнута к центру стола и сохранение мировой позы оставило бы карте
        // компенсирующий локальный трансформ. Теперь наоборот: карта уже стоит
        // ровно там, куда её поставит раскладка (CardFlight замерил это место
        // заранее), и мировую позу надо именно сохранить. Компенсация живёт
        // ровно до пересборки строкой ниже, которая перепишет карте и позицию,
        // и поворот, и масштаб.
        card.transform.SetParent(to, true);

        var toRect = to.GetComponent<RectTransform>();
        if (toRect != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(toRect);
        await Task.Yield();
        return true;
    }
}
