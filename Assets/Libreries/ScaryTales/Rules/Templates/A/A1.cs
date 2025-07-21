using Assets.Libreries.ScaryTales.Abstractions;
using Assets.Libreries.ScaryTales.Enums;
using Assets.Libreries.ScaryTales.Rules.Effects;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Assets.Libreries.ScaryTales.Rules.Templates.A
{
    public class A1 : Rule
    {
        public override string Name => "Битва на позабытых болотах";

        public override string Description => "Земли";

        public override RuleType Type => RuleType.InGame;

        public override List<IRuleEffect> Effects => new List<IRuleEffect>() { new REfA11(1), new REfA12(2), new REfA13(3) };
    }
}
