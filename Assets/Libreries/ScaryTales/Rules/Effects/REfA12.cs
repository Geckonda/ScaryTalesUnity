using Assets.Libreries.ScaryTales.Abstractions;
using ScaryTales;
using ScaryTales.Abstractions;
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
        public string Description => "Сбросьте 1 меч и 1 любого разыгранного злодея, чтобы получить 3 ПО.";

        public async Task ApplyEffect(IGameContext context)
        {
            var manager = context.GameManager;
            if (!IsEffectAvailable(context))
            {
                manager.PrintMessage($"Нельзя использовать правило 2. Условия не выполнены.");
                return;
            }

            var player = context.GameState.GetCurrentPlayer();
            var monsters = context.GameBoard.GetCardsOnBoard(CardType.Monster);
            var board = context.GameBoard;

            player.RemoveItemFromItemBag(ItemType.Sword);

            var monster = await player.SelectCardAmongOthers(monsters);
            manager.PrintMessage($"Игрок {player.Name} сбросил карту {monster.Name}");

            board.RemoveCardFromBoard(monster);
            manager.PutCardToDiscardPile(monster);
            manager.AddPointsToPlayer(player, 3);
        }

        public bool IsEffectAvailable(IGameContext context)
        {
            var player = context.GameState.GetCurrentPlayer();
            var n = context.GameBoard.GetCardsOnBoard(CardType.Monster).Count;
            
            return player.HasItem(ItemType.Sword) && n > 0;
        }
    }
}
