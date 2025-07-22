using Assets.Libreries.ScaryTales.Abstractions;
using Assets.Libreries.ScaryTales.Enums;
using Assets.Libreries.ScaryTales.Rules.Effects;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Assets.Libreries.ScaryTales.Rules.Templates.B
{
    public class B2 : Rule
    {
        public override string Name => "Сокровище дракона";

        public override string Description => "...и во время своих странствий они отыскали легендарное сокровище великого дракона.";

        public override RuleType Type => RuleType.InTheEnd;

        public override List<IRuleEffect> Effects => new List<IRuleEffect>() { new REfB21(1) };
    }
}
