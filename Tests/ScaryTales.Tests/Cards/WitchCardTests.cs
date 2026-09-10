using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using ScaryTales;
using ScaryTales.Cards;
using ScaryTales.Enums;
using ScaryTales.Tests.Fakes;

namespace ScaryTales.Tests.Cards
{
    /// <summary>
    /// Ведьма — сделка: играешь быстрее, но рука больше не пополняется.
    ///
    /// <para>Вся её сила постоянная и живёт в трёх переопределениях
    /// <see cref="Card"/>, а не в мгновенном эффекте. Каждое проверяется
    /// отдельно, и отдельно — то, как они складываются: без сброса в начале
    /// хода Ведьма подвешивает партию насмерть.</para>
    /// </summary>
    [TestFixture]
    public class WitchCardTests
    {
        // ---- Запрет на взятие ----

        [Test]
        public void Ведьма_запрещает_владельцу_брать_из_колоды()
        {
            var game = GameFixture.Start();
            game.OnBoard(new WitchCard());

            game.Manager.DrawCard(game.Current);

            Assert.That(game.Current.HandCardCount, Is.Zero);
            Assert.That(game.Notifier.Said("не может брать карты из колоды"));
        }

        [Test]
        public void Ведьма_не_мешает_брать_остальным()
        {
            // Запрет привязан к владельцу карты, а не к столу.
            var game = GameFixture.Start(playerCount: 2);
            var other = game.Players[1];
            game.OnBoard(new WitchCard(), game.Current);

            game.Manager.DrawCard(other);

            Assert.That(other.HandCardCount, Is.EqualTo(1));
        }

        [Test]
        public void Запрет_накрывает_и_взятия_от_эффектов_карт()
        {
            // Проверка стоит в GameManager.DrawCard — единственной воронке, а
            // не в цикле хода. Иначе Ведьма запрещала бы только обязательное
            // взятие, а Купец обходил бы её.
            var game = GameFixture.Start(deck: MerchantDeck());
            game.OnBoard(new WitchCard());
            var merchant = game.InHand(new MerchantCard { Id = 500 });

            RunSync(game.Manager.PlayCard(merchant));

            // Купец берёт две карты — обе должны быть запрещены. В руке пусто:
            // сам Купец ушёл на стол.
            Assert.That(game.Current.HandCardCount, Is.Zero, "Купец обошёл запрет Ведьмы");
        }

        [Test]
        public void Волшебник_обходит_запрет_ведьмы()
        {
            // Волшебник берёт через TryDrawCardFromDeck напрямую, минуя
            // DrawCard, — и потому запрет на него не действует.
            //
            // Это НЕ утверждение, что так правильно: вопрос числится открытым
            // в docs/OPEN_QUESTIONS.md. Тест закрепляет фактическое поведение,
            // чтобы решение принималось осознанно, а не случилось само.
            var game = GameFixture.Start(deck: WizardDeck());
            game.OnBoard(new WitchCard());
            var wizard = game.InHand(new WizardCard { Id = 500 });

            int deckBefore = game.Deck.CardsRemaining;
            RunSync(game.Manager.PlayCard(wizard));

            Assert.That(game.Deck.CardsRemaining, Is.LessThan(deckBefore),
                "Волшебник перестал обходить запрет — поведение изменилось, см. OPEN_QUESTIONS");
        }

        // ---- Дополнительный розыгрыш ----

        [Test]
        public void Ведьма_даёт_розыгрыш_в_тот_же_ход_когда_её_выложили()
        {
            // «Можете играть 1 дополнительную карту в свой ход» — а ход, в
            // который её выложили, тоже свой.
            var game = GameFixture.Start();
            var witch = game.InHand(new WitchCard { Id = 500 });

            RunSync(game.Manager.PlayCard(witch));

            Assert.That(game.State.ExtraPlays, Is.EqualTo(1));
        }

        [Test]
        public void Ведьма_даёт_розыгрыш_и_в_следующие_ходы()
        {
            var game = GameFixture.Start(playerCount: 1);
            game.OnBoard(new WitchCard());
            game.InHand(new TestCard(name: "Что-то"));
            game.State.ClearExtraPlays();

            game.Manager.ApplyStartOfTurnBoardRules(game.Current);

            Assert.That(game.State.ExtraPlays, Is.EqualTo(1));
        }

        [Test]
        public void Розыгрыш_не_выдаётся_дважды_в_ход_когда_её_выложили()
        {
            // В начале ЭТОГО хода карты на столе ещё не было, поэтому
            // ApplyStartOfTurnBoardRules её не увидел, а PlayCard выдал право
            // один раз. Задвоения нет — и это ровно то, что легко сломать,
            // передвинув одну из двух выдач.
            var game = GameFixture.Start(playerCount: 1);
            var witch = game.InHand(new WitchCard { Id = 500 });

            game.Manager.ApplyStartOfTurnBoardRules(game.Current); // Ведьмы на столе ещё нет
            RunSync(game.Manager.PlayCard(witch));

            Assert.That(game.State.ExtraPlays, Is.EqualTo(1), "право на розыгрыш выдано дважды");
        }

        [Test]
        public void Две_ведьмы_дают_два_розыгрыша()
        {
            var game = GameFixture.Start(playerCount: 1);
            game.OnBoard(new WitchCard());
            game.OnBoard(new WitchCard());
            game.InHand(new TestCard());
            game.State.ClearExtraPlays();

            game.Manager.ApplyStartOfTurnBoardRules(game.Current);

            Assert.That(game.State.ExtraPlays, Is.EqualTo(2));
        }

        // ---- Сброс в начале хода: защита от дедлока ----

        [Test]
        public void Ведьма_уходит_в_сброс_когда_рука_опустела()
        {
            var game = GameFixture.Start(playerCount: 1);
            var witch = game.OnBoard(new WitchCard());
            // Рука пуста

            game.Manager.ApplyStartOfTurnBoardRules(game.Current);

            Assert.That(game.Board.GetCardsOnBoard(), Does.Not.Contain(witch));
            Assert.That(game.Board.GetCardsFromDiscardPile(), Does.Contain(witch));
        }

        [Test]
        public void Ведьма_уходит_в_сброс_когда_колода_кончилась()
        {
            var game = GameFixture.Start(playerCount: 1);
            var witch = game.OnBoard(new WitchCard());
            game.InHand(new TestCard(name: "Ещё есть чем играть"));
            game.DrainDeck();

            game.Manager.ApplyStartOfTurnBoardRules(game.Current);

            Assert.That(game.Board.GetCardsFromDiscardPile(), Does.Contain(witch));
        }

        [Test]
        public void Ведьма_остаётся_пока_есть_и_рука_и_колода()
        {
            var game = GameFixture.Start(playerCount: 1);
            var witch = game.OnBoard(new WitchCard());
            game.InHand(new TestCard());

            game.Manager.ApplyStartOfTurnBoardRules(game.Current);

            Assert.That(game.Board.GetCardsOnBoard(), Does.Contain(witch));
        }

        [Test]
        public void Ведьма_без_владельца_не_сбрасывается()
        {
            var game = GameFixture.Start(playerCount: 1);
            var witch = new WitchCard(); // Owner == null
            game.Board.AddCardOnBoard(witch);

            Assert.That(witch.ShouldDiscardAtTurnStart(game.Context), Is.False);
        }

        [Test]
        public void Ушедшая_ведьма_больше_не_запрещает_взятие()
        {
            // Порядок в ApplyStartOfTurnBoardRules: сброс идёт ДО обязательного
            // взятия. Сделай мы наоборот — ведьма успела бы запретить взятие,
            // рука осталась бы пустой, и цикл хода объявил бы конец партии
            // вместо продолжения.
            var game = GameFixture.Start(playerCount: 1);
            game.OnBoard(new WitchCard());
            // Рука пуста — Ведьма обязана уйти

            game.Manager.ApplyStartOfTurnBoardRules(game.Current);
            game.Manager.DrawCard(game.Current);

            Assert.That(game.Current.HandCardCount, Is.EqualTo(1),
                "Ведьма ушла, но взятие всё равно осталось запрещённым — партия зависла бы");
        }

        [Test]
        public void Ушедшая_ведьма_не_дарит_розыгрыш_на_прощание()
        {
            // Сброс раньше выдачи прав — иначе уходящая карта успевала бы
            // подарить розыгрыш, которым уже нечего было бы разыграть.
            var game = GameFixture.Start(playerCount: 1);
            game.OnBoard(new WitchCard());
            game.State.ClearExtraPlays();

            game.Manager.ApplyStartOfTurnBoardRules(game.Current);

            Assert.That(game.State.ExtraPlays, Is.Zero);
        }

        // ---- Свойства карты ----

        [Test]
        public void У_ведьмы_нет_мгновенного_эффекта()
        {
            // Пустой эффект — не заглушка, а честный ответ: вся сила карты
            // постоянная.
            var game = GameFixture.Start();
            var witch = game.InHand(new WitchCard { Id = 500 });

            RunSync(game.Manager.PlayCard(witch));

            Assert.That(game.Current.Score, Is.Zero);
            Assert.That(game.Router.AskCount, Is.Zero, "Ведьма ничего не должна спрашивать");
        }

        [Test]
        public void Ведьма_ложится_перед_игроком_а_не_на_общий_стол()
        {
            // От этого зависит всё остальное: постоянные правила спрашиваются
            // у карт, лежащих перед игроком.
            var witch = new WitchCard();

            Assert.That(witch.PositionAfterPlay, Is.EqualTo(CardPosition.BeforePlayer));
            Assert.That(witch.Points, Is.Zero, "сделка не должна ещё и приносить очки");
        }

        // ---- Вспомогательное ----

        /// <summary>
        /// Часы в фикстуре мгновенные, роутер отвечает из очереди, поэтому
        /// задача завершается синхронно. GetAwaiter().GetResult() разворачивает
        /// исключение как есть, без обёртки AggregateException.
        /// </summary>
        private static void RunSync(Task task) => task.GetAwaiter().GetResult();

        private static List<Card> MerchantDeck() => new List<Card>
        {
            new TestCard(name: "Пустышка", countInDeck: 10),
        };

        private static List<Card> WizardDeck() => new List<Card>
        {
            new TestCard(name: "Пустышка", countInDeck: 10),
        };
    }
}
