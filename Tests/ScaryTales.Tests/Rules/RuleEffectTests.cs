using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Assets.Libraries.ScaryTales.Rules.Effects;
using NUnit.Framework;
using ScaryTales;
using ScaryTales.Cards;
using ScaryTales.Enums;
using ScaryTales.Tests.Fakes;

namespace ScaryTales.Tests.Rules
{
    /// <summary>
    /// Эффекты правил партии. У каждого проверяются три вещи: когда он
    /// доступен, что делает и что происходит при отказе игрока.
    ///
    /// <para>Отказ важнее, чем кажется. Предметы в этой игре не
    /// возобновляются, и «спросить до того, как потратить» — это правило,
    /// которое здесь однажды уже нарушалось.</para>
    /// </summary>
    [TestFixture]
    public class RuleEffectTests
    {
        private static bool RunSync(Task<bool> task) => task.GetAwaiter().GetResult();

        private static List<Card> Filler(int count = 20) =>
            new List<Card> { new TestCard(name: "Пустышка", countInDeck: count) };

        private static Card Monster(GameFixture game, int id = 11) =>
            game.OnBoard(new TestCard(name: "Злодей", type: CardType.Monster) { Id = id });

        // ================= A1-1: доспех → верхняя карта сброса =================

        [Test]
        public void A11_недоступно_без_доспеха()
        {
            var game = GameFixture.Start(playerCount: 1, deck: Filler());
            game.Board.AddCardToDiscardPile(new TestCard(name: "В сбросе"));

            Assert.That(new REfA11(1).IsEffectAvailable(game.Context), Is.False);
        }

        [Test]
        public void A11_недоступно_при_пустом_сбросе()
        {
            var game = GameFixture.Start(playerCount: 1, deck: Filler());
            game.GiveItem(ItemType.Armor);

            Assert.That(new REfA11(1).IsEffectAvailable(game.Context), Is.False);
        }

        [Test]
        public void A11_меняет_доспех_на_верхнюю_карту_сброса()
        {
            var game = GameFixture.Start(playerCount: 1, deck: Filler());
            game.GiveItem(ItemType.Armor);
            game.Board.AddCardToDiscardPile(new TestCard(name: "Нижняя"));
            var top = new TestCard(name: "Верхняя");
            game.Board.AddCardToDiscardPile(top);

            Assert.That(RunSync(new REfA11(1).ApplyEffect(game.Context)), Is.True);

            Assert.That(game.Current.Hand, Does.Contain(top));
            Assert.That(game.Current.ItemAmount(ItemType.Armor), Is.Zero);
            Assert.That(game.Board.DiscardPileCount(), Is.EqualTo(1));
        }

        [Test]
        public void A11_отказывает_когда_условия_не_выполнены()
        {
            var game = GameFixture.Start(playerCount: 1, deck: Filler());

            Assert.That(RunSync(new REfA11(1).ApplyEffect(game.Context)), Is.False);
            Assert.That(game.Notifier.Said("Условия не выполнены"));
        }

        // ================= A1-2: меч + злодей → 3 ПО =================

        [Test]
        public void A12_недоступно_без_меча_или_без_злодея()
        {
            var game = GameFixture.Start(playerCount: 1, deck: Filler());
            Monster(game);
            Assert.That(new REfA12(2).IsEffectAvailable(game.Context), Is.False, "без меча");

            var other = GameFixture.Start(playerCount: 1, deck: Filler());
            other.GiveItem(ItemType.Sword);
            Assert.That(new REfA12(2).IsEffectAvailable(other.Context), Is.False, "без злодея");
        }

        [Test]
        public void A12_меняет_меч_и_злодея_на_три_очка()
        {
            var game = GameFixture.Start(playerCount: 1, deck: Filler());
            game.GiveItem(ItemType.Sword);
            var monster = Monster(game);
            game.Router.PickCardId(monster.Id);

            Assert.That(RunSync(new REfA12(2).ApplyEffect(game.Context)), Is.True);

            Assert.That(game.Current.Score, Is.EqualTo(3));
            Assert.That(game.Current.ItemAmount(ItemType.Sword), Is.Zero);
            Assert.That(game.Board.GetCardsFromDiscardPile(), Does.Contain(monster));
        }

        [Test]
        public void A12_разрешает_отказаться()
        {
            // Разрешено ровно потому, что к моменту вопроса ещё ничего не
            // потрачено.
            var game = GameFixture.Start(playerCount: 1, deck: Filler());
            game.GiveItem(ItemType.Sword);
            Monster(game);

            Assert.That(game.Router.AskCount, Is.Zero);
            game.Router.PickFirstCard();
            RunSync(new REfA12(2).ApplyEffect(game.Context));

            Assert.That(game.Router.CardRequests.Single().CanCancel, Is.True);
        }

        [Test]
        public void A12_при_отказе_не_забирает_меч()
        {
            // Прежний порядок — забрать, потом спросить — терял меч впустую и
            // при отказе, и при выходе игрока. Злодей при этом оставался жив.
            var game = GameFixture.Start(playerCount: 1, deck: Filler());
            game.GiveItem(ItemType.Sword);
            var monster = Monster(game);
            game.Router.Decline();

            Assert.Throws<System.OperationCanceledException>(
                () => RunSync(new REfA12(2).ApplyEffect(game.Context)));

            Assert.That(game.Current.ItemAmount(ItemType.Sword), Is.EqualTo(1), "меч потрачен впустую");
            Assert.That(game.Board.GetCardsOnBoard(), Does.Contain(monster));
        }

        [Test]
        public void A12_при_уходе_игрока_не_забирает_меч()
        {
            var game = GameFixture.Start(playerCount: 1, deck: Filler());
            game.GiveItem(ItemType.Sword);
            Monster(game);
            game.Router.Abandon();

            Assert.Throws<System.OperationCanceledException>(
                () => RunSync(new REfA12(2).ApplyEffect(game.Context)));

            Assert.That(game.Current.ItemAmount(ItemType.Sword), Is.EqualTo(1));
        }

        [Test]
        public void A12_переживает_поддельный_выбор_злодея()
        {
            // Здесь защита уже написана: FirstOrDefault и ранний выход.
            var game = GameFixture.Start(playerCount: 1, deck: Filler());
            game.GiveItem(ItemType.Sword);
            Monster(game);
            game.Router.PickForgedCardId();

            Assert.That(RunSync(new REfA12(2).ApplyEffect(game.Context)), Is.False);
            Assert.That(game.Current.ItemAmount(ItemType.Sword), Is.EqualTo(1));
        }

        // ================= A1-3: палка → карта, монета, 1 ПО =================

        [Test]
        public void A13_доступно_только_с_палкой()
        {
            var game = GameFixture.Start(playerCount: 1, deck: Filler());
            Assert.That(new REfA13(3).IsEffectAvailable(game.Context), Is.False);

            game.GiveItem(ItemType.MagicStick);
            Assert.That(new REfA13(3).IsEffectAvailable(game.Context), Is.True);
        }

        [Test]
        public void A13_меняет_палку_на_карту_монету_и_очко()
        {
            var game = GameFixture.Start(playerCount: 1, deck: Filler());
            game.GiveItem(ItemType.MagicStick);

            Assert.That(RunSync(new REfA13(3).ApplyEffect(game.Context)), Is.True);

            Assert.That(game.Current.HandCardCount, Is.EqualTo(1));
            Assert.That(game.Current.ItemAmount(ItemType.Coin), Is.EqualTo(1));
            Assert.That(game.Current.Score, Is.EqualTo(1));
            Assert.That(game.Current.ItemAmount(ItemType.MagicStick), Is.Zero);
        }

        [Test]
        public void A13_на_пустом_запасе_монет_всё_равно_даёт_очко()
        {
            // Иначе палка ушла бы вообще ни за что.
            var game = GameFixture.Start(playerCount: 1, deck: Filler());
            game.GiveItem(ItemType.MagicStick);
            game.DrainSupply(ItemType.Coin);

            Assert.That(RunSync(new REfA13(3).ApplyEffect(game.Context)), Is.True);

            Assert.That(game.Current.Score, Is.EqualTo(1));
            Assert.That(game.Notifier.Said("Не осталось золотых монет"));
        }

        [Test]
        public void A13_на_пустой_колоде_всё_равно_даёт_очко_и_монету()
        {
            var game = GameFixture.Start(playerCount: 1, deck: Filler());
            game.GiveItem(ItemType.MagicStick);
            game.DrainDeck();

            Assert.That(RunSync(new REfA13(3).ApplyEffect(game.Context)), Is.True);

            Assert.That(game.Current.Score, Is.EqualTo(1));
            Assert.That(game.Current.ItemAmount(ItemType.Coin), Is.EqualTo(1));
        }

        [Test]
        [Ignore("Дефект: палка списывается ДО взятия, а взятие может быть запрещено Ведьмой. " +
                "GameManager.DrawCard возвращает void, поэтому семь мест вызова не могут узнать " +
                "об отказе, и палка пропадает впустую. Числится в docs/OPEN_QUESTIONS.md. " +
                "Починка — вернуть из DrawCard признак успеха и проверять его до списания. " +
                "Снять Ignore после починки.")]
        public void A13_не_тратит_палку_если_взятие_запрещено_ведьмой()
        {
            var game = GameFixture.Start(playerCount: 1, deck: Filler());
            game.GiveItem(ItemType.MagicStick);
            game.OnBoard(new WitchCard());

            RunSync(new REfA13(3).ApplyEffect(game.Context));

            Assert.That(game.Current.ItemAmount(ItemType.MagicStick), Is.EqualTo(1),
                "палка потрачена, а карту взять не дали");
        }

        // ================= A2-1: палка → дополнительный розыгрыш =================

        [Test]
        public void A21_недоступно_с_пустой_рукой()
        {
            // Разыграть дополнительную карту нечем, а палка ушла бы
            // безвозвратно. Цикл хода оборвал бы предложение и сам, но уже
            // после списания предмета.
            var game = GameFixture.Start(playerCount: 1, deck: Filler());
            game.GiveItem(ItemType.MagicStick);

            Assert.That(new REfA21(1).IsEffectAvailable(game.Context), Is.False);
        }

        [Test]
        public void A21_меняет_палку_на_дополнительный_розыгрыш()
        {
            var game = GameFixture.Start(playerCount: 1, deck: Filler());
            game.GiveItem(ItemType.MagicStick);
            game.InHand(new TestCard(name: "Есть чем играть"));

            Assert.That(RunSync(new REfA21(1).ApplyEffect(game.Context)), Is.True);

            Assert.That(game.State.ExtraPlays, Is.EqualTo(1));
            Assert.That(game.Current.ItemAmount(ItemType.MagicStick), Is.Zero);
        }

        // ================= A2-2: меч + злодей → монета, 1 ПО =================

        [Test]
        public void A22_меняет_меч_и_злодея_на_монету_и_очко()
        {
            var game = GameFixture.Start(playerCount: 1, deck: Filler());
            game.GiveItem(ItemType.Sword);
            var monster = Monster(game);
            game.Router.PickCardId(monster.Id);

            Assert.That(RunSync(new REfA22(2).ApplyEffect(game.Context)), Is.True);

            Assert.That(game.Current.ItemAmount(ItemType.Coin), Is.EqualTo(1));
            Assert.That(game.Current.Score, Is.EqualTo(1));
            Assert.That(game.Board.GetCardsFromDiscardPile(), Does.Contain(monster));
        }

        [Test]
        public void A22_на_пустом_запасе_монет_всё_равно_убивает_злодея_и_даёт_очко()
        {
            var game = GameFixture.Start(playerCount: 1, deck: Filler());
            game.GiveItem(ItemType.Sword);
            var monster = Monster(game);
            game.DrainSupply(ItemType.Coin);
            game.Router.PickCardId(monster.Id);

            Assert.That(RunSync(new REfA22(2).ApplyEffect(game.Context)), Is.True);

            Assert.That(game.Current.Score, Is.EqualTo(1));
            Assert.That(game.Board.GetCardsFromDiscardPile(), Does.Contain(monster));
        }

        [Test]
        public void A22_при_отказе_не_забирает_меч()
        {
            var game = GameFixture.Start(playerCount: 1, deck: Filler());
            game.GiveItem(ItemType.Sword);
            Monster(game);
            game.Router.Decline();

            Assert.Throws<System.OperationCanceledException>(
                () => RunSync(new REfA22(2).ApplyEffect(game.Context)));

            Assert.That(game.Current.ItemAmount(ItemType.Sword), Is.EqualTo(1));
        }

        // ================= A3-1: доспех + меч → 2 карты, 3 ПО =================

        [Test]
        public void A31_недоступно_без_обоих_предметов()
        {
            var game = GameFixture.Start(playerCount: 1, deck: Filler());
            game.GiveItem(ItemType.Armor);

            Assert.That(new REfA31(1).IsEffectAvailable(game.Context), Is.False);

            game.GiveItem(ItemType.Sword);
            Assert.That(new REfA31(1).IsEffectAvailable(game.Context), Is.True);
        }

        [Test]
        public void A31_меняет_доспех_и_меч_на_две_карты_и_три_очка()
        {
            var game = GameFixture.Start(playerCount: 1, deck: Filler());
            game.GiveItem(ItemType.Armor);
            game.GiveItem(ItemType.Sword);

            Assert.That(RunSync(new REfA31(1).ApplyEffect(game.Context)), Is.True);

            Assert.That(game.Current.HandCardCount, Is.EqualTo(2));
            Assert.That(game.Current.Score, Is.EqualTo(3));
            Assert.That(game.Current.ItemsBagCount, Is.Zero);
        }

        [Test]
        public void A31_на_пустой_колоде_всё_равно_даёт_очки()
        {
            // Иначе доспех и меч ушли бы вообще ни за что.
            var game = GameFixture.Start(playerCount: 1, deck: Filler());
            game.GiveItem(ItemType.Armor);
            game.GiveItem(ItemType.Sword);
            game.DrainDeck();

            Assert.That(RunSync(new REfA31(1).ApplyEffect(game.Context)), Is.True);

            Assert.That(game.Current.HandCardCount, Is.Zero);
            Assert.That(game.Current.Score, Is.EqualTo(3));
        }

        [Test]
        public void A31_на_колоде_из_одной_карты_берёт_сколько_есть()
        {
            var game = GameFixture.Start(playerCount: 1,
                deck: new List<Card> { new TestCard(name: "Единственная") });
            game.GiveItem(ItemType.Armor);
            game.GiveItem(ItemType.Sword);

            RunSync(new REfA31(1).ApplyEffect(game.Context));

            Assert.That(game.Current.HandCardCount, Is.EqualTo(1));
            Assert.That(game.Current.Score, Is.EqualTo(3));
        }

        // ================= A3-2: палка + злодей → монета, 1 ПО =================

        [Test]
        public void A32_меняет_палку_и_злодея_на_монету_и_очко()
        {
            var game = GameFixture.Start(playerCount: 1, deck: Filler());
            game.GiveItem(ItemType.MagicStick);
            var monster = Monster(game);
            game.Router.PickCardId(monster.Id);

            Assert.That(RunSync(new REfA32(2).ApplyEffect(game.Context)), Is.True);

            Assert.That(game.Current.ItemAmount(ItemType.Coin), Is.EqualTo(1));
            Assert.That(game.Current.ItemAmount(ItemType.MagicStick), Is.Zero);
            Assert.That(game.Current.Score, Is.EqualTo(1));
        }

        [Test]
        public void A32_спрашивает_до_того_как_забрать_палку()
        {
            var game = GameFixture.Start(playerCount: 1, deck: Filler());
            game.GiveItem(ItemType.MagicStick);
            Monster(game);
            game.Router.Decline();

            Assert.Throws<System.OperationCanceledException>(
                () => RunSync(new REfA32(2).ApplyEffect(game.Context)));

            Assert.That(game.Current.ItemAmount(ItemType.MagicStick), Is.EqualTo(1),
                "палка потрачена при отказе");
        }

        [Test]
        public void A32_недоступно_без_злодея_на_столе()
        {
            var game = GameFixture.Start(playerCount: 1, deck: Filler());
            game.GiveItem(ItemType.MagicStick);

            Assert.That(new REfA32(2).IsEffectAvailable(game.Context), Is.False);
        }

        // ================= B2-1: наборы «3 монеты + палка» → 12 ПО =================

        [Test]
        public void B21_без_набора_ничего_не_даёт()
        {
            var game = GameFixture.Start(playerCount: 1, deck: Filler());
            game.GiveItem(ItemType.Coin);
            game.GiveItem(ItemType.Coin);
            game.GiveItem(ItemType.MagicStick);

            var scoring = new REfB21(1).Score(game.Context, game.Current);

            Assert.That(scoring.Points, Is.Zero, "двух монет на набор не хватает");
        }

        [Test]
        public void B21_один_набор_даёт_двенадцать_очков()
        {
            var game = GameFixture.Start(playerCount: 1, deck: Filler());
            for (int i = 0; i < 3; i++) game.GiveItem(ItemType.Coin);
            game.GiveItem(ItemType.MagicStick);

            var scoring = new REfB21(1).Score(game.Context, game.Current);

            Assert.That(scoring.Points, Is.EqualTo(12));
        }

        [Test]
        public void B21_два_набора_дают_двадцать_четыре_очка()
        {
            var game = GameFixture.Start(playerCount: 1, deck: Filler());
            for (int i = 0; i < 6; i++) game.GiveItem(ItemType.Coin);
            game.GiveItem(ItemType.MagicStick);
            game.GiveItem(ItemType.MagicStick);

            var scoring = new REfB21(1).Score(game.Context, game.Current);

            Assert.That(scoring.Points, Is.EqualTo(24));
        }

        [Test]
        public void B21_остаток_сверх_наборов_не_считается()
        {
            // Пять монет и одна палка — это один набор, а не полтора.
            var game = GameFixture.Start(playerCount: 1, deck: Filler());
            for (int i = 0; i < 5; i++) game.GiveItem(ItemType.Coin);
            game.GiveItem(ItemType.MagicStick);

            var scoring = new REfB21(1).Score(game.Context, game.Current);

            Assert.That(scoring.Points, Is.EqualTo(12));
        }

        [Test]
        public void B21_ограничен_тем_чего_меньше()
        {
            // Восемь монет, но одна палка — набор всё равно один.
            var game = GameFixture.Start(playerCount: 1, deck: Filler());
            for (int i = 0; i < 8; i++) game.GiveItem(ItemType.Coin);
            game.GiveItem(ItemType.MagicStick);

            Assert.That(new REfB21(1).Score(game.Context, game.Current).Points, Is.EqualTo(12));
        }

        [Test]
        public void B21_перечисляет_ровно_те_предметы_что_сыграли()
        {
            // Показу нужно подсветить именно сыгравшие монеты и палки, а не
            // «монеты вообще».
            var game = GameFixture.Start(playerCount: 1, deck: Filler());
            for (int i = 0; i < 5; i++) game.GiveItem(ItemType.Coin);
            game.GiveItem(ItemType.MagicStick);

            var counted = new REfB21(1).Score(game.Context, game.Current).CountedItems;

            Assert.That(counted.Count(t => t == ItemType.Coin), Is.EqualTo(3));
            Assert.That(counted.Count(t => t == ItemType.MagicStick), Is.EqualTo(1));
        }

        [Test]
        public void B21_у_игрока_без_предметов_ничего_не_считает()
        {
            var game = GameFixture.Start(playerCount: 1, deck: Filler());

            Assert.That(new REfB21(1).Score(game.Context, game.Current).Points, Is.Zero);
            Assert.That(new REfB21(1).IsEffectAvailable(game.Context), Is.False);
        }

        [Test]
        public void B21_на_null_игроке_не_падает()
        {
            // Разбор конца партии идёт по списку, из которого игроки могли
            // выйти.
            var game = GameFixture.Start(playerCount: 1, deck: Filler());

            Assert.That(new REfB21(1).Score(game.Context, null).Points, Is.Zero);
        }

        [Test]
        public void B21_подсчёт_ничего_не_меняет()
        {
            // Начисляет очки тот, кто вызвал: разбор должен успеть рассказать
            // клиентам, что насчитал, прежде чем счёт вырастет на табличке.
            var game = GameFixture.Start(playerCount: 1, deck: Filler());
            for (int i = 0; i < 3; i++) game.GiveItem(ItemType.Coin);
            game.GiveItem(ItemType.MagicStick);

            new REfB21(1).Score(game.Context, game.Current);

            Assert.That(game.Current.Score, Is.Zero);
            Assert.That(game.Current.ItemsBagCount, Is.EqualTo(4), "подсчёт забрал предметы");
        }
    }
}
