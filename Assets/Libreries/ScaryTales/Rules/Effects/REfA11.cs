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
        public string Description => "Сбросьте 1 доспех, чтобы взять верхнюю карту стопки сброса.";

        public async Task ApplyEffect(IGameContext context)
        {
            if (!IsEffectAvailable(context))
                return;
        }

        public bool IsEffectAvailable(IGameContext context)
        {
            var player = context.GameState.GetCurrentPlayer();
            var n = context.GameBoard.DiscardPileCount();
            return player.HasItem(ItemType.Armor) && n > 0;
        }
    }
}
