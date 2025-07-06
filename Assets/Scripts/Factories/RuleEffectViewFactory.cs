using Assets.Libreries.ScaryTales.Abstractions;
using Assets.Scripts.Views;
using ScaryTales;
using ScaryTales.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Assets.Scripts.Factories
{
    public class RuleEffectViewFactory
    {
        private readonly IGameManager _gameManager;
        private readonly Transform _gameBoardPanel;
        private readonly GameObject _ruleEffectPrefab;

        public RuleEffectViewFactory(IGameManager gameManager, Transform gameBoardPanel, GameObject itemPrefab)
        {
            _gameManager = gameManager;
            _gameBoardPanel = gameBoardPanel;
            _ruleEffectPrefab = itemPrefab;
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
