using Assets.Libreries.ScaryTales.Abstractions;
using ScaryTales;
using ScaryTales.Abstractions;
using ScaryTales.Decisions;
using ScaryTales.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Assets.Libreries.ScaryTales.Rules.Effects
{
    public class REfA12 : IRuleEffect
    {
        public REfA12(int id)
        {
            this._id = id;
        }
        private int _id;
        public string Description => "Сбросьте 1 меч и 1 любого разыгранного злодея, чтобы получить 3 ПО.";

        public int Id => _id;

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

            manager.RemoveItemFromPlayerItemBag(ItemType.Sword, player);

            var pick = await context.Router.PickCard(
                player.Id,
                new PickCardRequest(monsters.Select(c => c.Id)));
            var monster = monsters.First(c => c.Id == pick.CardId);
            manager.PrintMessage($"Игрок {player.Name} сбросил карту {monster.Name}");

            board.RemoveCardFromBoard(monster);
            manager.PutCardToDiscardPile(monster);
            manager.AddPointsToPlayer(player, 3);
            return true;
        }

        public bool IsEffectAvailable(IGameContext context)
        {
            var player = context.GameState.GetCurrentPlayer();
            var n = context.GameBoard.GetCardsOnBoard(CardType.Monster).Count;
            
            return player.HasItem(ItemType.Sword) && n > 0;
        }
    }
}
