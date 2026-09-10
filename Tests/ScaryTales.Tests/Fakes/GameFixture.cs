using System.Collections.Generic;
using System.Linq;
using ScaryTales;
using ScaryTales.Abstractions;
using ScaryTales.Enums;

namespace ScaryTales.Tests.Fakes
{
    /// <summary>
    /// Партия, собранная под конкретный тест.
    ///
    /// <para>Внутри — <b>настоящие</b> <see cref="GameManager"/>,
    /// <see cref="GameBoard"/>, <see cref="Deck"/> и <see cref="ItemManager"/>,
    /// а не заглушки. Эффект карты почти никогда не работает сам по себе: он
    /// зовёт менеджера, тот двигает карту, шлёт события и трогает колоду.
    /// Подменив менеджера, мы проверяли бы, что эффект вызывает нужные методы,
    /// а не что в партии происходит нужное — а расходятся эти две вещи ровно
    /// там, где живут баги.</para>
    ///
    /// <para>Подменены только двое: игрок (<see cref="ScriptedRouter"/>) и темп
    /// показа (<see cref="InstantGameClock"/>).</para>
    /// </summary>
    public class GameFixture
    {
        public GameManager Manager { get; }
        public IGameContext Context => Manager._context;
        public ScriptedRouter Router { get; }
        public RecordingNotifier Notifier { get; }
        public InstantGameClock Clock { get; }

        public IGameBoard Board => Context.GameBoard;
        public IGameState State => Context.GameState;
        public Deck Deck => Context.Deck;
        public ItemManager Items => Context.ItemManager;
        public List<Player> Players => Context.Players;

        /// <summary>Тот, чей сейчас ход.</summary>
        public Player Current => State.GetCurrentPlayer();

        private GameFixture(GameManager manager, ScriptedRouter router, RecordingNotifier notifier, InstantGameClock clock)
        {
            Manager = manager;
            Router = router;
            Notifier = notifier;
            Clock = clock;
        }

        /// <summary>
        /// Партия на заданное число игроков с пустым столом и колодой из
        /// безликих карт. Настоящие карты каталога добавляются тестом — те,
        /// которые ему нужны.
        /// </summary>
        /// <param name="playerCount">Сколько игроков за столом.</param>
        /// <param name="deck">
        /// Шаблоны колоды. По умолчанию — двадцать безликих карт: достаточно,
        /// чтобы взятие срабатывало, и ничего не делающих сверх того.
        /// </param>
        public static GameFixture Start(int playerCount = 2, List<Card> deck = null)
        {
            var players = Enumerable.Range(1, playerCount)
                .Select(i => new Player(i, $"И{i}"))
                .ToList();

            var router = new ScriptedRouter();
            var notifier = new RecordingNotifier();
            var clock = new InstantGameClock();
            var board = new GameBoard();

            var templates = deck ?? DefaultDeck();
            var manager = new GameManager(
                new GameState(players), board, players,
                new Deck(templates), new ItemManager(GameBuilder.MakeItemTemplates()),
                notifier, router, clock);

            return new GameFixture(manager, router, notifier, clock);
        }

        private static List<Card> DefaultDeck() =>
            Enumerable.Range(1, 20)
                .Select(i => (Card)new TestCard(name: $"Пустышка {i}"))
                .ToList();

        // ---- Расстановка ----

        /// <summary>Кладёт карту на стол, назначив ей владельца.</summary>
        public T OnBoard<T>(T card, Player owner = null) where T : Card
        {
            card.Owner = owner ?? Current;
            card.Position = CardPosition.OnGameBoard;
            Board.AddCardOnBoard(card);
            return card;
        }

        /// <summary>Кладёт карту в руку игроку.</summary>
        public T InHand<T>(T card, Player owner = null) where T : Card
        {
            var player = owner ?? Current;
            card.Owner = player;
            card.Position = CardPosition.InHand;
            player.AddCardToHand(card);
            return card;
        }

        /// <summary>Кладёт карты наверх колоды в указанном порядке.</summary>
        public void DeckTop(params Card[] cards)
        {
            // Карты, которых в колоде нет, туда не положить: ShuffleById умеет
            // только переставлять. Поэтому тест, которому нужна конкретная
            // карта сверху, собирает колоду из неё в Start.
            var ids = cards.Select(c => c.Id).ToList();
            var rest = Deck.GetCardIds().Where(id => !ids.Contains(id));
            Deck.ShuffleById(ids.Concat(rest).ToList());
        }

        /// <summary>Выдаёт игроку предмет из запаса.</summary>
        public Item GiveItem(ItemType type, Player owner = null)
        {
            var player = owner ?? Current;
            var item = Items.GetItemByType(type);
            player.AddItemToItemBag(item);
            return item;
        }

        /// <summary>Опустошает запас предметов этого типа.</summary>
        public void DrainSupply(ItemType type)
        {
            while (Items.GetItemByType(type) != null) { }
        }

        /// <summary>Вычерпывает колоду досуха.</summary>
        public void DrainDeck()
        {
            while (Deck.DrawCard() != null) { }
        }

        /// <summary>Передаёт ход следующему игроку.</summary>
        public void NextTurn() => State.NextTurn();
    }
}
