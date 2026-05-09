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
    public class RuleContainer : MonoBehaviour
    {
        public static RuleContainer Instance { get; private set; }

        [SerializeField] public Transform contentPanel; // Куда добавляются предметы
        [SerializeField] public Button SkipBtn;
        [SerializeField] private Button ShowBtn;
        [SerializeField] private Button CloseBtn;
        [SerializeField] private GameObject UIBlockerOverlay;


        private List<RuleEffectView> _ruleEffectViews = new List<RuleEffectView>();

        public Action<IRuleEffect> OnRuleSelected;
        private bool _ruleChosen = false;
        private IRuleEffect _selectedEffect;

        private void Awake()
        {
            Instance = this;
            gameObject.SetActive(false); // По умолчанию скрыто
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="openedByPlayer">Был открыт пользователем</param>
        public void Show(List<IRuleEffect> ruleEffects, bool openedByPlayer)
        {
            gameObject.SetActive(true);
            ShowBtn.gameObject.SetActive(false);
            SkipBtn.gameObject.SetActive(!openedByPlayer);
            CloseBtn.gameObject.SetActive(openedByPlayer);
            UIBlockerOverlay.SetActive(true); // включает блокировку

            //ClearContentPanelChildren();
            ConvertRuleEffectsToViews(ruleEffects);
            foreach (var view in _ruleEffectViews)
            {
                view.transform.SetParent(contentPanel, false);

                // Подписываемся на клик по эффекту
                if (!openedByPlayer)
                {
                    view.OnRuleEffectClicked += effect =>
                    {
                        _selectedEffect = effect;
                        _ruleChosen = true;
                        Hide();
                        OnRuleSelected?.Invoke(effect); // вызываем событие
                    };
                }
            }
            if (!openedByPlayer)
            {
                // Подписка на кнопку пропустить
                SkipBtn.onClick.RemoveAllListeners();
                SkipBtn.onClick.AddListener(() =>
                {
                    _selectedEffect = null;
                    _ruleChosen = true;
                    Hide();
                    OnRuleSelected?.Invoke(null); // ничего не выбрано
                });
            }
        }
        public void Show(List<RuleEffectView> views, bool openedByPlayer, Action onSkip = null)
        {
            gameObject.SetActive(true);
            UIBlockerOverlay.SetActive(true); // включает блокировку
            ShowBtn.gameObject.SetActive(false);
            SkipBtn.gameObject.SetActive(!openedByPlayer);
            CloseBtn.gameObject.SetActive(true);

            //ClearContentPanelChildren();
            foreach (var view in _ruleEffectViews)
            {
                view.transform.SetParent(contentPanel, false);

                // Подписываемся на клик по эффекту
                if (!openedByPlayer)
                {
                    view.OnRuleEffectClicked += effect =>
                    {
                        _selectedEffect = effect;
                        _ruleChosen = true;
                        Hide();
                        OnRuleSelected?.Invoke(effect); // вызываем событие
                    };
                }
            }
            if (!openedByPlayer && onSkip != null)
            {
                SkipBtn.onClick.RemoveAllListeners();
                SkipBtn.onClick.AddListener(() => {
                    onSkip();
                    Hide();
                });
            }
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
        public void Hide()
        {
            gameObject.SetActive(false);
            ClearContentPanelChildren();
            SkipBtn.gameObject.SetActive(true);
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
        public IRuleEffect WaitForRuleChoice()
        {
            return _selectedEffect;
        }

        public bool IsRuleChosen => _ruleChosen;
    }
}
