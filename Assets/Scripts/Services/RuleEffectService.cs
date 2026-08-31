using Assets.Libraries.ScaryTales.Abstractions;
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

        /// <summary>
        /// Drops the cached instance so the next access builds a fresh one.
        ///
        /// This is a plain C# static, so it is NOT subject to Unity's fake-null:
        /// it survives the scene reload that ends every game, still holding the
        /// destroyed scene's Transforms and views. Creating a card against a
        /// destroyed parent gives it no parent at all, which is why cards ended up
        /// loose in the scene on a second game. Called from UnGameManager.Awake,
        /// which is once per scene load — exactly the lifetime these want.
        /// </summary>
        public static void Reset() => _instance = null;

        private readonly RuleEffectViewFactory _ruleEffectViewFactory;
        private readonly Dictionary<IRuleEffect, RuleEffectView> _ruleEffectToViewMap = new();

        private RuleEffectService()
        {
            var ruleEffectPrefab = Resources.Load<GameObject>("RuleEffectPrefab");
            _ruleEffectViewFactory = new RuleEffectViewFactory(ruleEffectPrefab);
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
