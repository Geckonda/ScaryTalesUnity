using System.Collections.Generic;
using System.Threading.Tasks;
using Assets.Libraries.ScaryTales.CardEffects;
using NUnit.Framework;
using ScaryTales;
using ScaryTales.CardEffects;
using ScaryTales.Enums;
using ScaryTales.Tests.Fakes;

namespace ScaryTales.Tests.Cards
{
    /// <summary>
    /// Что происходит, когда клиент отвечает не тем, что ему предлагали.
    ///
    /// <para><b>Почему это важно.</b> <c>NetworkDecisionRouter</c> проверяет,
    /// что ответ пришёл от того игрока, которого спросили, и что тип ответа
    /// совпадает с типом вопроса. Но он <b>не проверяет, что выбранный id был
    /// среди кандидатов</b> — список кандидатов остаётся у эффекта. Значит
    /// подделанный интент доходит до эффекта как есть.</para>
    ///
    /// <para>Часть эффектов к этому готова и берёт <c>FirstOrDefault</c> с
    /// проверкой (правила A1-2, A2-2, A3-2, Тайная помощница). Часть — берёт
    /// <c>First</c> и падает с <see cref="System.InvalidOperationException"/>
    /// прямо внутри цикла хода. Комната ловит это в общий catch и обрывает
    /// партию: один игрок может выкинуть со стола всех.</para>
    ///
    /// <para>Тесты ниже описывают <b>желаемое</b> поведение — «не падать» — и
    /// отложены, пока эффекты не починены. Починка минимальная и уже принята в
    /// других местах: <c>FirstOrDefault</c> и ранний выход.</para>
    /// </summary>
    [TestFixture]
    public class ForgedDecisionTests
    {
        private const string FixNote =
            "Известная дыра: эффект берёт cards.First(c => c.Id == pick.CardId) и падает " +
            "на id, которого не было среди кандидатов. Клиент может уронить комнату " +
            "подделанным интентом. Починка — FirstOrDefault с ранним выходом, как уже " +
            "сделано в REfA12/A22/A32 и SecretHelperEffect. Снять Ignore после починки.";

        [Test]
        [Ignore(FixNote)]
        public void Дракон_переживает_поддельный_выбор()
        {
            var game = GameFixture.Start();
            game.OnBoard(new TestCard(name: "Пещера", type: CardType.Place) { Id = 11 });
            game.Router.PickForgedCardId();

            Assert.DoesNotThrow(() => RunSync(new DragonEffect().ApplyEffect(game.Context)));
        }

        [Test]
        [Ignore(FixNote)]
        public void Тёмный_владыка_переживает_поддельный_выбор()
        {
            var game = GameFixture.Start();
            game.OnBoard(new TestCard(name: "Замок", type: CardType.Place) { Id = 11 });
            game.Router.PickForgedCardId();

            Assert.DoesNotThrow(() => RunSync(new DarkLordEffect().ApplyEffect(game.Context)));
        }

        [Test]
        [Ignore(FixNote)]
        public void Кража_карты_переживает_поддельный_выбор()
        {
            var game = GameFixture.Start();
            game.OnBoard(new TestCard(name: "Фея", type: CardType.Woman) { Id = 11 });
            game.Router.PickForgedCardId();

            var effect = new CardStealEffect(new List<CardType> { CardType.Woman });

            Assert.DoesNotThrow(() => RunSync(effect.ApplyEffect(game.Context)));
        }

        // ---- А это работает уже сейчас ----

        [Test]
        public void Выдача_предмета_не_падает_на_поддельном_типе()
        {
            // ItemDistributor передаёт выбранный тип в GetItemByType без
            // проверки, что он вообще предлагался, — но пустой результат
            // обрабатывает. Уронить комнату этим нельзя.
            var game = GameFixture.Start();
            game.DrainSupply(ItemType.Sword);
            game.Router.PickItemType(ItemType.Sword); // предлагали монету, отвечаем мечом

            var effect = new ItemDistributor(new List<ItemType> { ItemType.Coin });

            Assert.DoesNotThrow(() => RunSync(effect.ApplyEffect(game.Context)));
            Assert.That(game.Current.ItemsBagCount, Is.Zero);
        }

        [Test]
        public void Выдача_предмета_отдаёт_непредложенный_тип_если_он_есть_в_запасе()
        {
            // Меньшая беда, чем падение, но всё же дыра: игрок получает
            // предмет, которого ему не предлагали. Тест закрепляет факт, чтобы
            // при починке дырой занялись осознанно.
            var game = GameFixture.Start();
            game.Router.PickItemType(ItemType.Sword);

            var effect = new ItemDistributor(new List<ItemType> { ItemType.Coin });
            RunSync(effect.ApplyEffect(game.Context));

            Assert.That(game.Current.ItemAmount(ItemType.Sword), Is.EqualTo(1),
                "поведение изменилось — проверьте, не починена ли дыра");
        }

        private static void RunSync(Task task) => task.GetAwaiter().GetResult();
    }
}
