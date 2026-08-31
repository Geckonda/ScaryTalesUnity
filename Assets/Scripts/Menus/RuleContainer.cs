using Assets.Libreries.ScaryTales.Abstractions;
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
        [Tooltip("Наследство прежнего режима «выбери или пропусти». Больше не используется и выключается в Awake; поле оставлено, чтобы не рвать привязку в сцене.")]
        [SerializeField] public Button SkipBtn;
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
            // Кнопки «Пропустить» больше нет ни в одном сценарии.
            if (SkipBtn != null) SkipBtn.gameObject.SetActive(false);
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
        public void Show(List<IRuleEffect> ruleEffects, bool interactive,
                         Func<IRuleEffect, bool> isAvailable = null)
        {
            gameObject.SetActive(true);
            ShowBtn.gameObject.SetActive(false);
            CloseBtn.gameObject.SetActive(true);
            UIBlockerOverlay.SetActive(true); // включает блокировку

            if (_hintText != null)
                _hintText.text = interactive ? _hintActive : _hintInactive;

            ConvertRuleEffectsToViews(ruleEffects);
            foreach (var view in _ruleEffectViews)
            {
                view.transform.SetParent(contentPanel, false);

                // Подсветка — подсказка, а не запрет: кликаются все правила,
                // пока ход позволяет. Клиентское зеркало может разойтись с
                // сервером в мелочи, и тогда запрет на клик отнял бы у игрока
                // правило, которое он имеет право применить. Сервер всё равно
                // проверяет условия заново и откажет сам.
                bool highlighted = interactive && isAvailable != null && isAvailable(view.Effect);
                view.SetAvailable(highlighted, _availableColor);

                if (interactive)
                    view.OnRuleEffectClicked += Pick;
            }
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
            // Очищаем контейнер перед добавлением новых элементов
            foreach (Transform child in contentPanel)
            {
                Destroy(child.gameObject);
                Debug.Log($"Destroying {child.name}");
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
