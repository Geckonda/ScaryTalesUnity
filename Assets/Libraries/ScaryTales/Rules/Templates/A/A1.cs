using Assets.Libraries.ScaryTales.Abstractions;
using Assets.Libraries.ScaryTales.Enums;
using Assets.Libraries.ScaryTales.Rules.Effects;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Assets.Libraries.ScaryTales.Rules.Templates.A
{
    public class A1 : Rule
    {
        public override string Name => "Битва на позабытых болотах";

        public override string Description => "Земли к северу от столицы испокон веков принадлежали королевству — пока их не захватили и не заселили ужасные монстры. Наследник престола и его соратники наконец решили отвоевать их обратно...";

        public override RuleType Type => RuleType.InGame;

        // Собирается один раз на экземпляр правила, а не на каждое обращение.
        // Прежнее `=> new List<...>` выдавало новые объекты эффектов каждому
        // читателю, так что применённый сервером эффект гарантированно был НЕ
        // тем объектом, который показали и нажали на клиенте. Держалось это
        // лишь на том, что эффекты не хранят состояния и по проводу ходит Id.
        private List<IRuleEffect> _effects;

        public override List<IRuleEffect> Effects =>
            _effects ??= new List<IRuleEffect> { new REfA11(1), new REfA12(2), new REfA13(3) };
    }
}
