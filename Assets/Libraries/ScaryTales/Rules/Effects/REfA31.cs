using Assets.Libraries.ScaryTales.Abstractions;
using ScaryTales.Abstractions;
using ScaryTales.Enums;
using System.Threading.Tasks;

namespace Assets.Libraries.ScaryTales.Rules.Effects
{
    public class REfA31 : IRuleEffect
    {
        public REfA31(int id)
        {
            this._id = id;
        }
        private int _id;

        public int Id => _id;
        public string Description => "Сбросьте 1 доспех и 1 меч, чтобы взять 2 карты из колоды и получить 3 ПО.";

        public Task<bool> ApplyEffect(IGameContext context)
        {
            var manager = context.GameManager;
            if (!IsEffectAvailable(context))
            {
                manager.PrintMessage($"Нельзя использовать правило 1. Условия не выполнены.");
                return Task.FromResult(false);
            }

            var player = context.GameState.GetCurrentPlayer();

            manager.RemoveItemFromPlayerItemBag(ItemType.Armor, player);
            manager.RemoveItemFromPlayerItemBag(ItemType.Sword, player);

            // Пустая колода не отменяет правило: DrawCard на ней просто ничего
            // не делает, а ПО начисляются всё равно — иначе доспех и меч ушли
            // бы вообще ни за что. Так же ведёт себя REfA13.
            manager.DrawCard(player);
            manager.DrawCard(player);

            manager.AddPointsToPlayer(player, 3);
            return Task.FromResult(true);
        }

        public bool IsEffectAvailable(IGameContext context)
        {
            var player = context.GameState.GetCurrentPlayer();

            return player.HasItem(ItemType.Armor) && player.HasItem(ItemType.Sword);
        }
    }
}
