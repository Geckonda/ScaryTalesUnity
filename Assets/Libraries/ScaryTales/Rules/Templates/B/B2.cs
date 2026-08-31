using Assets.Libraries.ScaryTales.Abstractions;
using Assets.Libraries.ScaryTales.Enums;
using Assets.Libraries.ScaryTales.Rules.Effects;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Assets.Libraries.ScaryTales.Rules.Templates.B
{
    public class B2 : Rule
    {
        public override string Name => "Сокровище дракона";

        public override string Description => "...и во время своих странствий они отыскали легендарное сокровище великого дракона.";

        public override RuleType Type => RuleType.InTheEnd;

        // Один раз на экземпляр — см. пояснение в A1.
        private List<IRuleEffect> _effects;

        public override List<IRuleEffect> Effects =>
            _effects ??= new List<IRuleEffect> { new REfB21(1) };
    }
}
