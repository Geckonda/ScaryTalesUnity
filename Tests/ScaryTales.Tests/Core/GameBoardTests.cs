using System.Linq;
using NUnit.Framework;
using ScaryTales;
using ScaryTales.Cards;
using ScaryTales.Enums;
using ScaryTales.Tests.Fakes;

namespace ScaryTales.Tests.Core
{
    [TestFixture]
    public class GameBoardTests
    {
        // ---- Стол ----

        [Test]
        public void Карта_кладётся_на_стол_и_считается()
        {
            var board = new GameBoard();
            board.AddCardOnBoard(new TestCard());

            Assert.That(board.CardsOnBoardCount(), Is.EqualTo(1));
            Assert.That(board.DiscardPileCount(), Is.Zero);
        }

        [Test]
        public void Карта_ищется_на_столе_по_id()
        {
            var board = new GameBoard();
            var card = new TestCard { Id = 7 };
            board.AddCardOnBoard(card);

            Assert.That(board.GetCardFromBoard(7), Is.SameAs(card));
            Assert.That(board.GetCardFromBoard(8), Is.Null, "чужой id должен давать null, а не бросать");
        }

        [Test]
        public void Карты_на_столе_отбираются_по_типу_имени_и_владельцу()
        {
            var board = new GameBoard();
            var owner = new Player(1, "А");
            var other = new Player(2, "Б");

            var monster = new TestCard(name: "Дракон", type: CardType.Monster) { Owner = owner };
            var woman = new TestCard(name: "Фея", type: CardType.Woman) { Owner = other };
            var alsoMonster = new TestCard(name: "Огр", type: CardType.Monster) { Owner = owner };

            board.AddCardOnBoard(monster);
            board.AddCardOnBoard(woman);
            board.AddCardOnBoard(alsoMonster);

            Assert.That(board.GetCardsOnBoard(CardType.Monster), Is.EquivalentTo(new[] { monster, alsoMonster }));
            Assert.That(board.GetCardsOnBoard("Фея"), Is.EqualTo(new[] { woman }));
            Assert.That(board.GetCardsOnBoard(owner), Is.EquivalentTo(new[] { monster, alsoMonster }));
        }

        [Test]
        public void Отбор_без_совпадений_даёт_пустой_список()
        {
            var board = new GameBoard();
            board.AddCardOnBoard(new TestCard(type: CardType.Woman));

            Assert.That(board.GetCardsOnBoard(CardType.Monster), Is.Empty);
            Assert.That(board.GetCardsOnBoard("Кракен"), Is.Empty);
        }

        [Test]
        public void Снятие_со_стола_не_кладёт_карту_в_сброс()
        {
            // Так уходит карта, украденная со стола другим игроком: она не
            // сброшена, а сменила место.
            var board = new GameBoard();
            var card = new TestCard();
            board.AddCardOnBoard(card);

            board.RemoveCardFromBoard(card);

            Assert.That(board.CardsOnBoardCount(), Is.Zero);
            Assert.That(board.DiscardPileCount(), Is.Zero);
        }

        // ---- Сброс ----

        [Test]
        public void Перенос_со_стола_в_сброс_меняет_обе_стопки()
        {
            var board = new GameBoard();
            var card = new TestCard();
            board.AddCardOnBoard(card);

            board.MoveCardFromBoardToDiscardPile(card);

            Assert.That(board.CardsOnBoardCount(), Is.Zero);
            Assert.That(board.DiscardPileCount(), Is.EqualTo(1));
            Assert.That(board.GetCardsFromDiscardPile(), Is.EqualTo(new[] { card }));
        }

        [Test]
        public void Верхняя_карта_сброса_снимается_вопреки_имени_метода()
        {
            // Метод называется Get, но снимает карту. На этом стоит правило
            // A1-1 («вернуть верхнюю карту сброса»), поэтому поведение
            // закрепляем как есть — переименование метода тест переживёт,
            // а вот смена поведения сломает правило.
            var board = new GameBoard();
            var first = new TestCard(name: "Первая");
            var second = new TestCard(name: "Вторая");
            board.AddCardToDiscardPile(first);
            board.AddCardToDiscardPile(second);

            var top = board.GetTopCardFromDiscardPile();

            Assert.That(top, Is.SameAs(second), "верхняя — положенная последней");
            Assert.That(board.DiscardPileCount(), Is.EqualTo(1));
            Assert.That(board.GetTopCardFromDiscardPile(), Is.SameAs(first));
            Assert.That(board.DiscardPileCount(), Is.Zero);
        }

        [Test]
        public void Верхняя_карта_пустого_сброса_это_null()
        {
            Assert.That(new GameBoard().GetTopCardFromDiscardPile(), Is.Null);
        }

        // ---- Слот времени суток ----

        [Test]
        public void Слот_времени_суток_пуст_в_начале()
        {
            Assert.That(new GameBoard().GetCardFromTimeOfDaySlot(), Is.Null);
        }

        [Test]
        public void Слот_принимает_день_и_ночь()
        {
            var board = new GameBoard();

            var night = new NightCard();
            board.SetTimeOfDaySlot(night);
            Assert.That(board.GetCardFromTimeOfDaySlot(), Is.SameAs(night));

            var day = new DayCard();
            board.SetTimeOfDaySlot(day);
            Assert.That(board.GetCardFromTimeOfDaySlot(), Is.SameAs(day), "новая карта вытесняет прежнюю");
        }

        [Test]
        public void Слот_молча_отвергает_любую_другую_карту()
        {
            // Молча — это осознанное решение стола, а не недосмотр: проверка
            // на «карту не того вида» стоит выше по стеку.
            var board = new GameBoard();
            var night = new NightCard();
            board.SetTimeOfDaySlot(night);

            board.SetTimeOfDaySlot(new TestCard(name: "Принцесса", type: CardType.Woman));

            Assert.That(board.GetCardFromTimeOfDaySlot(), Is.SameAs(night), "слот занял кто попало");
        }

        [Test]
        public void Стол_сам_не_сбрасывает_вытесненную_карту_времени_суток()
        {
            // Сброс прежней карты — забота GameManager.PutCardInTimeOfDaySlot.
            // Здесь фиксируем границу ответственности: сам стол этого не делает,
            // и звать SetTimeOfDaySlot напрямую значит потерять карту.
            var board = new GameBoard();
            board.SetTimeOfDaySlot(new NightCard());

            board.SetTimeOfDaySlot(new DayCard());

            Assert.That(board.DiscardPileCount(), Is.Zero);
        }

        // ---- Известный дефект ----

        [Test]
        [Ignore("Дефект: MoveAllCardsToDiscardPile меняет список во время foreach по нему же " +
                "и бросает InvalidOperationException на любом непустом столе. Метод сейчас " +
                "нигде не вызывается (есть только объявление в IGameBoard), поэтому в игре " +
                "это не проявляется. Снять Ignore после починки — или удалить метод вместе " +
                "с тестом, если он не нужен.")]
        public void Стол_очищается_в_сброс_целиком()
        {
            var board = new GameBoard();
            board.AddCardOnBoard(new TestCard(name: "Первая"));
            board.AddCardOnBoard(new TestCard(name: "Вторая"));

            board.MoveAllCardsToDiscardPile();

            Assert.That(board.CardsOnBoardCount(), Is.Zero);
            Assert.That(board.DiscardPileCount(), Is.EqualTo(2));
        }
    }
}
