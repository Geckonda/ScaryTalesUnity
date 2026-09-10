using Assets.Libraries.ScaryTales.Abstractions;
using ScaryTales.Abstractions;
using ScaryTales.Decisions;
using ScaryTales.Enums;
using System.Linq;
using System.Threading.Tasks;

namespace Assets.Libraries.ScaryTales.Rules.Effects
{
    public class REfA32 : IRuleEffect
    {
        public REfA32(int id)
        {
            this._id = id;
        }
        private int _id;

        public int Id => _id;
        public string Description => "Сбросьте 1 волшебную палку и 1 любого разыгранного злодея, чтобы взять из запаса 1 золотую монету и получить 1 ПО.";

        public async Task<bool> ApplyEffect(IGameContext context)
        {
            var manager = context.GameManager;
            if (!IsEffectAvailable(context))
            {
                manager.PrintMessage($"Нельзя использовать правило 2. Условия не выполнены.");
                return false;
            }

            var player = context.GameState.GetCurrentPlayer();
            var monsters = context.GameBoard.GetCardsOnBoard(CardType.Monster);
            var board = context.GameBoard;
            var itemManager = context.ItemManager;

            // Сначала спрашиваем, потом забираем палку. Отказ от выбора или
            // выход игрока посреди вопроса прилетит сюда исключением, и до
            // списания предмета выполнение не дойдёт — иначе палка терялась бы
            // впустую, а злодей оставался на столе.
            var pick = await context.Router.PickCard(
                player.Id,
                new PickCardRequest(monsters.Select(c => c.Id), canCancel: true));

            var monster = monsters.FirstOrDefault(c => c.Id == pick.CardId);
            if (monster == null)
            {
                manager.PrintMessage("Испытание рыцарей: выбранный злодей не найден на столе.");
                return false;
            }

            manager.RemoveItemFromPlayerItemBag(ItemType.MagicStick, player);
            manager.PrintMessage($"Игрок {player.Name} сбросил карту {monster.Name}");

            board.RemoveCardFromBoard(monster);
            manager.PutCardToDiscardPile(monster);

            // Запас монет конечен. Кончился — злодей всё равно повержен и ПО
            // начисляется: иначе палка и карта уходили бы ни за что.
            var coin = itemManager.GetItemByType(ItemType.Coin);
            if (coin == null)
                manager.PrintMessage($"Не осталось золотых монет.");
            else
                manager.PutItemInPlayerItemBag(coin, player);

            manager.AddPointsToPlayer(player, 1);
            return true;
        }

        public bool IsEffectAvailable(IGameContext context)
        {
            var player = context.GameState.GetCurrentPlayer();
            var n = context.GameBoard.GetCardsOnBoard(CardType.Monster).Count;

            return player.HasItem(ItemType.MagicStick) && n > 0;
        }
    }
}
