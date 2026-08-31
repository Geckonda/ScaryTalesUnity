using Assets.Libraries.ScaryTales.Abstractions;
using ScaryTales.Abstractions;
using ScaryTales.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Assets.Libraries.ScaryTales.Rules.Effects
{
    internal class REfA13 : IRuleEffect
    {
        public REfA13(int id)
        {
            this._id = id;
        }
        private int _id;
        public int Id => _id;

        public string Description => "Сбросьте 1 волшебную палку, чтобы взять 1 карту из колоды и 1 золотую монету из запаса, а также получить 1 ПО.";

        public Task<bool> ApplyEffect(IGameContext context)
        {
            var manager = context.GameManager;
            if (!IsEffectAvailable(context))
            {
                manager.PrintMessage($"Нельзя использовать правило 3. Условия не выполнены.");
                return Task.FromResult(false);
            }

            var player = context.GameState.GetCurrentPlayer();
            var board = context.GameBoard;
            var itemManager = context.ItemManager;
            var deck = context.Deck;

            manager.RemoveItemFromPlayerItemBag(ItemType.MagicStick, player);

            manager.DrawCard(player);

            var coin = itemManager.GetItemByType(ItemType.Coin);

            if (coin == null)
                manager.PrintMessage($"Не осталось золотых монет.");
            else
                manager.PutItemInPlayerItemBag(coin, player);

            manager.AddPointsToPlayer(player, 1);
            return Task.FromResult(true);
        }

        public bool IsEffectAvailable(IGameContext context)
        {
            var player = context.GameState.GetCurrentPlayer();

            return player.HasItem(ItemType.MagicStick);
        }
    }
}
