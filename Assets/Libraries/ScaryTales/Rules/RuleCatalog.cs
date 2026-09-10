using Assets.Libraries.ScaryTales.Enums;
using Assets.Libraries.ScaryTales.Rules.Templates.A;
using Assets.Libraries.ScaryTales.Rules.Templates.B;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Assets.Libraries.ScaryTales.Rules
{
    /// <summary>
    /// The single place that knows which <see cref="Rule"/> classes exist and
    /// what stable id each one has.
    ///
    /// Rules used to be constructed by literal <c>new A1()</c> in two places —
    /// the server session and the client's UnGameManager — which meant the two
    /// sides agreed only by coincidence, and <c>Rule.Id</c> was left at its
    /// default 0 so nothing could refer to a rule over the wire. Now the
    /// server picks ids, sends them in <c>GameStartedEvent</c>, and every
    /// client rebuilds the same rule from this catalog.
    ///
    /// Ids are part of the wire format: never renumber an existing entry, only
    /// append. Lives in the core library, not in Unity, so a headless server
    /// and a future test harness can both use it.
    /// </summary>
    public static class RuleCatalog
    {
        /// <summary>
        /// Не правило, а просьба выбрать случайное. Ноль занят под это
        /// намеренно: настоящие id начинаются с единицы, и <see cref="Create"/>
        /// на ноль и так вернул бы null.
        /// </summary>
        public const int RandomRuleId = 0;

        public const int A1Id = 1;
        public const int B2Id = 2;
        public const int A2Id = 3;
        public const int A3Id = 4;

        /// <summary>
        /// Builds a fresh rule instance with its <see cref="Rule.Id"/> set.
        /// Returns null for an unknown id — callers decide whether that is
        /// fatal, since it normally means a version mismatch between peers.
        /// </summary>
        public static Rule Create(int id)
        {
            Rule rule = id switch
            {
                A1Id => new A1(),
                B2Id => new B2(),
                A2Id => new A2(),
                A3Id => new A3(),
                _ => null,
            };
            if (rule != null) rule.Id = id;
            return rule;
        }

        public static bool IsKnown(int id) => Create(id) != null;

        /// <summary>Ids of every rule that can be the in-game rule.</summary>
        public static IEnumerable<int> InGameRuleIds => AllIds.Where(id => Create(id).Type == RuleType.InGame);

        /// <summary>Ids of every rule that can be the end-of-game rule.</summary>
        public static IEnumerable<int> FinalRuleIds => AllIds.Where(id => Create(id).Type == RuleType.InTheEnd);

        /// <summary>Fallbacks matching the rules the game shipped with.</summary>
        public static int DefaultInGameRuleId => A1Id;
        public static int DefaultFinalRuleId => B2Id;

        private static IEnumerable<int> AllIds => new[] { A1Id, B2Id, A2Id, A3Id };

        /// <summary>
        /// Случайное правило каждого вида на партию.
        ///
        /// <para>Выбирает <b>только сервер</b>, в <c>Room.StartGame</c>, и
        /// рассылает выбранные id в <c>GameStartedEvent</c>. Дай выбирать
        /// клиенту — и за одним столом оказались бы разные правила.</para>
        ///
        /// <para><paramref name="rng"/> передаётся снаружи, а не создаётся
        /// здесь: два стола, стартовавшие в один тик, получили бы от
        /// <c>new Random()</c> одинаковое зерно и одинаковые правила.</para>
        /// </summary>
        public static int RandomInGameRuleId(Random rng) => PickOrDefault(InGameRuleIds, DefaultInGameRuleId, rng);

        /// <inheritdoc cref="RandomInGameRuleId"/>
        public static int RandomFinalRuleId(Random rng) => PickOrDefault(FinalRuleIds, DefaultFinalRuleId, rng);

        private static int PickOrDefault(IEnumerable<int> ids, int fallback, Random rng)
        {
            var pool = ids.ToList();
            if (pool.Count == 0) return fallback;
            return pool[rng.Next(pool.Count)];
        }
    }
}
