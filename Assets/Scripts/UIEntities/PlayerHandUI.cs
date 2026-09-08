using Assets.Scripts.Network;
using Assets.Scripts.UIEntities;
using Assets.Scripts.UIEntities.Components;
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

    [Tooltip("Сколько длится разворот карты в полёте, секунд. Вложен в конец полёта, а не добавлен к нему. Ноль — разворота нет, карта просто летит.")]
    [SerializeField] private float _flipDuration = 0.35f;

    [Header("Сцена Цыганки")]
    [Tooltip("Сколько держать раскрытые руки на виду, секунд. Константа: столько же при двух картах на столе и при двадцати. Очередь событий на это время встаёт, поэтому очки и взятые карты придут уже после показа.")]
    [SerializeField] private float _revealSeconds = 5f;

    [Tooltip("На сколько чужая рука выдвигается к центру стола, пикселей канваса. Фиксированное расстояние, а НЕ доля пути: доля сводила бы все верхние руки в одну точку, и при трёх-четырёх игроках они наезжали бы друг на друга.")]
    [SerializeField] private float _revealForwardOffset = 130f;

    [Tooltip("Насколько развести карты в раскрытой руке, пикселей между соседними. У трёх вееров в сцене radius равен нулю, из-за чего все карты лежат в одной точке и различаются только наклоном — без этого их не разобрать. Урезается по ширине места, чтобы руки не наезжали на соседей.")]
    [SerializeField] private float _revealFanSpread = 115f;

    [Tooltip("Отступ от края экрана, пикселей. Раскрытая рука опускается ровно настолько, чтобы верхушки карт не вылезали за кадр, плюс этот запас. Величина замеряется, а не подбирается: она зависит от масштаба карт места, их числа и разрешения.")]
    [SerializeField] private float _revealScreenMargin = 24f;

    [Tooltip("Сколько рука едет туда и обратно, секунд.")]
    [SerializeField] private float _revealMoveDuration = 0.4f;

    [Tooltip("На сколько отдельная карта выступает ЕЩЁ дальше, когда за неё дают очко или карту, пикселей канваса.")]
    [SerializeField] private float _showcaseOffset = 170f;

    [Tooltip("Во сколько раз увеличивается выступающая карта.")]
    [SerializeField] private float _showcaseScale = 1.4f;

    [Tooltip("Сколько карта выезжает и заезжает, секунд.")]
    [SerializeField] private float _showcaseMoveDuration = 0.35f;

    [Tooltip("Сколько выступившая карта стоит на виду, секунд. Умножается на число раскрытых мест и цыганок, так что удлинять её дорого — на изучение всех карт есть отдельные 5 секунд выше.")]
    [SerializeField] private float _showcaseHold = 1.2f;

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
        _view.OnHandsRevealed += HandleHandsRevealed;
        _view.OnCardShowcased += HandleCardShowcased;
        // Без этого обрыв партии посреди показа оставил бы руки открытыми и
        // выехавшими: события, которое закрыло бы их штатно, уже не будет.
        _view.OnGameAborted += HandleGameAbortedDuringReveal;

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
        //
        // Исключение — показ рук: карты за цыганок прилетают сразу после него,
        // и руки не должны поехать закрываться, пока эти карты ещё в воздухе.
        AnimationManager.Instance.Register(animationTask,
                                          blocksEventQueue: _handsRevealed,
                                          staggerSeconds: _dealStagger);
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
    /// остаётся рубашкой.</para>
    ///
    /// <para><b>Рубашка здесь — решение показа, а не незнание.</b> Клиент
    /// знает каждую карту в каждой руке: взятие рассылается всей комнате, а
    /// порядок колоды целиком приезжает в <c>GameStartedEvent</c>. Потому
    /// Цыганка и обходится одним событием (см. <see cref="RevealAllHands"/>) —
    /// раскрывать нечего, достаточно перестать прятать.</para>
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

    // ---- Сцена Цыганки ----
    //
    // Чужие руки выезжают вперёд, выпрямляются, раскрываются в ряд и
    // переворачиваются лицом; пять секунд на изучение; потом каждое место и
    // каждая цыганка по очереди выступают вперёд за своим очком или картой; в
    // конце всё едет обратно.
    //
    // Порядок задаёт сервер, а не клиент: он шлёт события по одному, а очередь
    // применяет их только между анимациями. Достаточно отыграть каждое событие
    // блокирующей задачей — последовательность сложится сама.

    // Что было рубашкой до раскрытия: только это и переворачиваем обратно.
    // Своя рука и так открыта, и без этого списка она ушла бы рубашкой вниз —
    // то есть карта «раскрыла» бы карты игрока от него самого.
    private readonly List<CardView> _revealedViews = new();

    // Панели, которые выехали, и их исходная поза целиком: вернуть надо и
    // положение, и поворот, и раскладку веера, и место в порядке отрисовки.
    private readonly Dictionary<RectTransform, PanelPose> _movedPanels = new();

    private readonly struct PanelPose
    {
        public readonly Vector3 Position;
        public readonly Quaternion Rotation;
        public readonly int SiblingIndex;
        public readonly FanLayoutGroup Fan;
        public readonly float Spread;
        public readonly float Angle;

        public PanelPose(Vector3 position, Quaternion rotation, int siblingIndex,
                         FanLayoutGroup fan, float spread, float angle)
        {
            Position = position;
            Rotation = rotation;
            SiblingIndex = siblingIndex;
            Fan = fan;
            Spread = spread;
            Angle = angle;
        }
    }

    // Идёт ли показ. Пока идёт, прилёт карты в руку блокирует очередь: цыганка
    // должна заезжать обратно ПОСЛЕ того, как выданная карта долетела.
    private bool _handsRevealed;

    // Кто сейчас выступает: клон на канвасе и оригинал, у которого мы погасили
    // графику. Оригинал не двигаем — он лежит в раскладке веера, и любая
    // пересборка вернула бы его на место посреди движения.
    private CardView _showcaseClone;
    private CardView _showcaseOriginal;

    private async void HandleHandsRevealed(Player by, bool revealed)
    {
        var task = revealed ? RevealAllHands() : HideAllHands();
        // Блокирующая: шаги сцены, очки и взятия придут следующими событиями и
        // должны примениться ПОСЛЕ того, как руки показали.
        AnimationManager.Instance.Register(task);
        await task;
    }

    /// <summary>
    /// Руки открываются: чужие панели выезжают вперёд, выпрямляются и
    /// раскрываются в ряд, рубашки переворачиваются. Держим
    /// <see cref="_revealSeconds"/> — константу, не зависящую от числа карт.
    ///
    /// <para><b>Двигаем панель, а не карты в ней.</b> Карты лежат в раскладке
    /// веера, которая сама пишет им позицию, поворот и масштаб на каждой
    /// пересборке; панель же никакой раскладке не принадлежит — её ставит
    /// <c>SeatLayout</c> один раз за партию.</para>
    ///
    /// <para><b>Панель выпрямляем.</b> У верхних мест она повёрнута на 180
    /// градусов, и без этого карты показывались бы вверх ногами. Обычно так
    /// делать нельзя — <c>FanLayoutGroup.fanUpwards</c> рассчитан на то, что
    /// разворот несёт панель, — но здесь мы тут же зануляем угол веера, а при
    /// нулевом угле и нулевом радиусе направление веера ни на что не влияет:
    /// вся дуга вырождается в точку.</para>
    ///
    /// <para><b>Панель поднимаем в порядке отрисовки.</b> У части мест карты,
    /// выложенные перед игроком, рисуются поверх руки (<c>HandBehindTable</c>
    /// в <c>SeatLayout.ApplyDrawOrder</c>) — и накрывали бы то самое, что мы
    /// показываем.</para>
    ///
    /// <para>Своя панель остаётся на месте: её карты и так открыты, а
    /// выехавшая собственная рука только загородила бы стол.</para>
    /// </summary>
    private async Task RevealAllHands()
    {
        if (_playerHandPanels == null) return;

        _revealedViews.Clear();
        _movedPanels.Clear();
        _handsRevealed = true;

        var boardCentre = TableCentre();
        var moves = new List<Task>();

        foreach (var pair in _playerHandPanels)
        {
            var panel = pair.Value as RectTransform;
            if (panel == null) continue;

            bool mine = pair.Key == _view.LocalPlayer;
            foreach (Transform child in panel)
            {
                var view = child.GetComponent<CardView>();
                if (view == null || view.IsFacedUp) continue;
                _revealedViews.Add(view);
                view.FaceUp();
            }

            if (mine) continue;

            var fan = panel.GetComponent<FanLayoutGroup>();
            _movedPanels[panel] = new PanelPose(
                panel.position, panel.rotation, panel.GetSiblingIndex(),
                fan, fan != null ? fan.extraSpread : 0f, fan != null ? fan.angle : 0f);

            panel.SetAsLastSibling();

            // Двигаем ТОЛЬКО по вертикали, к центру стола. Все оппоненты сидят
            // сверху, так что это движение вниз, а горизонтальные расстояния
            // между их руками остаются прежними — иначе руки соседних мест
            // наезжают друг на друга при трёх-четырёх игроках.
            float toCentre = Mathf.Sign(boardCentre.y - panel.position.y);
            var target = panel.position + Vector3.up * (toCentre * _revealForwardOffset);

            float spread = FittingSpread(panel, fan);
            // И опускаем ещё настолько, насколько рука иначе вылезла бы за
            // верхний край экрана: выпрямленная карта растёт вверх от своего
            // пивота (низ-центр), и у верхних мест её верхушка уходила за кадр.
            target.y -= DropToFitOnScreen(panel, fan, spread, target);

            moves.Add(MovePanel(panel, target, Quaternion.identity));
            if (fan != null) moves.Add(OpenFan(panel, fan, spread, angle: 0f));
        }

        if (moves.Count > 0) await Task.WhenAll(moves);

        await AnimationManager.Delay(_revealSeconds);
    }

    /// <summary>
    /// Насколько развести карты, чтобы ряд остался внутри своего места.
    ///
    /// <para>Мест за столом до четырёх, и раскрытая рука шириной в запрошенные
    /// 115 пикселей на карту попросту не помещается между соседями — руки
    /// наезжают друг на друга. Поэтому раздвижка урезается по ширине панели:
    /// при нехватке места карты наползают внутри своего места, а не вываливаются
    /// к соседу. Тот же приём и та же причина, что в
    /// <c>CardTableLayout.UpdateLayout</c>.</para>
    /// </summary>
    private float FittingSpread(RectTransform panel, FanLayoutGroup fan)
    {
        int count = 0;
        float cardWidth = 0f;
        foreach (RectTransform child in panel)
        {
            count++;
            cardWidth = Mathf.Max(cardWidth, child.rect.width);
        }
        if (count < 2) return 0f;

        float visualWidth = cardWidth * Mathf.Max(0.01f, fan.scale);
        float available = panel.rect.width - visualWidth;
        if (available <= 0f) return 0f;

        return Mathf.Min(_revealFanSpread, available / (count - 1));
    }

    /// <summary>
    /// На сколько ещё опустить руку, чтобы она целиком поместилась на экране.
    ///
    /// <para><b>Зачем.</b> Карта растёт вверх от своего пивота (низ-центр), а
    /// показ выпрямляет панель — и у верхних мест верхушка карты уходила за
    /// край кадра. Подобрать одно число нельзя: высота зависит от масштаба
    /// карт места, числа карт и разрешения экрана.</para>
    ///
    /// <para><b>Поэтому меряем, а не считаем.</b> Панель примеряет конечную
    /// позу, раскладка пересобирается, у детей спрашиваются мировые углы — и
    /// всё возвращается назад. Строго синхронно, без единого <c>await</c>:
    /// промежуточное состояние не доживает до отрисовки. Тот же приём, что в
    /// <c>CardFlight.ProbeLanding</c>.</para>
    /// </summary>
    private float DropToFitOnScreen(RectTransform panel, FanLayoutGroup fan, float spread, Vector3 target)
    {
        var root = panel.root as RectTransform;
        if (root == null) return 0f;

        var wasPosition = panel.position;
        var wasRotation = panel.rotation;
        float wasAngle = fan != null ? fan.angle : 0f;
        float wasSpread = fan != null ? fan.extraSpread : 0f;

        panel.position = target;
        panel.rotation = Quaternion.identity;
        if (fan != null) { fan.angle = 0f; fan.extraSpread = spread; }
        LayoutRebuilder.ForceRebuildLayoutImmediate(panel);

        float top = float.NegativeInfinity;
        var corners = new Vector3[4];
        foreach (Transform child in panel)
        {
            if (!(child is RectTransform rect)) continue;
            rect.GetWorldCorners(corners);
            for (int i = 0; i < 4; i++) top = Mathf.Max(top, corners[i].y);
        }

        panel.position = wasPosition;
        panel.rotation = wasRotation;
        if (fan != null) { fan.angle = wasAngle; fan.extraSpread = wasSpread; }
        LayoutRebuilder.ForceRebuildLayoutImmediate(panel);

        if (float.IsNegativeInfinity(top)) return 0f;

        // GetWorldCorners отдаёт углы против часовой от левого нижнего, так что
        // [1] — левый верхний: это и есть верхний край канваса.
        root.GetWorldCorners(corners);
        return Mathf.Max(0f, top - (corners[1].y - _revealScreenMargin));
    }

    /// <summary>Руки закрываются: панели едут назад, веер собирается, лица гаснут.</summary>
    private async Task HideAllHands()
    {
        _handsRevealed = false;

        // Показ мог оборваться на выехавшей карте. Прибираем прежде, чем
        // гасить руки.
        RestoreShowcase();

        var moves = new List<Task>();
        foreach (var pair in _movedPanels)
        {
            if (pair.Key == null) continue;
            moves.Add(MovePanel(pair.Key, pair.Value.Position, pair.Value.Rotation));
            if (pair.Value.Fan != null)
                moves.Add(OpenFan(pair.Key, pair.Value.Fan, pair.Value.Spread, pair.Value.Angle));
        }

        if (moves.Count > 0) await Task.WhenAll(moves);

        // Проверка после await обязательна: сцену могли перезагрузить, пока
        // панели ехали. Уничтоженный объект Unity равен null по своей
        // перегрузке ==, но обращение к нему бросает MissingReferenceException,
        // а бросать его отсюда некуда — задачу ждут из async void.
        foreach (var pair in _movedPanels)
            if (pair.Key != null) pair.Key.SetSiblingIndex(pair.Value.SiblingIndex);

        foreach (var view in _revealedViews)
            if (view != null) view.FaceDown();

        _revealedViews.Clear();
        _movedPanels.Clear();
    }

    /// <summary>
    /// Раскрывает веер в ровный ряд: карты разъезжаются в стороны и перестают
    /// наклоняться друг относительно друга.
    ///
    /// <para><b>Раздвигать приходится вручную, потому что веера в сцене —
    /// не веера.</b> <c>FanLayoutGroup.ArrangeCards</c> считает позицию карты
    /// как <c>sin(угол) * radius</c>, а у трёх вееров из четырёх
    /// <c>radius</c> равен нулю: все карты получают одну и ту же точку и
    /// различаются только наклоном. Такую стопку не разобрать, сколько её ни
    /// увеличивай, — потому карты и разводят, а не укрупняют.</para>
    ///
    /// <para>Угол зануляем заодно: наклон, рассчитанный на дугу, без дуги
    /// превращает ряд в частокол, а у повёрнутых мест ещё и смотрит в
    /// обратную сторону.</para>
    ///
    /// <para>Раскладку приходится пересобирать на каждом кадре: смена поля
    /// сама по себе не помечает её грязной. Для веера из пяти карт это дёшево.</para>
    /// </summary>
    private async Task OpenFan(RectTransform panel, FanLayoutGroup fan, float spread, float angle)
    {
        float fromSpread = fan.extraSpread;
        float fromAngle = fan.angle;

        // Своей ручки у раскрытия нет намеренно: оно идёт вместе с выездом
        // руки, и раскрыться позже, чем приехала рука, незачем.
        await DOTween.To(() => 0f, (float u) =>
        {
            if (fan == null || panel == null) return;
            fan.extraSpread = Mathf.LerpUnclamped(fromSpread, spread, u);
            fan.angle = Mathf.LerpUnclamped(fromAngle, angle, u);
            LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
        }, 1f, _revealMoveDuration)
            .SetEase(Ease.OutQuad)
            .SetTarget(fan)
            .AsyncWaitForCompletion();

        if (fan == null || panel == null) return;
        fan.extraSpread = spread;
        fan.angle = angle;
        LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
    }

    private async void HandleCardShowcased(Card card, bool stepOut)
    {
        var task = stepOut ? ShowcaseOut(card) : ShowcaseBack();
        AnimationManager.Instance.Register(task);
        await task;
    }

    /// <summary>
    /// Одна карта выступает вперёд — за неё сейчас дадут очко или карту.
    ///
    /// <para><b>Выступает клон, а оригинал гаснет на месте.</b> Оригинал лежит
    /// в раскладке веера, и её пересборка (а она случится: выданная карта
    /// прилетит в ту же руку) вернула бы его в строй посреди движения. У клона
    /// нет ни родителя-раскладки, ни записи в словаре представлений — см.
    /// <c>CardViewService.CreateSingleCardViewClone</c>.</para>
    ///
    /// <para>Гасим оригинал через <c>Graphic.enabled</c>, а не
    /// <c>SetActive(false)</c>: выключенный объект выпадает из
    /// <c>rectChildren</c>, и веер тут же сомкнулся бы.</para>
    /// </summary>
    private async Task ShowcaseOut(Card card)
    {
        // Предыдущая карта могла не успеть заехать — обрыв показа, спешка.
        RestoreShowcase();

        var original = _cardViewService.GetCardView(card);
        if (original == null) return;

        var root = original.transform.root;
        if (root == original.transform) return;

        _showcaseOriginal = original;
        SetGraphicsVisible(original, false);

        _showcaseClone = _cardViewService.CreateSingleCardViewClone(card, root);
        if (_showcaseClone == null)
        {
            RestoreShowcase();
            return;
        }

        var clone = _showcaseClone.transform;
        clone.position = original.transform.position;
        clone.rotation = Quaternion.identity;
        clone.localScale = ScaleUnder(root, original.transform);
        _showcaseClone.FaceUp();

        var toCentre = Direction(clone.position, TableCentre());
        await MoveClone(clone,
                        clone.position + toCentre * _showcaseOffset,
                        clone.localScale * _showcaseScale);

        // Пауза здесь, а не на заезде: за неё сервер успеет прислать очко или
        // взятие, и игрок увидит их, пока карта на виду.
        await AnimationManager.Delay(_showcaseHold);
    }

    /// <summary>Выступавшая карта возвращается в строй.</summary>
    private async Task ShowcaseBack()
    {
        if (_showcaseClone == null)
        {
            RestoreShowcase();
            return;
        }

        var clone = _showcaseClone.transform;
        var home = clone.position;
        var scale = clone.localScale;
        if (_showcaseOriginal != null)
        {
            home = _showcaseOriginal.transform.position;
            scale = ScaleUnder(clone.root, _showcaseOriginal.transform);
        }

        await MoveClone(clone, home, scale);
        RestoreShowcase();
    }

    /// <summary>
    /// Убирает клон и возвращает оригиналу графику. Идемпотентна: зовётся и
    /// штатно, и при обрыве показа.
    /// </summary>
    private void RestoreShowcase()
    {
        if (_showcaseClone != null) Destroy(_showcaseClone.gameObject);
        _showcaseClone = null;

        if (_showcaseOriginal != null) SetGraphicsVisible(_showcaseOriginal, true);
        _showcaseOriginal = null;
    }

    private static void SetGraphicsVisible(CardView view, bool visible)
    {
        foreach (var graphic in view.GetComponentsInChildren<Graphic>(true))
            graphic.enabled = visible;
    }

    /// <summary>
    /// Каким должен быть <c>localScale</c> у ребёнка <paramref name="parent"/>,
    /// чтобы он выглядел того же размера, что <paramref name="sample"/>.
    ///
    /// <para>Просто взять <c>lossyScale</c> нельзя: канвас масштабируется под
    /// разрешение экрана (<c>CanvasScaler</c>), и на не-1920×1080 его масштаб
    /// не единица — присвоенный как локальный, он применился бы дважды.</para>
    /// </summary>
    private static Vector3 ScaleUnder(Transform parent, Transform sample)
    {
        var p = parent.lossyScale;
        var s = sample.lossyScale;
        return new Vector3(
            Mathf.Approximately(p.x, 0f) ? s.x : s.x / p.x,
            Mathf.Approximately(p.y, 0f) ? s.y : s.y / p.y,
            Mathf.Approximately(p.z, 0f) ? s.z : s.z / p.z);
    }

    private void HandleGameAbortedDuringReveal(string reason, Player who) => AbandonReveal();

    private void OnDestroy() => RestoreShowcase();

    /// <summary>
    /// Обрыв партии посреди сцены. Возвращаем всё на место немедленно, без
    /// анимаций: события, которое закрыло бы руки штатно, уже не будет —
    /// <c>GameAbortedEvent</c> идёт мимо очереди и чистит её.
    /// </summary>
    private void AbandonReveal()
    {
        if (!_handsRevealed) return;
        _handsRevealed = false;

        RestoreShowcase();

        foreach (var pair in _movedPanels)
        {
            if (pair.Key == null) continue;
            pair.Key.DOKill();
            pair.Key.position = pair.Value.Position;
            pair.Key.rotation = pair.Value.Rotation;
            pair.Key.SetSiblingIndex(pair.Value.SiblingIndex);

            if (pair.Value.Fan == null) continue;
            pair.Value.Fan.DOKill();
            pair.Value.Fan.extraSpread = pair.Value.Spread;
            pair.Value.Fan.angle = pair.Value.Angle;
            LayoutRebuilder.ForceRebuildLayoutImmediate(pair.Key);
        }
        _movedPanels.Clear();

        foreach (var view in _revealedViews)
            if (view != null) view.FaceDown();
        _revealedViews.Clear();
    }

    private Vector3 TableCentre()
    {
        var board = UnGameManager.Instance != null ? UnGameManager.Instance.GameBoardPanel : null;
        return board != null ? board.position : Vector3.zero;
    }

    /// <summary>
    /// Направление «к центру стола». Вырожденный случай (объект ровно в центре)
    /// уводим вверх — лишь бы не в нулевой вектор.
    /// </summary>
    private static Vector3 Direction(Vector3 from, Vector3 to)
    {
        var d = to - from;
        return d.sqrMagnitude > 0.0001f ? d.normalized : Vector3.up;
    }

    private Task MoveClone(Transform clone, Vector3 position, Vector3 scale)
        => MoveTransform(clone, position, scale, _showcaseMoveDuration);

    /// <summary>
    /// Плавно ведёт панель руки в позу: положение и поворот одним твином.
    /// Масштаб не трогаем — карты разводит веер, а не увеличение.
    /// </summary>
    private async Task MovePanel(RectTransform panel, Vector3 position, Quaternion rotation)
    {
        if (panel == null) return;

        panel.DOKill();
        var fromPosition = panel.position;
        var fromRotation = panel.rotation;

        await DOTween.To(() => 0f, (float u) =>
        {
            if (panel == null) return;
            panel.position = Vector3.LerpUnclamped(fromPosition, position, u);
            panel.rotation = Quaternion.Slerp(fromRotation, rotation, u);
        }, 1f, _revealMoveDuration)
            .SetEase(Ease.OutQuad)
            // Без цели твин не найдётся по transform.DOKill().
            .SetTarget(panel)
            .AsyncWaitForCompletion();

        if (panel == null) return;
        panel.position = position;
        panel.rotation = rotation;
    }

    /// <summary>
    /// Плавно ведёт клон в позу: положение и масштаб одним твином — по той же
    /// причине, что и в <see cref="CardFlight"/>: два твина на одном
    /// трансформе в этой игре уже дважды разъезжались друг с другом.
    /// </summary>
    private static async Task MoveTransform(Transform t, Vector3 position, Vector3 scale, float duration)
    {
        if (t == null) return;

        t.DOKill();
        var fromPosition = t.position;
        var fromScale = t.localScale;

        await DOTween.To(() => 0f, (float u) =>
        {
            if (t == null) return;
            t.position = Vector3.LerpUnclamped(fromPosition, position, u);
            t.localScale = Vector3.LerpUnclamped(fromScale, scale, u);
        }, 1f, duration)
            .SetEase(Ease.OutQuad)
            .SetTarget(t)
            .AsyncWaitForCompletion();

        if (t == null) return;
        t.position = position;
        t.localScale = scale;
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
