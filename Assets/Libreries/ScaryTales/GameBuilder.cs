using ScaryTales.Abstractions;
using ScaryTales.Cards;
using ScaryTales.Decisions;
using ScaryTales.Items;
using System.Collections.Generic;
using System.Linq;

namespace ScaryTales
{
    public class GameBuilder
    {
        private readonly INotifier _notifier;
        private readonly IGameBoard _gameBoard;
        private readonly List<Player> _players;

        public GameBuilder(INotifier notifier, IGameBoard gameBoard, IEnumerable<Player> players)
        {
            _notifier = notifier;
            _gameBoard = gameBoard;
            _players = players.ToList();
        }

        public GameManager Build(IDecisionRouter router)
        {
            var deck = new Deck(MakeCardTemplates());
            var items = new ItemManager(MakeItemTemplates());
            var players = new List<Player>(_players);
            var gameState = new GameState(players);
            return new GameManager(gameState, _gameBoard, players, deck, items, _notifier, router);
        }

        // Static so client-side ClientGameView can build the same Card
        // catalog the server's Deck uses (so card IDs match across all peers).
        public static List<Card> MakeCardTemplates()
        {
            return new List<Card>()
            {
                new NightChildCard(),
                new OldMasterCard(),
                new DarkLordCard(),
                new DragonCard(),
                //new EnchantedForestCard(),
                new PrincessCard(),
                new MerchantCard(),
                new WizardCard(),
                new NightCard(),
                new DayCard(),
                new OgreCard(),
                new WisdomKingCard(),
                new FollyKingCard(),
                new FairyCard(),
                new YoungHeroCard(),
                new HiddenCaveCard(),
                new CursedCastleCard(),
                new CharmCard(),
            };
        }
        public static List<Item> MakeItemTemplates()
        {
            return new List<Item>()
            {
                new Coin(),
                new Armor(),
                new Sword(),
                new MagicStick()
            };
        }
    }
}
