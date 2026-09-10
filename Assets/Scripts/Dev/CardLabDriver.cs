using System.Collections.Generic;
using System.Linq;
using ScaryTales;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// Лаборатория карты: раскладывает всю колоду настоящими картами прямо в редакторе,
/// без Play и без сети.
///
/// <para>Верстать карту по префабу вслепую нельзя: в Prefab Mode видно одну карту с
/// плейсхолдерами «Карта»/«Описание», а проверять нужно совсем другое — самое длинное
/// описание в колоде, рубашку, подсветку и, главное, три масштаба сразу. Карта живёт на
/// столе при 1.0, в руке при 1.3 и раскрывается под курсором при 2.3 — вёрстка обязана
/// работать во всех трёх, а шрифт 10 при 1.0 нечитаем в принципе.</para>
///
/// <para>Карты создаются как <b>инстансы префаба</b>, а не копии: правка префаба тут же
/// прилетает во всю галерею, и обратно — подвинул рект на одной карте, «Overrides ▸ Apply
/// All», и остальные девятнадцать перестроились. Это и есть «прямой эфир».</para>
///
/// <para>Расставляет карты либо драйвер, либо ты — см. <see cref="Arrangement"/>.
/// В режиме сетки карты временные и в файл сцены не попадают; в ручном они обычные
/// объекты сцены, и перестройка обновляет им данные, не трогая положение.</para>
/// </summary>
[ExecuteAlways]
public class CardLabDriver : MonoBehaviour
{
    [Header("Что раскладываем")]
    [Tooltip("Пусто — возьмём Resources/CardPrefab, тот же самый, что и в игре.")]
    [SerializeField] private GameObject _cardPrefab;

    [Tooltip("Показать только карты, в названии которых есть эта строка. Пусто — все.")]
    [SerializeField] private string _onlyCard = "";

    [Header("Раскладка")]
    [Tooltip("Grid — расставляет драйвер, карты временные и живут только в редакторе. " +
             "Manual — расставляешь ты, карты сохраняются в сцене, и перестройка их не двигает.")]
    [SerializeField] private Arrangement _arrangement = Arrangement.Grid;

    [Header("Сетка (только для Grid)")]
    [Tooltip("Сколько рядов. Колонки считаются сами: столько, чтобы влезла вся колода.")]
    [SerializeField] private int _rows = 3;

    [Tooltip("Шаг сетки. Карта 150x250, так что 180x300 — это по 30 и 50 пикселей зазора.")]
    [SerializeField] private Vector2 _spacing = new Vector2(180f, 300f);

    [Tooltip("Отступ от краёв холста.")]
    [SerializeField] private float _margin = 40f;

    [Header("Правая колонка (только для Grid)")]
    [Tooltip("Название карты для превью. Пусто — первая из колоды.")]
    [SerializeField] private string _previewCard = "";

    [Tooltip("Масштаб превью при наведении. Столько ставит HoverCardScaler.")]
    [SerializeField] private float _previewScale = 2.3f;

    [Tooltip("Показать под превью карту такой, какой её видно в руке местного игрока: " +
             "HandLayoutGroup принудительно ужимает рект до cellSize, а не просто масштабирует, " +
             "и это единственное место, где лицо верстается в чужом ректе.")]
    [SerializeField] private bool _showHandCell = true;

    [Tooltip("HandLayoutGroup.cellSize из сцены. Карта в руке живёт именно в этом ректе.")]
    [SerializeField] private Vector2 _handCellSize = new Vector2(100f, 150f);

    [SerializeField] private float _handCellScale = 1.3f;

    [Header("Состояния")]
    [SerializeField] private bool _faceDown;
    [SerializeField] private bool _highlight;

    [Tooltip("Залить во все карты самое длинное описание из колоды — проверка на переполнение.")]
    [SerializeField] private bool _stressTest;

    [Tooltip("Оставить живыми DragAndDrop / HoverCardScaler / CardHoverCursor. " +
             "В лаборатории им не на чем работать, поэтому по умолчанию выключены.")]
    [SerializeField] private bool _interactive;

    /// <summary>
    /// Кто расставляет карты.
    ///
    /// <para><see cref="Grid"/> — драйвер. Карты помечены
    /// <see cref="HideFlags.DontSave"/>, то есть в файл сцены не попадают и
    /// не дают диф на каждую перестройку. Расплата: подвинутая руками карта
    /// живёт до ближайшей перестройки, а та случается сама — при сохранении
    /// префаба, при импорте арта, при любой правке поля в инспекторе.</para>
    ///
    /// <para><see cref="Manual"/> — ты. Карты создаются один раз, сохраняются
    /// в сцене как обычные объекты и переживают перестройку: она обновляет им
    /// данные и состояние, но <b>не трогает трансформ</b>. Ставь этот режим,
    /// когда собираешь свою композицию.</para>
    /// </summary>
    public enum Arrangement
    {
        Grid,
        Manual,
    }

    private static readonly Vector2 CardSize = new Vector2(150f, 250f);
    private const float LabelHeight = 22f;

    /// <summary>Префикс в имени объекта. По нему ручной режим узнаёт, что за карта.</summary>
    private const string CardNamePrefix = "Card — ";

    private bool _rebuildQueued;

    private void OnEnable()
    {
        RequestRebuild();
#if UNITY_EDITOR
        // Правка префаба обязана возвращаться в галерею сама — ради этого лаборатория
        // и заводилась. projectChanged ловит сохранение префаба (в Prefab Mode оно
        // автоматическое) и импорт нового арта; prefabInstanceUpdated — «Apply All»
        // с карты прямо в сцене.
        EditorApplication.projectChanged -= RequestRebuild;
        EditorApplication.projectChanged += RequestRebuild;
        PrefabUtility.prefabInstanceUpdated -= OnPrefabInstanceUpdated;
        PrefabUtility.prefabInstanceUpdated += OnPrefabInstanceUpdated;
#endif
    }

    private void OnDisable()
    {
#if UNITY_EDITOR
        EditorApplication.projectChanged -= RequestRebuild;
        PrefabUtility.prefabInstanceUpdated -= OnPrefabInstanceUpdated;
#endif
    }

    private void OnValidate() => RequestRebuild();

#if UNITY_EDITOR
    private void OnPrefabInstanceUpdated(GameObject instance) => RequestRebuild();
#endif

    /// <summary>
    /// Перестройка откладывается на следующий тик редактора. Из OnValidate нельзя ни
    /// удалять объекты, ни звать SendMessage — Unity ругается и половина галереи остаётся
    /// от прошлого прохода.
    ///
    /// <para>Перестроить нужно именно всё заново, а не только спрайты: сохранение префаба
    /// рассылает свои значения по инстансам и затирает всё, что скрипт проставил в
    /// эти же поля — арт карты в том числе. Зарегистрированным override это не является,
    /// так что защититься нечем, только собрать галерею заново.</para>
    /// </summary>
    private void RequestRebuild()
    {
        if (_rebuildQueued) return;
        _rebuildQueued = true;
#if UNITY_EDITOR
        EditorApplication.delayCall += () =>
        {
            if (this == null) return;
            _rebuildQueued = false;
            Rebuild();
        };
#else
        _rebuildQueued = false;
        Rebuild();
#endif
    }

    [ContextMenu("Rebuild")]
    public void Rebuild()
    {
        // В ручном режиме чистить нельзя: композиция — это и есть результат
        // работы, ради которого сюда пришли.
        if (_arrangement == Arrangement.Grid) Clear();

        GameObject prefab = _cardPrefab != null
            ? _cardPrefab
            : Resources.Load<GameObject>("CardPrefab");

        if (prefab == null)
        {
            Debug.LogWarning("[CardLab] Не найден CardPrefab. Задайте префаб в инспекторе " +
                             "или проверьте Assets/Resources/CardPrefab.prefab.");
            return;
        }

        List<Card> cards = GameBuilder.MakeCardTemplates();

        // Стресс-тест берёт самое длинное описание в колоде, а не выдуманную строку:
        // если влезло оно — влезет всё, что есть в игре.
        string longest = cards
            .Select(c => c.EffectDescription ?? string.Empty)
            .OrderByDescending(d => d.Length)
            .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(_onlyCard))
            cards = cards
                .Where(c => c.Name.IndexOf(_onlyCard, System.StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

        if (cards.Count == 0)
        {
            Debug.LogWarning($"[CardLab] Под фильтр «{_onlyCard}» не подошла ни одна карта.");
            return;
        }

        if (_arrangement == Arrangement.Manual)
        {
            RefreshInPlace(prefab, cards, longest);
            ReportMissingArt(cards);
            return;
        }

        BuildLayout(prefab, cards, longest);
        ReportMissingArt(cards);
    }

    /// <summary>
    /// Раскладка по умолчанию: вся колода рядами слева в натуральную величину,
    /// справа — одна карта так, как её видно при наведении.
    ///
    /// <para>Раньше эталонный ряд стоял сверху во всю ширину, и карта в 2.3×
    /// съедала больше половины высоты холста — колода уезжала за нижний край,
    /// и её приходилось либо листать, либо ужимать до нечитаемого. Превью
    /// сбоку убирает конфликт: обе части помещаются в 1920×1080 целиком и без
    /// уменьшения.</para>
    /// </summary>
    private void BuildLayout(GameObject prefab, List<Card> cards, string longest)
    {
        float canvasWidth = 1920f, canvasHeight = 1080f;
        var canvas = GetComponentInParent<Canvas>();
        if (canvas != null)
        {
            var r = ((RectTransform)canvas.transform).rect;
            if (r.width > 1f && r.height > 1f) { canvasWidth = r.width; canvasHeight = r.height; }
        }

        float sideWidth = CardSize.x * Mathf.Max(1f, _previewScale);
        float sideX = canvasWidth - _margin - sideWidth;

        BuildSideColumn(prefab, cards, longest, sideX, sideWidth);
        BuildGallery(prefab, cards, longest, sideX - _margin, canvasHeight);
    }

    /// <summary>
    /// Ручной режим: карты уже расставлены человеком, и трансформ — это его
    /// работа. Перестройка обновляет данные и состояние прямо на месте.
    ///
    /// <para>Обновлять всё равно нужно: сохранение префаба рассылает свои
    /// значения по инстансам и затирает то, что скрипт проставил в те же поля,
    /// — арт карты в первую очередь. Именно поэтому «ничего не делать» тут не
    /// подходит, а «собрать заново» ломает композицию.</para>
    ///
    /// <para>Карт ещё нет — раскладываем сетку один раз, чтобы было что
    /// двигать. Дальше расстановка твоя.</para>
    /// </summary>
    private void RefreshInPlace(GameObject prefab, List<Card> cards, string longest)
    {
        var existing = GetComponentsInChildren<CardView>(true);

        if (existing.Length == 0)
        {
            BuildLayout(prefab, cards, longest);
            Debug.Log("[CardLab] Ручной режим: карты разложены первый раз. " +
                      "Дальше двигай их как хочешь — перестройка больше их не тронет, " +
                      "но правки префаба будут прилетать в них по-прежнему.");
            return;
        }

        int matched = 0;
        foreach (var view in existing)
        {
            // Карты, доставшиеся от режима Grid, помечены DontSave и молча не
            // попали бы в файл сцены: расстановка выглядела бы сохранённой и
            // исчезала при следующем открытии. Снимаем метку здесь, а не при
            // переключении режима, потому что попасть сюда можно и иначе —
            // например продублировав карту в Hierarchy.
            if (view.gameObject.hideFlags != HideFlags.None)
            {
                view.gameObject.hideFlags = HideFlags.None;
#if UNITY_EDITOR
                EditorUtility.SetDirty(view.gameObject);
                EditorSceneManager.MarkSceneDirty(gameObject.scene);
#endif
            }

            var card = MatchCardByObjectName(view.gameObject.name, cards);
            if (card == null) continue;

            ApplyCardState(view, card, longest);
            matched++;
        }

        if (matched < existing.Length)
            Debug.LogWarning($"[CardLab] {existing.Length - matched} карт(ы) не опознались по имени объекта. " +
                             $"Ручной режим узнаёт карту по имени вида «{CardNamePrefix}Дракон» — " +
                             "переименованный объект обновить нечем.");
    }

    /// <summary>
    /// Какая карта лежит в объекте с таким именем.
    ///
    /// <para>Имя объекта — единственное, что переживает перезагрузку домена:
    /// <c>CardView._card</c> это обычный C#-класс, Unity его не сериализует, и
    /// после перекомпиляции он null. Заодно имя видно прямо в Hierarchy.</para>
    ///
    /// <para>Берём самое длинное подходящее имя: дубликат, которому Unity
    /// приписала « (1)», должен опознаться так же, как оригинал.</para>
    /// </summary>
    private static Card MatchCardByObjectName(string objectName, List<Card> cards)
    {
        if (string.IsNullOrEmpty(objectName)) return null;

        var tail = objectName.StartsWith(CardNamePrefix)
            ? objectName.Substring(CardNamePrefix.Length)
            : objectName;

        return cards
            .Where(c => tail.StartsWith(c.Name, System.StringComparison.Ordinal))
            .OrderByDescending(c => c.Name.Length)
            .FirstOrDefault();
    }

    [ContextMenu("Clear")]
    public void Clear()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            GameObject child = transform.GetChild(i).gameObject;
            if (Application.isPlaying) Destroy(child);
            else DestroyImmediate(child);
        }
    }

    /// <summary>
    /// Правая колонка: одна карта так, как её видит игрок при наведении, и под
    /// ней — она же в ректе руки.
    ///
    /// <para>Карта в руке это не просто уменьшенная карта:
    /// <c>HandLayoutGroup</c> каждый пересчёт делает <c>child.sizeDelta = cellSize</c>,
    /// то есть 150×250 превращается в 100×150, и только потом накладывается
    /// масштаб. Это единственное место, где лицо карты верстается в чужом
    /// ректе, и потому первое, что ломается при перестановке полос.</para>
    /// </summary>
    private void BuildSideColumn(GameObject prefab, List<Card> cards, string longest,
                                 float x, float width)
    {
        var sample = PickPreviewCard(cards);
        float scale = Mathf.Max(0.01f, _previewScale);
        float top = -_margin;

        CardView preview = Spawn(prefab, sample, longest);
        if (preview != null)
        {
            AddLabel($"{scale:0.##}× — при наведении", x, top, width);

            var rect = (RectTransform)preview.transform;
            rect.localScale = Vector3.one * scale;
            rect.anchoredPosition = new Vector2(
                x + width / 2f,
                top - LabelHeight - CardSize.y * scale);
        }

        if (!_showHandCell) return;

        float handTop = top - LabelHeight - CardSize.y * scale - _margin;
        float handScale = Mathf.Max(0.01f, _handCellScale);

        CardView hand = Spawn(prefab, sample, longest);
        if (hand == null) return;

        AddLabel($"{_handCellSize.x:0}×{_handCellSize.y:0} @ {handScale:0.##}× — в руке",
                 x, handTop, width);

        var handRect = (RectTransform)hand.transform;
        handRect.sizeDelta = _handCellSize;
        handRect.localScale = Vector3.one * handScale;
        handRect.anchoredPosition = new Vector2(
            x + width / 2f,
            handTop - LabelHeight - _handCellSize.y * handScale);
    }

    private Card PickPreviewCard(List<Card> cards)
    {
        if (string.IsNullOrWhiteSpace(_previewCard)) return cards[0];

        var hit = cards.FirstOrDefault(c =>
            c.Name.IndexOf(_previewCard, System.StringComparison.OrdinalIgnoreCase) >= 0);

        if (hit == null)
            Debug.LogWarning($"[CardLab] Карта «{_previewCard}» для превью не нашлась — беру первую.");

        return hit ?? cards[0];
    }

    /// <summary>
    /// Вся колода рядами, в натуральную величину.
    ///
    /// <para>Колонки считаются из числа рядов, а не задаются: рядов ровно
    /// столько, сколько попросили, а ширина подстраивается. Если ряд не влезает
    /// в отведённую ширину — ужимается шаг, а не карта: смотреть колоду имеет
    /// смысл в том размере, в котором она лежит на столе.</para>
    /// </summary>
    private void BuildGallery(GameObject prefab, List<Card> cards, string longest,
                              float rightEdge, float canvasHeight)
    {
        int rows = Mathf.Max(1, _rows);
        int columns = Mathf.CeilToInt(cards.Count / (float)rows);

        float available = rightEdge - _margin;
        float stepX = _spacing.x;
        if (columns > 1)
        {
            float needed = (columns - 1) * stepX + CardSize.x;
            if (needed > available)
                stepX = (available - CardSize.x) / (columns - 1);
        }

        // Ряды прижимаются к верху; если колода в заданное число рядов по
        // высоте не влезает, шаг ужимается — карты начнут находить друг на
        // друга, и это честнее, чем молча уехать за нижний край.
        float stepY = _spacing.y;
        float neededY = (rows - 1) * stepY + CardSize.y;
        float availableY = canvasHeight - _margin * 2f;
        if (rows > 1 && neededY > availableY)
            stepY = (availableY - CardSize.y) / (rows - 1);

        for (int i = 0; i < cards.Count; i++)
        {
            CardView view = Spawn(prefab, cards[i], longest);
            if (view == null) continue;

            int column = i % columns;
            int row = i / columns;

            var rect = (RectTransform)view.transform;
            rect.localScale = Vector3.one;
            rect.anchoredPosition = new Vector2(
                _margin + column * stepX + CardSize.x / 2f,
                -_margin - row * stepY - CardSize.y);
        }
    }


    /// <summary>
    /// Создаёт одну карту и приводит её ровно в то состояние, что и в игре:
    /// Initialize → SetHighlight → FaceUp/FaceDown.
    /// </summary>
    private CardView Spawn(GameObject prefab, Card card, string longest)
    {
        GameObject instance;
#if UNITY_EDITOR
        // Именно InstantiatePrefab, а не Instantiate: связь с префабом — то, ради чего
        // лаборатория и существует.
        instance = Application.isPlaying
            ? Instantiate(prefab, transform)
            : (GameObject)PrefabUtility.InstantiatePrefab(prefab, transform);
#else
        instance = Instantiate(prefab, transform);
#endif
        if (instance == null) return null;

        instance.transform.SetParent(transform, false);

        // В ручном режиме карта обязана попасть в файл сцены — иначе твоя
        // расстановка не переживёт даже перезагрузку сцены, не то что
        // перезапуск редактора.
        if (_arrangement == Arrangement.Grid) instance.hideFlags = HideFlags.DontSave;

        instance.name = CardNamePrefix + card.Name;

        CardView view = instance.GetComponent<CardView>();
        if (view == null)
        {
            Debug.LogWarning($"[CardLab] На префабе {prefab.name} нет CardView.");
            return null;
        }

        if (!_interactive)
        {
            // DragAndDrop и ховер-скейлер тянутся к UnGameManager и CardScaleContainer,
            // которых в лаборатории нет. Гасим их — но только их.
            //
            // Image и TextMeshProUGUI тоже MonoBehaviour, и «выключить всё, кроме
            // CardView» гасило заодно корневой Image, то есть фон карты. В игре и в
            // Prefab Mode фон при этом оставался на месте, так что расхождение выглядело
            // как проблема самого фона, а не лаборатории.
            foreach (var behaviour in instance.GetComponents<MonoBehaviour>())
            {
                if (behaviour is CardView || behaviour is Graphic) continue;
                behaviour.enabled = false;
            }
        }

        ApplyCardState(view, card, longest);
        return view;
    }

    /// <summary>
    /// Приводит карту ровно в то состояние, что и в игре:
    /// Initialize → подсветка → лицо или рубашка. Трансформ не трогает —
    /// им заведует либо сетка, либо человек.
    /// </summary>
    private void ApplyCardState(CardView view, Card card, string longest)
    {
        view.Initialize(card);

        if (_stressTest && view._cardDescriptionText != null && longest != null)
            view._cardDescriptionText.text = longest;

        view.SetHighlight(_highlight);

        if (_faceDown) view.FaceDown();
        else view.FaceUp();
    }

    private void AddLabel(string text, float x, float top, float width)
    {
        var go = new GameObject("Label", typeof(RectTransform));
        if (_arrangement == Arrangement.Grid) go.hideFlags = HideFlags.DontSave;
        go.transform.SetParent(transform, false);

        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = new Vector2(Mathf.Max(width, 120f), LabelHeight);
        rect.anchoredPosition = new Vector2(x, top + LabelHeight);

        var label = go.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = 16;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.raycastTarget = false;
    }

    /// <summary>
    /// Карт в колоде больше, чем картинок в Resources/CardsImages, и состав картинок
    /// сейчас меняется. Без этого списка «почему карта белая» стоит отдельного расследования.
    /// </summary>
    private static void ReportMissingArt(IEnumerable<Card> cards)
    {
        var missing = cards
            .Where(c => Resources.Load<Sprite>($"CardsImages/{c.Name}") == null)
            .Select(c => c.Name)
            .ToList();

        if (missing.Count == 0)
        {
            Debug.Log("[CardLab] Картинки есть у всех карт.");
            return;
        }

        Debug.LogWarning($"[CardLab] Нет картинок у {missing.Count} карт " +
                         $"(ожидаются в Assets/Resources/CardsImages/<название>.PNG): " +
                         string.Join(", ", missing));
    }
}
