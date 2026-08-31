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
        /// <summary>
        /// Отпечаток каталога карт: имена и количества в порядке шаблонов.
        ///
        /// <para><b>Зачем.</b> Id карты назначается позиционно — <see cref="Deck"/>
        /// и клиентский каталог идут по этому же списку и раздают номера
        /// подряд. Значит список шаблонов есть общий контракт сервера и
        /// клиента, и любое его изменение (добавили карту, поменяли местами,
        /// изменили количество) сдвигает номера всех последующих карт.</para>
        ///
        /// <para>Если стороны разойдутся, ничего не сломается заметно: сервер
        /// пришлёт «карта №45 в слот времени суток», клиент найдёт у себя
        /// под №45 другую карту и честно её нарисует. Игра начнёт показывать
        /// не то, что происходит, и выглядеть это будет как мистика — что
        /// однажды и стоило владельцу вечера. Поэтому отпечаток едет в
        /// GameStartedEvent, и клиент сверяет его со своим.</para>
        ///
        /// <para>Считается по именам, а не по хеш-коду типов: имя стабильно
        /// между сборками и читаемо в логе.</para>
        /// </summary>
        public static int CardCatalogVersion()
        {
            unchecked
            {
                int hash = 17;
                foreach (var template in MakeCardTemplates())
                {
                    foreach (char c in template.Name ?? string.Empty)
                        hash = hash * 31 + c;
                    hash = hash * 31 + template.CardCountInDeck;
                }
                return hash;
            }
        }

        public static List<Card> MakeCardTemplates()
        {
            return new List<Card>()
            {
                new NightChildCard(),
                new OldMasterCard(),
                new DarkLordCard(),
                new DragonCard(),
                new EnchantedForestCard(),
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
