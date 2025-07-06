using Assets.Libreries.ScaryTales;
using Assets.Libreries.ScaryTales.Abstractions;
using ScaryTales;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Assets.Scripts.Views
{
    public class RuleEffectView : MonoBehaviour, IPointerClickHandler
    {
        public event Action<IRuleEffect> OnRuleEffectClicked;

        public TMP_Text ruleEffectDescription;
        public Image background;

        private IRuleEffect _ruleEffect;

        public void Initialize(IRuleEffect ruleEffect)
        {
            _ruleEffect = ruleEffect;
            DisplayRuleEffect();
            //SetHighlight(false); // Отключаем подсветку по умолчанию
        }
        public void OnPointerClick(PointerEventData eventData)
        {
            Debug.Log($"Ты нажал на эффект №{_ruleEffect.Id}");
            OnRuleEffectClicked?.Invoke(_ruleEffect);
        }
        public void DisplayRuleEffect()
        {
            ruleEffectDescription.text = _ruleEffect.Description;
        }

    }
}
