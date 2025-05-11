using Mirror.Examples.BilliardsPredicted;
using ScaryTales.Abstractions;
using ScaryTales.Cards;
using ScaryTales.Items;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ScaryTales
{
    public class GameBuilder
    {
        private readonly IPlayerInput _playerInput1;
        private readonly IPlayerInput _playerInput2;
        private readonly INotifier _notifier;
        private readonly IGameBoard _gameBoard;

        private Player _player1;
        private Player _player2;
        public GameBuilder(IPlayerInput playerInput,
            INotifier notifier,
            IGameBoard gameBoard)
        {
            _playerInput1 = playerInput;
            _playerInput2 = playerInput;
            _notifier = notifier;
            _gameBoard = gameBoard;
            _player1 = new Player("Саша", _playerInput1);
            _player2 = new Player("Вова", _playerInput2);
        }
        public GameBuilder(IPlayerInput playerInput1,
            IPlayerInput playerInput2,
            INotifier notifier,
            IGameBoard gameBoard)
        {
            _playerInput1 = playerInput1;
            _playerInput2 = playerInput2;
            _notifier = notifier;
            _gameBoard = gameBoard;

            _player1 = new Player("Саша", _playerInput1);
            _player2 = new Player("Вова", _playerInput2);

        }
        public GameBuilder(IPlayerInput playerInput1,
            IPlayerInput playerInput2,
            INotifier notifier,
            IGameBoard gameBoard,
            Player player1,
            Player player2)
        {
            _playerInput1 = playerInput1;
            _playerInput2 = playerInput2;
            _notifier = notifier;
            _gameBoard = gameBoard;
            _player1 = player1;
            _player2 = player2;
        }
        public GameBuilder(INotifier notifier,
                IGameBoard gameBoard,
                Player player1,
                Player player2)
        {
            _notifier = notifier;
            _gameBoard = gameBoard;
            _player1 = player1;
            _player2 = player2;
        }
        public GameManager Build()
        {
            var deck = new Deck(MakeCardTemplates());
            var items = new ItemManager(MakeItemTemplates());

            // Список игроков
            var players = new List<Player> { _player1, _player2 };

            // Игровое состояние
            var gameState = new GameState(players);


            // Создаем игровой менеджер
            return new GameManager(gameState, _gameBoard, players, deck, items, _notifier);
        }

        private List<Card> MakeCardTemplates()
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
        private List<Item> MakeItemTemplates()
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
