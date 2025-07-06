using Assets.Libreries.ScaryTales.Abstractions;
using Assets.Scripts.Factories;
using Assets.Scripts.Views;
using ScaryTales;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Assets.Scripts.Services
{
    public class RuleEffectService
    {
        private static RuleEffectService _instance;
        public static RuleEffectService Instance => _instance ??= new RuleEffectService();

        private readonly RuleEffectViewFactory _ruleEffectViewFactory;
        private readonly Dictionary<IRuleEffect, RuleEffectView> _ruleEffectToViewMap = new();

        private RuleEffectService()
        {
            var gameManager = UnGameManager.Instance.GameManager;
            var gameBoardPanel = UnGameManager.Instance.GameBoardPanel;
            var ruleEffectPrefab = Resources.Load<GameObject>("RuleEffectPrefab");

            _ruleEffectViewFactory = new RuleEffectViewFactory(gameManager, gameBoardPanel, ruleEffectPrefab);
        }

        public void BundleItemAndView(IRuleEffect ruleEffect, RuleEffectView view)
        {
            if (_ruleEffectToViewMap.ContainsKey(ruleEffect))
                throw new ArgumentException("Это правило уже имеет представление.");

            _ruleEffectToViewMap.Add(ruleEffect, view);
        }

        public RuleEffectView GetRuleEffectView(IRuleEffect ruleEffect)
        {
            _ruleEffectToViewMap.TryGetValue(ruleEffect, out RuleEffectView view);
            return view;
        }

        public RuleEffectView CreateRuleEffectView(IRuleEffect ruleEffect, Transform parent)
        {
            var view = _ruleEffectViewFactory.CreateRuleEffectView(ruleEffect, parent);
            if (view != null)
            {
                _ruleEffectToViewMap[ruleEffect] = view;
            }
            return view;
        }
    }
}
