using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Assets.Scripts.UIEntities
{
    /// <summary>
    /// Название сказки в начале партии: имена двух выпавших правил, сшитые
    /// союзом «и», на весь экран.
    ///
    /// <para>Правила выбираются случайно на каждую партию, и до этого экрана
    /// игрок узнавал о них только заглянув в таблицу правил. Заголовок делает
    /// жребий событием: партия начинается с того, что стол читает, во что
    /// сегодня играет.</para>
    ///
    /// <para><b>Работ в сцене не требует.</b> Затемнение, подпись и канвас
    /// собираются кодом — по образцу <c>CardPickPanel</c> и
    /// <c>EndGameScoringUI</c>. Непривязанный слот имеет свойство остаться
    /// непривязанным, а молчаливо выключенная функция в этом проекте уже не
    /// раз стоила расследования.</para>
    /// </summary>
    public class TaleTitleUI : MonoBehaviour
    {
        [Tooltip("Сколько заголовок держится на экране в полную силу, секунд.")]
        [SerializeField] private float _holdSeconds = 3f;

        [Tooltip("Сколько длится проявление и сколько — угасание, секунд.")]
        [SerializeField] private float _fadeSeconds = 0.6f;

        [Tooltip("Размер шрифта заголовка.")]
        [SerializeField] private float _fontSize = 64f;

        [Tooltip("Затемнение под заголовком. Гасит стол и ловит клики, пока идёт показ.")]
        [SerializeField] private Color _dimColor = new Color(0f, 0f, 0f, 0.72f);

        [Tooltip("Цвет заголовка.")]
        [SerializeField] private Color _titleColor = new Color(0.96f, 0.91f, 0.78f, 1f);

        /// <summary>
        /// Поверх всего: у GameCanvas порядок 0, у StaticUICanvas — 1.
        /// Свой канвас с большим порядком избавляет от вопроса, к какому из
        /// них прицепиться, и заголовок не уедет под кнопки интерфейса.
        /// </summary>
        private const int SortingOrder = 500;

        private const string DecorativeFontPath = "fonts/RuslanDisplay-Regular SDF";

        private GameObject _overlay;
        private Sequence _sequence;

        /// <summary>
        /// Показать название и убрать его само.
        ///
        /// <para>Пустое или отсутствующее имя правила означает, что сборка не
        /// знает правила, выбранного сервером (о чём <c>UnGameManager</c> уже
        /// накричал в лог). Заголовок в таком случае не показывается вовсе:
        /// «Битва на позабытых болотах и » — хуже, чем ничего.</para>
        /// </summary>
        public void Show(string inGameRuleName, string finalRuleName)
        {
            if (string.IsNullOrWhiteSpace(inGameRuleName) || string.IsNullOrWhiteSpace(finalRuleName))
                return;

            Hide();

            var root = FindCanvasRoot();
            if (root == null)
            {
                Debug.LogWarning("[TaleTitleUI] Не нашёлся корень канваса — заголовка не будет.");
                return;
            }

            var group = BuildOverlay(root, $"{inGameRuleName.Trim()} и {finalRuleName.Trim()}");

            // Гасим до первого кадра: оверлей создаётся непрозрачным, и без
            // этой строки первый кадр успел бы мигнуть готовым заголовком
            // раньше, чем начнётся проявление.
            group.alpha = 0f;

            // Показ идёт по нескалированному времени: заголовок ничем не
            // управляет и не должен зависеть от того, что кто-то поставил
            // Time.timeScale в ноль на паузе.
            _sequence = DOTween.Sequence()
                .SetUpdate(true)
                .Append(group.DOFade(1f, _fadeSeconds).From(0f))
                .AppendInterval(_holdSeconds)
                .Append(group.DOFade(0f, _fadeSeconds))
                .OnComplete(Hide);
        }

        /// <summary>
        /// Снять заголовок. Идемпотентна: зовётся и по концу показа, и из
        /// <see cref="OnDestroy"/> при обрыве партии. Оверлей ловит клики,
        /// поэтому оставить его висеть — значит подвесить стол.
        /// </summary>
        public void Hide()
        {
            _sequence?.Kill();
            _sequence = null;

            if (_overlay != null) Destroy(_overlay);
            _overlay = null;
        }

        private void OnDestroy() => Hide();

        private CanvasGroup BuildOverlay(Transform root, string title)
        {
            _overlay = new GameObject("TaleTitle", typeof(RectTransform), typeof(CanvasGroup),
                                      typeof(Canvas), typeof(GraphicRaycaster), typeof(Image));

            var rect = (RectTransform)_overlay.transform;
            rect.SetParent(root, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
            rect.SetAsLastSibling();

            var canvas = _overlay.GetComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = SortingOrder;

            var dim = _overlay.GetComponent<Image>();
            dim.color = _dimColor;
            // Ловит клики: пока читают название, по столу кликать нечего.
            dim.raycastTarget = true;

            BuildTitle(rect, title);

            return _overlay.GetComponent<CanvasGroup>();
        }

        private void BuildTitle(RectTransform panel, string title)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(panel, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(1400f, 400f);

            var text = go.AddComponent<TextMeshProUGUI>();
            text.text = title;
            text.color = _titleColor;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;

            // Автосайз: два длинных названия подряд легко перерастают экран,
            // а обрезанное название сказки читается как ошибка.
            text.enableAutoSizing = true;
            text.fontSizeMin = 24f;
            text.fontSizeMax = _fontSize;
            text.fontSize = _fontSize;

            // Декоративный шрифт уже есть в проекте и стоит на заголовках в
            // сцене. Нет — TMP подставит свой по умолчанию, и это не повод
            // отменять показ.
            var font = Resources.Load<TMP_FontAsset>(DecorativeFontPath);
            if (font != null) text.font = font;
        }

        /// <summary>
        /// Корень канваса. Берётся от игрового поля — тем же способом, что и
        /// у окна выбора карты и разбора конца партии.
        /// </summary>
        private static Transform FindCanvasRoot()
        {
            var board = UnGameManager.Instance != null ? UnGameManager.Instance.GameBoardPanel : null;
            if (board == null) return null;

            var root = board.root;
            return root == board ? null : root;
        }
    }
}
