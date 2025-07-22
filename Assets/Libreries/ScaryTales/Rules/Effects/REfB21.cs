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
    public class REfB21 : IRuleEffect
    {
        public REfB21(int id)
        {
            this._id = id;
        }
        private int _id;
        public int Id => _id;

        public string Description => "Получите 12 ПО за каждый ваш набор из 3 золотых монет и 1 волшебной палки.";

        public Task<bool> ApplyEffect(IGameContext context)
        {
            var manager = context.GameManager;
            if (!IsEffectAvailable(context))
            {
                manager.PrintMessage($"Нельзя использовать правило 2. Условия не выполнены.");
                return Task.FromResult(false);
            }

            var player = context.GameState.GetCurrentPlayer();

            var magicSticksCount = player.ItemAmount(ItemType.MagicStick);
            var coinsCount = player.ItemAmount(ItemType.Coin) / 3;
            var points = (Math.Min(coinsCount, magicSticksCount)) * 12;

            manager.AddPointsToPlayer(player, points);
            manager.PrintMessage($"Игрок {player.Name} получает {points}ПО за правило в конце игры");
            return Task.FromResult(true);
        }

        public bool IsEffectAvailable(IGameContext context)
        {
            var player = context.GameState.GetCurrentPlayer();

            return player.HasItem(ItemType.MagicStick)
                && (player.ItemAmount(ItemType.Coin) >= 3);
        }
    }
}
