using Assets.Libraries.ScaryTales.Abstractions;
using Assets.Libraries.ScaryTales.Enums;
using Assets.Libraries.ScaryTales.Rules.Effects;
using System.Collections.Generic;

namespace Assets.Libraries.ScaryTales.Rules.Templates.A
{
    public class A2 : Rule
    {
        public override string Name => "Сумеречное колдовство";

        public override string Description => "В черной башне посреди деревни пробудилась тёмная сила, и теперь по ночам на улицах лютуют кошмарные твари. Люди объединились, чтобы изгнать колдуна - источник всех несчастий...";

        public override RuleType Type => RuleType.InGame;

        // Список собирается один раз на экземпляр — по той же причине, что и в
        // A1: сервер применяет тот же объект эффекта, который клиент показал и
        // на который нажали, а не свежую копию.
        private List<IRuleEffect> _effects;

        public override List<IRuleEffect> Effects =>
            _effects ??= new List<IRuleEffect> { new REfA21(1), new REfA22(2) };
    }
}
