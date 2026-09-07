using Assets.Libraries.ScaryTales.Abstractions;
using Assets.Scripts.Services;
using Assets.Scripts.Views;
using ScaryTales;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace Assets.Scripts.Menus
{
    /// <summary>
    /// Таблица правил.
    ///
    /// <para><b>Один режим вместо двух.</b> Раньше панель открывалась
    /// по-разному: «просто посмотреть» (крестик, эффекты не кликаются) и
    /// «выбери или пропусти» (кнопка «Пропустить», крестика нет), и режим
    /// выбирался в момент открытия. Отсюда и путаница: игрок, открывший
    /// таблицу в свой ход, видел «Пропустить» — как будто открыть её значит
    /// потратить право на правило.</para>
    ///
    /// <para>Теперь правило простое: <b>таблицу можно открыть когда угодно,
    /// закрыть — только крестиком, а эффекты кликаются лишь тогда, когда
    /// правило действительно можно применить</b> (свой ход, карта ещё не
    /// разыграна). Закрытие не тратит ничего.</para>
    /// </summary>
    public class RuleContainer : MonoBehaviour
    {
        public static RuleContainer Instance { get; private set; }

        [SerializeField] public Transform contentPanel; // Куда добавляются предметы

        [Tooltip("Переключатель страниц таблицы: правила в игре ↔ правила в конце партии. Это бывшая кнопка «Пропустить» из прежнего режима «выбери или пропусти» — привязка в сцене та же, поэтому переименование идёт через FormerlySerializedAs. Не привязана — переключателя не будет, и об этом скажет лог.")]
        [FormerlySerializedAs("SkipBtn")]
        [SerializeField] public Button ToggleBtn;
        [SerializeField] private Button ShowBtn;
        [SerializeField] private Button CloseBtn;
        [SerializeField] private GameObject UIBlockerOverlay;

        [Tooltip("Необязательно. Строка под таблицей: объясняет, почему правило сейчас нельзя применить. Без неё клик по эффекту просто молча ничего не делает, что и раздражает.")]
        [SerializeField] private TMP_Text _hintText;

        [Tooltip("Что писать, когда правило применить можно.")]
        [SerializeField] private string _hintActive = "Выберите правило, чтобы применить его.";

        [Tooltip("Что писать, когда нельзя. Правило одно: только в свой ход и только до того, как разыграна карта.")]
        [SerializeField] private string _hintInactive = "Применить правило можно только в свой ход и только до того, как вы разыграли карту.";

        [Tooltip("Фон правила, которое можно применить прямо сейчас. Подсвечивается только когда сошлось И то, и другое: ваш ход с неразыгранной картой И условия самого правила (предметы, состояние стола).")]
        [SerializeField] private Color _availableColor = new Color(0.45f, 0.8f, 0.45f); // мягкий зелёный


        private List<RuleEffectView> _ruleEffectViews = new List<RuleEffectView>();

        /// <summary>
        /// Одна страница таблицы: что показать, кликается ли это и что написать
        /// внизу.
        /// </summary>
        private readonly struct Page
        {
            public readonly List<IRuleEffect> Effects;
            public readonly bool Interactive;
            public readonly Func<IRuleEffect, bool> IsAvailable;
            public readonly string Hint;

            public Page(List<IRuleEffect> effects, bool interactive,
                        Func<IRuleEffect, bool> isAvailable, string hint)
            {
                Effects = effects ?? new List<IRuleEffect>();
                Interactive = interactive;
                IsAvailable = isAvailable;
                Hint = hint;
            }
        }

        // Первая страница — та, с которой таблица открылась. Вторая появляется
        // только если её задали через SetAlternatePage; без неё переключателя
        // нет вовсе.
        private Page _primary;
        private Page? _alternate;
        private bool _showingAlternate;
        private string _toggleOpenLabel;
        private string _toggleBackLabel;

        /// <summary>Игрок выбрал эффект. Вызывается только в интерактивном режиме.</summary>
        public Action<IRuleEffect> OnRuleSelected;

        /// <summary>
        /// Панель закрыли крестиком, ничего не выбрав.
        ///
        /// <para>Событие нужно потому, что крестик привязан в сцене прямо к
        /// <see cref="Hide"/>. Пока его не было, корутина, ждавшая выбора,
        /// после закрытия висела вечно — и это был главный источник «открыл,
        /// закрыл, и правило больше не работает».</para>
        /// </summary>
        public Action OnClosed;

        private void Awake()
        {
            Instance = this;
            // Переключатель включается только тогда, когда есть куда
            // переключаться, — см. SetAlternatePage.
            if (ToggleBtn != null) ToggleBtn.gameObject.SetActive(false);
            gameObject.SetActive(false); // По умолчанию скрыто
        }
        /// <summary>
        /// Показать таблицу правил.
        /// </summary>
        /// <param name="ruleEffects">Что показывать.</param>
        /// <param name="interactive">
        /// Можно ли применить правило прямо сейчас. Только от этого и зависит,
        /// кликаются ли эффекты; на возможность открыть и закрыть таблицу это
        /// не влияет никак.
        /// </param>
        /// <param name="isAvailable">
        /// Выполнены ли условия конкретного правила (предметы, состояние
        /// стола). Нужно только для подсветки: правило подсвечивается, когда
        /// сошлось И <paramref name="interactive"/>, И условия. Может быть
        /// null — тогда никто не подсвечивается.
        /// </param>
        /// <param name="hint">
        /// Своя подпись под таблицей вместо дежурной.
        ///
        /// <para>Нужна разбору конца партии: он показывает этой же панелью
        /// баннер с финальным правилом, а дежурная подпись говорит «применить
        /// правило можно только в свой ход» — для конца партии прямая
        /// неправда. Пусто или null — подпись прежняя.</para>
        /// </param>
        public void Show(List<IRuleEffect> ruleEffects, bool interactive,
                         Func<IRuleEffect, bool> isAvailable = null,
                         string hint = null)
        {
            gameObject.SetActive(true);
            ShowBtn.gameObject.SetActive(false);
            CloseBtn.gameObject.SetActive(true);
            UIBlockerOverlay.SetActive(true); // включает блокировку

            _primary = new Page(ruleEffects, interactive, isAvailable, hint);
            // Вторая страница задаётся отдельным вызовом уже после Show, и
            // сбрасывать её здесь обязательно: иначе прошлое открытие оставило
            // бы переключатель, ведущий неизвестно куда.
            _alternate = null;
            _showingAlternate = false;

            RefreshToggleButton();
            Render();
        }

        /// <summary>
        /// Добавляет таблице вторую страницу и переключатель на неё.
        /// </summary>
        ///
        /// <para>Ради правил конца партии: игрок должен их видеть, но применить
        /// не может — они считаются сами, когда партия кончилась. Поэтому
        /// вторая страница всегда некликабельная, каким бы ни был ход.</para>
        ///
        /// <param name="openLabel">Что написано на кнопке, пока открыта первая страница.</param>
        /// <param name="backLabel">Что написано на ней же, пока открыта вторая.</param>
        public void SetAlternatePage(List<IRuleEffect> effects, string hint,
                                     string openLabel, string backLabel)
        {
            if (effects == null || effects.Count == 0) return;

            _alternate = new Page(effects, interactive: false, isAvailable: null, hint: hint);
            _toggleOpenLabel = openLabel;
            _toggleBackLabel = backLabel;

            RefreshToggleButton();
        }

        /// <summary>
        /// Перерисовывает содержимое под текущую страницу. Строки создаются
        /// заново, поэтому подписка на клик не накапливается между показами.
        /// </summary>
        private void Render()
        {
            var page = _showingAlternate && _alternate.HasValue ? _alternate.Value : _primary;

            ClearContentPanelChildren();

            if (_hintText != null)
                _hintText.text = string.IsNullOrEmpty(page.Hint)
                    ? (page.Interactive ? _hintActive : _hintInactive)
                    : page.Hint;

            ConvertRuleEffectsToViews(page.Effects);
            foreach (var view in _ruleEffectViews)
            {
                view.transform.SetParent(contentPanel, false);

                // Подсветка — подсказка, а не запрет: кликаются все правила,
                // пока ход позволяет. Клиентское зеркало может разойтись с
                // сервером в мелочи, и тогда запрет на клик отнял бы у игрока
                // правило, которое он имеет право применить. Сервер всё равно
                // проверяет условия заново и откажет сам.
                bool highlighted = page.Interactive
                                   && page.IsAvailable != null
                                   && page.IsAvailable(view.Effect);
                view.SetAvailable(highlighted, _availableColor);

                if (page.Interactive)
                    view.OnRuleEffectClicked += Pick;
            }
        }

        private void TogglePage()
        {
            if (!_alternate.HasValue) return;
            _showingAlternate = !_showingAlternate;
            RefreshToggleButton();
            Render();
        }

        /// <summary>
        /// Приводит кнопку-переключатель в соответствие состоянию: показать,
        /// подписать и подключить — или спрятать, если переключаться некуда.
        /// </summary>
        private void RefreshToggleButton()
        {
            if (ToggleBtn == null)
            {
                // Молчаливо выключенная функция в этом проекте уже трижды
                // стоила расследования, поэтому она говорит о себе сама.
                if (_alternate.HasValue)
                    Debug.LogWarning("[RuleContainer] Слот ToggleBtn не привязан — " +
                                     "правила конца партии посмотреть будет нечем.");
                return;
            }

            bool available = _alternate.HasValue;
            ToggleBtn.gameObject.SetActive(available);
            if (!available) return;

            // Слушателей снимаем перед каждой подпиской: таблицу открывают
            // много раз за партию, и без этого один клик звал бы переключение
            // столько раз, сколько её открывали.
            ToggleBtn.onClick.RemoveAllListeners();
            ToggleBtn.onClick.AddListener(TogglePage);

            var label = ToggleBtn.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
                label.text = _showingAlternate ? _toggleBackLabel : _toggleOpenLabel;
        }

        /// <summary>
        /// Игрок выбрал эффект. Закрываем панель БЕЗ <see cref="OnClosed"/>:
        /// выбор — это не отказ, и разбудить обработчика отказа здесь значило
        /// бы ответить дважды.
        /// </summary>
        private void Pick(IRuleEffect effect)
        {
            var selected = OnRuleSelected;
            Close();
            selected?.Invoke(effect);
        }
        public void ClearContentPanelChildren()
        {
            foreach (Transform child in contentPanel)
            {
                // Гасим перед уничтожением. Destroy откладывается до конца
                // кадра, а переключение страниц чистит и наполняет таблицу в
                // одном кадре — раскладка успела бы посчитать обе пачки строк
                // сразу. Выключенных детей LayoutGroup не видит.
                child.gameObject.SetActive(false);
                Destroy(child.gameObject);
            }
            _ruleEffectViews.Clear();
        }
        /// <summary>
        /// Закрыть панель. Крестик в сцене привязан сюда напрямую, поэтому
        /// именно отсюда уходит <see cref="OnClosed"/> — иначе тот, кто ждёт
        /// ответа, не узнает, что игрок просто ушёл.
        /// </summary>
        public void Hide()
        {
            var closed = OnClosed;
            Close();
            closed?.Invoke();
        }

        private void Close()
        {
            gameObject.SetActive(false);
            ClearContentPanelChildren();
            CloseBtn.gameObject.SetActive(true);
            ShowBtn.gameObject.SetActive(true);
            UIBlockerOverlay.SetActive(false); // отключает блокировку

            // Следующее открытие начинается с первой страницы: игрок пришёл за
            // правилами своего хода, а не за тем, где его оставили в прошлый раз.
            _alternate = null;
            _showingAlternate = false;
            if (ToggleBtn != null) ToggleBtn.gameObject.SetActive(false);
        }
        private void ConvertRuleEffectsToViews(List<IRuleEffect> effects)
        {
            foreach (var effect in effects)
            {
                var itemView = RuleEffectService.Instance.CreateRuleEffectView(effect, contentPanel);
                if (itemView != null)
                {
                    _ruleEffectViews.Add(itemView);
                }
            }
        }
        /// <summary>Открыта ли таблица прямо сейчас.</summary>
        public bool IsShowing => gameObject.activeSelf;
    }
}
