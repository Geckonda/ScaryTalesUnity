using Assets.Libraries.ScaryTales.Abstractions;
using Assets.Libraries.ScaryTales.Enums;
using Assets.Libraries.ScaryTales.Rules.Effects;
using System.Collections.Generic;

namespace Assets.Libraries.ScaryTales.Rules.Templates.A
{
    public class A3 : Rule
    {
        public override string Name => "Испытание рыцарей";

        public override string Description => "Стена от ран, в замок воротился егерь. Принцесса, которую он охранял, бесследно исчезла в Заколдованном лесу. Храбрейшие юноши готовы отправиться на ее поиски...";

        public override RuleType Type => RuleType.InGame;

        // Список собирается один раз на экземпляр — по той же причине, что и в
        // A1 с A2: сервер применяет тот же объект эффекта, который клиент
        // показал и на который нажали, а не свежую копию.
        private List<IRuleEffect> _effects;

        public override List<IRuleEffect> Effects =>
            _effects ??= new List<IRuleEffect> { new REfA31(1), new REfA32(2) };
    }
}
