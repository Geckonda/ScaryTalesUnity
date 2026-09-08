using ScaryTales;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Assets.Scripts.Menus
{
    /// <summary>
    /// Окно выбора карты, которой нет на столе, — сегодня это верх колоды
    /// (Тайная помощница).
    ///
    /// <para><b>Почему не подсветкой, как обычный выбор.</b>
    /// <c>UnGameManager.PromptCardPick</c> подсвечивает карты, у которых уже
    /// есть представление на столе. У карты из колоды его нет: кандидат молча
    /// выпал бы из списка, корутина осталась бы ждать клика, которого не
    /// будет, а комната — стоять на неотвеченном вопросе. Выбор карты из
    /// колоды прежним путём вешает стол насмерть, поэтому путь свой.</para>
    ///
    /// <para><b>Работ в сцене не требует.</b> Окно целиком собирается кодом:
    /// затемнение, подпись и карточные представления. Ни префаба, ни привязки
    /// — по образцу <c>EndGameScoringUI</c>. Молчаливо выключенная функция в
    /// этом проекте уже не раз стоила расследования, а непривязанный слот
    /// имеет свойство остаться непривязанным.</para>
    /// </summary>
    public class CardPickPanel : MonoBehaviour
    {
        /// <summary>Затемнение под окном: гасит стол и ловит клики мимо карт.</summary>
        private static readonly Color DimColor = new Color(0f, 0f, 0f, 0.78f);

        [Tooltip("Во сколько раз карта в окне крупнее обычной. Выбирают по картинке и тексту, читать их надо без прищура.")]
        [SerializeField] private float _cardScale = 1.35f;

        [Tooltip("Расстояние между картами в окне, пикселей канваса.")]
        [SerializeField] private float _cardSpacing = 340f;

        [Tooltip("На сколько ряд карт опущен относительно центра, пикселей канваса. Подпись живёт над ним.")]
        [SerializeField] private float _rowOffsetY = -30f;

        [Tooltip("Насколько подпись выше центра, пикселей канваса.")]
        [SerializeField] private float _titleOffsetY = 250f;

        [Tooltip("Размер шрифта подписи.")]
        [SerializeField] private float _titleFontSize = 40f;

        private RectTransform _panel;
        private readonly List<CardView> _views = new();
        private Action<Card> _onPicked;

        /// <summary>Открыто ли окно прямо сейчас.</summary>
        public bool IsShowing => _panel != null;

        /// <summary>
        /// Показать карты и ждать выбора.
        ///
        /// <para>Выбор — единственный выход: крестика нет намеренно. Окно
        /// открывается на эффекте уже разыгранной карты, где отказ означал бы
        /// бесплатную отмену половины хода (см. <c>PickCardRequest.CanCancel</c>).</para>
        /// </summary>
        /// <param name="cards">Что показать. Пустой список — окна не будет.</param>
        /// <param name="title">Подпись над картами. Можно не задавать.</param>
        /// <param name="onPicked">Зовётся один раз, с выбранной картой. Окно к этому моменту уже закрыто.</param>
        public void Show(IReadOnlyList<Card> cards, string title, Action<Card> onPicked)
        {
            Hide();
            if (cards == null || cards.Count == 0) return;

            var root = FindCanvasRoot();
            if (root == null)
            {
                // Без канваса показывать негде. Ответить за игрока отсюда
                // нельзя — окно не знает ни номера запроса, ни сети, — но и
                // молчать нельзя: зовущий ждёт выбора, а комната ждёт его.
                Debug.LogError("[CardPickPanel] Не нашёлся канвас — окно выбора карты показать негде.");
                return;
            }

            _onPicked = onPicked;
            _panel = BuildPanel(root);

            if (!string.IsNullOrEmpty(title)) BuildTitle(_panel, title);

            var factory = CardViewService.Instance.CardViewFactory;
            float span = _cardSpacing * (cards.Count - 1);

            for (int i = 0; i < cards.Count; i++)
            {
                // Представление создаётся ФАБРИКОЙ, а не CardViewService: тот
                // записал бы карту в свой словарь, и выбранная карта, приехав
                // потом в руку, получила бы второе представление поверх
                // уничтоженного. Клон карты (CreateSingleCardViewClone) тоже
                // не годится — у клона Id равен нулю, и по клику стало бы
                // нечего отправлять серверу.
                var view = factory.CreateCardView(cards[i], _panel);
                if (view == null) continue;

                // Перетаскивание карты из окна не имеет смысла: она ещё не в
                // руке. Компонент фабрика вешает всегда, снимаем сами.
                factory.DisableDrag(view);
                StripHoverZoom(view);
                view.FaceUp();

                var t = (RectTransform)view.transform;
                t.anchorMin = t.anchorMax = new Vector2(0.5f, 0.5f);
                t.pivot = new Vector2(0.5f, 0.5f);
                t.anchoredPosition = new Vector2(-span / 2f + i * _cardSpacing, _rowOffsetY);
                t.localRotation = Quaternion.identity;
                t.localScale = Vector3.one * _cardScale;

                view.OnCardClicked += HandleCardClicked;
                _views.Add(view);
            }

            if (_views.Count == 0)
            {
                Debug.LogError("[CardPickPanel] Ни одно представление карты не создалось — окно пустое.");
                Hide();
            }
        }

        /// <summary>
        /// Закрыть окно, никого не выбрав. Идемпотентна: зовётся и штатно,
        /// и при обрыве партии.
        ///
        /// <para><c>_onPicked</c> отсюда НЕ вызывается: закрытие — это не
        /// выбор, и разбудить ожидающего пустой рукой значило бы отправить
        /// серверу ответ, которого игрок не давал.</para>
        /// </summary>
        public void Hide()
        {
            foreach (var view in _views)
                if (view != null) view.OnCardClicked -= HandleCardClicked;
            _views.Clear();

            if (_panel != null) Destroy(_panel.gameObject);
            _panel = null;
            _onPicked = null;
        }

        private void OnDestroy() => Hide();

        /// <summary>
        /// Снимает с карты приближение по наведению.
        ///
        /// <para>Оно показывает увеличенную копию в отдельной панели сцены
        /// (<c>CardScaleContainer</c>), а та лежит в канвасе там, где лежит, —
        /// и вполне может оказаться ПОД затемнением окна. Приближать здесь
        /// нечего: карты в окне и так крупные, а показывать копию за мутным
        /// стеклом хуже, чем не показывать вовсе.</para>
        /// </summary>
        private static void StripHoverZoom(CardView view)
        {
            var zoom = view.GetComponent<UIEntities.Components.HoverCardScaler>();
            if (zoom != null) Destroy(zoom);
        }

        /// <summary>
        /// Окно закрывается ДО того, как позвать выбравшего: тот отправит
        /// интент, сервер ответит событиями, а рисовать их поверх ещё живого
        /// окна незачем.
        /// </summary>
        private void HandleCardClicked(Card card)
        {
            var picked = _onPicked;
            Hide();
            picked?.Invoke(card);
        }

        /// <summary>
        /// Растянутая на весь экран панель с затемнением. Живёт в корне
        /// канваса последним ребёнком — то есть поверх всего стола.
        /// </summary>
        private RectTransform BuildPanel(Transform root)
        {
            var go = new GameObject("CardPickPanel", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(root, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
            rect.SetAsLastSibling();

            var dim = go.GetComponent<Image>();
            dim.color = DimColor;
            // Ловит клики: пока идёт выбор, стол под окном трогать нельзя.
            dim.raycastTarget = true;

            return rect;
        }

        private void BuildTitle(RectTransform panel, string title)
        {
            var go = new GameObject("Title", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(panel, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, _titleOffsetY);
            rect.sizeDelta = new Vector2(900f, 90f);

            var text = go.AddComponent<TextMeshProUGUI>();
            text.text = title;
            text.fontSize = _titleFontSize;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;

            // Шрифт TMP берёт из настроек проекта сам. Если их нет, текста
            // просто не будет — окно от этого не сломается, но знать об этом
            // стоит: пустая подпись выглядит как забытая строка.
            if (text.font == null)
                Debug.LogWarning("[CardPickPanel] У TMP нет шрифта по умолчанию — подписи над картами не будет.");
        }

        /// <summary>
        /// Корень канваса. Берётся от игрового поля — того же якоря, по
        /// которому кладут своё содержимое полёт карты и разбор конца партии.
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
