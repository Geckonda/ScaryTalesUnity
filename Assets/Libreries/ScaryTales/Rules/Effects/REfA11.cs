using Assets.Libreries.ScaryTales.Abstractions;
using ScaryTales.Abstractions;
using ScaryTales.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Assets.Libreries.ScaryTales.Rules.Effects
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

        public async Task<bool> ApplyEffect(IGameContext context)
        {
            if (!IsEffectAvailable(context))
                return false;
            return true;
        }

        public bool IsEffectAvailable(IGameContext context)
        {
            var player = context.GameState.GetCurrentPlayer();
            var n = context.GameBoard.DiscardPileCount();
            return player.HasItem(ItemType.Armor) && n > 0;
        }
    }
}
