using System;
using System.Linq;
using Assets.Libraries.ScaryTales.Enums;
using Assets.Libraries.ScaryTales.Rules;
using Assets.Libraries.ScaryTales.Rules.Templates.A;
using Assets.Libraries.ScaryTales.Rules.Templates.B;
using NUnit.Framework;

namespace ScaryTales.Tests.Contracts
{
    /// <summary>
    /// Id правил — формат провода: сервер бросает кубик один раз и рассылает
    /// выбранные id в GameStartedEvent, каждый клиент пересобирает правило из
    /// каталога по этому id. Перенумеровать существующую запись — значит
    /// показать половине стола не то правило, по которому идёт партия.
    ///
    /// <para>Каталог только дописывается. Эти тесты — напоминание об этом
    /// в исполняемом виде.</para>
    /// </summary>
    [TestFixture]
    public class RuleCatalogTests
    {
        [Test]
        public void Id_правил_закреплены_за_своими_классами()
        {
            Assert.That(RuleCatalog.Create(1), Is.TypeOf<A1>());
            Assert.That(RuleCatalog.Create(2), Is.TypeOf<B2>());
            Assert.That(RuleCatalog.Create(3), Is.TypeOf<A2>());
            Assert.That(RuleCatalog.Create(4), Is.TypeOf<A3>());
        }

        [Test]
        public void Константы_id_совпадают_с_числами_на_проводе()
        {
            Assert.That(RuleCatalog.A1Id, Is.EqualTo(1));
            Assert.That(RuleCatalog.B2Id, Is.EqualTo(2));
            Assert.That(RuleCatalog.A2Id, Is.EqualTo(3));
            Assert.That(RuleCatalog.A3Id, Is.EqualTo(4));
        }

        [Test]
        public void Созданное_правило_помнит_свой_id()
        {
            // Без этого Rule.Id остался бы нулём, и сослаться на правило по
            // проводу было бы нечем — ровно та поломка, ради которой каталог
            // и появился.
            foreach (var id in new[] { 1, 2, 3, 4 })
                Assert.That(RuleCatalog.Create(id).Id, Is.EqualTo(id));
        }

        [Test]
        public void Ноль_это_просьба_выбрать_случайное_а_не_правило()
        {
            Assert.That(RuleCatalog.RandomRuleId, Is.EqualTo(0));
            Assert.That(RuleCatalog.Create(RuleCatalog.RandomRuleId), Is.Null);
            Assert.That(RuleCatalog.IsKnown(RuleCatalog.RandomRuleId), Is.False);
        }

        [Test]
        public void Неизвестный_id_возвращает_null_а_не_бросает()
        {
            // Неизвестный id обычно означает разные версии у сторон. Решать,
            // фатально это или нет, — дело вызывающего, поэтому здесь null.
            Assert.That(RuleCatalog.Create(999), Is.Null);
            Assert.That(RuleCatalog.Create(-1), Is.Null);
            Assert.That(RuleCatalog.IsKnown(999), Is.False);
        }

        [Test]
        public void Правила_разложены_по_видам()
        {
            Assert.That(RuleCatalog.InGameRuleIds, Is.EquivalentTo(new[] { 1, 3, 4 }));
            Assert.That(RuleCatalog.FinalRuleIds, Is.EquivalentTo(new[] { 2 }));
        }

        [Test]
        public void Вид_правила_совпадает_со_списком_в_котором_оно_лежит()
        {
            foreach (var id in RuleCatalog.InGameRuleIds)
                Assert.That(RuleCatalog.Create(id).Type, Is.EqualTo(RuleType.InGame));

            foreach (var id in RuleCatalog.FinalRuleIds)
                Assert.That(RuleCatalog.Create(id).Type, Is.EqualTo(RuleType.InTheEnd));
        }

        [Test]
        public void Умолчания_указывают_на_существующие_правила()
        {
            Assert.That(RuleCatalog.IsKnown(RuleCatalog.DefaultInGameRuleId));
            Assert.That(RuleCatalog.IsKnown(RuleCatalog.DefaultFinalRuleId));
            Assert.That(RuleCatalog.Create(RuleCatalog.DefaultInGameRuleId).Type, Is.EqualTo(RuleType.InGame));
            Assert.That(RuleCatalog.Create(RuleCatalog.DefaultFinalRuleId).Type, Is.EqualTo(RuleType.InTheEnd));
        }

        [Test]
        public void Случайный_выбор_даёт_только_правила_подходящего_вида()
        {
            var rng = new Random(20260910);
            for (int i = 0; i < 200; i++)
            {
                Assert.That(RuleCatalog.Create(RuleCatalog.RandomInGameRuleId(rng)).Type,
                    Is.EqualTo(RuleType.InGame));
                Assert.That(RuleCatalog.Create(RuleCatalog.RandomFinalRuleId(rng)).Type,
                    Is.EqualTo(RuleType.InTheEnd));
            }
        }

        [Test]
        public void Случайный_выбор_со_временем_достаёт_все_правила_своего_вида()
        {
            // Иначе легко не заметить, что новое правило добавили в каталог,
            // но выпасть оно не может.
            var rng = new Random(20260910);
            var seen = Enumerable.Range(0, 500)
                .Select(_ => RuleCatalog.RandomInGameRuleId(rng))
                .Distinct();

            Assert.That(seen, Is.EquivalentTo(RuleCatalog.InGameRuleIds));
        }

        [Test]
        public void У_каждого_правила_есть_имя_и_хотя_бы_один_эффект()
        {
            foreach (var id in RuleCatalog.InGameRuleIds.Concat(RuleCatalog.FinalRuleIds))
            {
                var rule = RuleCatalog.Create(id);
                Assert.That(rule.Name, Is.Not.Null.And.Not.Empty, $"правило {id}: пустое имя");
                Assert.That(rule.Effects, Is.Not.Null.And.Not.Empty, $"правило {id}: нет эффектов");
            }
        }

        [Test]
        public void Id_эффектов_уникальны_внутри_правила()
        {
            // По этому id прилетает UseRuleEffectIntent — дубль означал бы,
            // что сервер применит не тот эффект, который нажал игрок.
            foreach (var id in RuleCatalog.InGameRuleIds.Concat(RuleCatalog.FinalRuleIds))
            {
                var effects = RuleCatalog.Create(id).Effects;
                var ids = effects.Select(e => e.Id).ToList();
                Assert.That(ids.Distinct().Count(), Is.EqualTo(ids.Count),
                    $"правило {id}: повторяющиеся id эффектов");
            }
        }
    }
}
