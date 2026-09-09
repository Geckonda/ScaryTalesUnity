using Assets.Libraries.ScaryTales.Abstractions;
using ScaryTales.Abstractions;
using ScaryTales.Enums;
using System.Threading.Tasks;

namespace Assets.Libraries.ScaryTales.Rules.Effects
{
    public class REfA21 : IRuleEffect
    {
        public REfA21(int id)
        {
            this._id = id;
        }
        private int _id;

        public int Id => _id;
        public string Description => "Сбросьте 1 волшебную палку, чтобы разыграть 1 дополнительную карту в этот же ход.";

        public Task<bool> ApplyEffect(IGameContext context)
        {
            var manager = context.GameManager;
            if (!IsEffectAvailable(context))
            {
                manager.PrintMessage($"Нельзя использовать правило 1. Условия не выполнены.");
                return Task.FromResult(false);
            }

            var player = context.GameState.GetCurrentPlayer();

            manager.RemoveItemFromPlayerItemBag(ItemType.MagicStick, player);
            manager.GrantExtraPlay();

            return Task.FromResult(true);
        }

        public bool IsEffectAvailable(IGameContext context)
        {
            var player = context.GameState.GetCurrentPlayer();

            // Пустая рука закрывает эффект так же, как пустой сброс закрывает
            // REfA11: разыграть дополнительную карту нечем, а палка ушла бы
            // безвозвратно. Игровой цикл сам обрывает предложение при пустой
            // руке, но там это происходит уже после списания предмета.
            return player.HasItem(ItemType.MagicStick) && player.HandCardCount > 0;
        }
    }
}
