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
    public class REfA11 : IRuleEffect
    {
        public REfA11(int id)
        {
            this._id = id;
        }
        private int _id;

        public int Id => _id;
        public string Description => "Сбросьте 1 доспех, чтобы взять верхнюю карту стопки сброса.";

        public Task<bool> ApplyEffect(IGameContext context)
        {
            var manager = context.GameManager;
            if (!IsEffectAvailable(context))
            {
                manager.PrintMessage($"Нельзя использовать правило 1. Условия не выполнены.");
                return Task.FromResult(false);
            }

            var player = context.GameState.GetCurrentPlayer();
            var board = context.GameBoard;

            manager.RemoveItemFromPlayerItemBag(ItemType.Armor, player);

            var card = board.GetTopCardFromDiscardPile();
            manager.PutCardInPlayerHandFromDiscardPile(card, player);

            return Task.FromResult(true);
        }

        public bool IsEffectAvailable(IGameContext context)
        {
            var player = context.GameState.GetCurrentPlayer();
            var n = context.GameBoard.DiscardPileCount();
            return player.HasItem(ItemType.Armor) && n > 0;
        }
    }
}
