using Assets.Libreries.ScaryTales.Abstractions;
using Assets.Scripts.Views;
using UnityEngine;

namespace Assets.Scripts.Factories
{
    public class RuleEffectViewFactory
    {
        private readonly GameObject _ruleEffectPrefab;

        public RuleEffectViewFactory(GameObject ruleEffectPrefab)
        {
            _ruleEffectPrefab = ruleEffectPrefab;
        }

        public RuleEffectView CreateRuleEffectView(IRuleEffect effect, Transform parent)
        {
            if (_ruleEffectPrefab == null) return null;

            GameObject ruleEffectInstance = GameObject.Instantiate(_ruleEffectPrefab, parent);
            RuleEffectView ruleEffectView = ruleEffectInstance.GetComponent<RuleEffectView>();

            if (ruleEffectView == null)
            {
                Debug.LogError("Компонент RuleEffectView не найден!");
                return null;
            }

            ruleEffectView.Initialize(effect);
            return ruleEffectView;
        }
    }
}
