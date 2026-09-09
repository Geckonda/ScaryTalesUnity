using System.Collections.Generic;
using System.Linq;
using ScaryTales;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
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
/// <para>Инстансы помечены <see cref="HideFlags.DontSave"/>: галерея не попадает в
/// CardLab.unity и не даёт километровый диф на каждую перестройку.</para>
/// </summary>
[ExecuteAlways]
public class CardLabDriver : MonoBehaviour
{
    [Header("Что раскладываем")]
    [Tooltip("Пусто — возьмём Resources/CardPrefab, тот же самый, что и в игре.")]
    [SerializeField] private GameObject _cardPrefab;

    [Tooltip("Показать только карты, в названии которых есть эта строка. Пусто — все.")]
    [SerializeField] private string _onlyCard = "";

    [Header("Сетка")]
    [SerializeField] private int _columns = 7;
    [SerializeField] private Vector2 _spacing = new Vector2(180f, 300f);
    [SerializeField] private Vector2 _origin = new Vector2(120f, -60f);

    [Header("Эталонный ряд")]
    [Tooltip("Масштабы, в которых карта реально встречается в игре: стол, рука, ховер-превью.")]
    [SerializeField] private float[] _scales = { 1f, 1.3f, 2.3f };

    [Tooltip("Показать карту так, как её реально видно в руке местного игрока: " +
             "HandLayoutGroup принудительно ужимает рект до cellSize, а не просто масштабирует.")]
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

    [Tooltip("Сохранять галерею в файл сцены. По умолчанию нет: иначе каждая перестройка " +
             "даёт километровый диф в CardLab.unity.")]
    [SerializeField] private bool _persistInstances;

    private static readonly Vector2 CardSize = new Vector2(150f, 250f);
    private const float LabelHeight = 22f;

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
        Clear();

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

        float y = BuildScaleRow(prefab, cards[0], longest);
        BuildGallery(prefab, cards, longest, y);
        ReportMissingArt(cards);
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
    /// Верхний ряд: одна и та же карта во всех масштабах, в которых её видит игрок.
    /// Возвращает Y, с которого можно начинать галерею.
    /// </summary>
    private float BuildScaleRow(GameObject prefab, Card sample, string longest)
    {
        bool hasScales = _scales != null && _scales.Length > 0;
        if (!hasScales && !_showHandCell) return _origin.y;

        float maxScale = hasScales ? _scales.Max() : 1f;
        if (_showHandCell) maxScale = Mathf.Max(maxScale, _handCellScale);

        float rowTop = _origin.y - LabelHeight;
        float rowBottom = rowTop - CardSize.y * maxScale;
        float x = _origin.x;

        if (hasScales)
        {
            foreach (float rawScale in _scales)
            {
                float scale = Mathf.Max(0.01f, rawScale);
                float width = CardSize.x * scale;

                CardView view = Spawn(prefab, sample, longest);
                if (view == null) continue;

                var rect = (RectTransform)view.transform;
                rect.localScale = Vector3.one * scale;
                rect.anchoredPosition = new Vector2(x + width / 2f, rowBottom);

                AddLabel($"{scale:0.##}× — {DescribeScale(rawScale)}", x, rowTop, width);

                x += width + 40f;
            }
        }

        if (_showHandCell) BuildHandCell(prefab, sample, longest, ref x, rowTop, rowBottom);

        return rowBottom - 70f;
    }

    /// <summary>
    /// Карта в руке местного игрока — не просто уменьшенная карта.
    /// <see cref="HandLayoutGroup"/> каждый пересчёт делает <c>child.sizeDelta = cellSize</c>,
    /// то есть 150×250 превращается в 100×150, и только потом накладывается масштаб.
    ///
    /// <para>Это единственное место, где вёрстка лица работает в чужом ректе, и потому
    /// первое, что ломается при перестановке полос. Показываем его рядом с остальными
    /// масштабами, чтобы не узнавать об этом из партии.</para>
    /// </summary>
    private void BuildHandCell(GameObject prefab, Card sample, string longest,
                               ref float x, float rowTop, float rowBottom)
    {
        CardView view = Spawn(prefab, sample, longest);
        if (view == null) return;

        float scale = Mathf.Max(0.01f, _handCellScale);
        float width = _handCellSize.x * scale;

        var rect = (RectTransform)view.transform;
        rect.sizeDelta = _handCellSize;
        rect.localScale = Vector3.one * scale;
        rect.anchoredPosition = new Vector2(x + width / 2f, rowBottom);

        AddLabel($"{_handCellSize.x:0}×{_handCellSize.y:0} @ {scale:0.##}× — рука",
                 x, rowTop, width);

        x += width + 40f;
    }

    private static string DescribeScale(float scale)
    {
        if (Mathf.Approximately(scale, 1f)) return "стол";
        if (Mathf.Approximately(scale, 1.3f)) return "рука";
        if (Mathf.Approximately(scale, 2.3f)) return "ховер-превью";
        return "свой";
    }

    private void BuildGallery(GameObject prefab, List<Card> cards, string longest, float top)
    {
        int columns = Mathf.Max(1, _columns);

        for (int i = 0; i < cards.Count; i++)
        {
            CardView view = Spawn(prefab, cards[i], longest);
            if (view == null) continue;

            int column = i % columns;
            int row = i / columns;

            var rect = (RectTransform)view.transform;
            rect.localScale = Vector3.one;
            rect.anchoredPosition = new Vector2(
                _origin.x + column * _spacing.x + CardSize.x / 2f,
                top - row * _spacing.y - CardSize.y);
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
        if (!_persistInstances) instance.hideFlags = HideFlags.DontSave;
        instance.name = $"Card — {card.Name}";

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

        view.Initialize(card);

        if (_stressTest && view._cardDescriptionText != null && longest != null)
            view._cardDescriptionText.text = longest;

        view.SetHighlight(_highlight);

        if (_faceDown) view.FaceDown();
        else view.FaceUp();

        return view;
    }

    private void AddLabel(string text, float x, float top, float width)
    {
        var go = new GameObject("Label", typeof(RectTransform));
        go.hideFlags = HideFlags.DontSave;
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
