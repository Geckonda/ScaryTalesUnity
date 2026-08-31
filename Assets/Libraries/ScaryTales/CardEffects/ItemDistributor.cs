using ScaryTales.Abstractions;
using ScaryTales.Decisions;
using ScaryTales.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ScaryTales.CardEffects
{
    public class ItemDistributor : ICardEffect
    {
        private readonly List<ItemType> _itemTypes;
        public CardEffectTimeType Type => CardEffectTimeType.Instant;
        public ItemDistributor(List<ItemType> types)
        {
            _itemTypes = types;
        }

        public async Task ApplyEffect(IGameContext context)
        {
            var manager = context.GameManager;
            var itemManager = context.ItemManager;
            var player = context.GameState.GetCurrentPlayer();

            // Какие из требуемых типов сейчас в наличии
            var availableTypes = _itemTypes
                .Where(t => itemManager.CountItemByType(t) > 0)
                .ToList();

            if (availableTypes.Count == 0)
            {
                manager.PrintMessage("Нет доступных предметов в запасе.");
                return;
            }

            var unavailableTypes = _itemTypes
                .Where(t => !availableTypes.Contains(t))
                .ToList();
            if (unavailableTypes.Count > 0)
                PrintInavailableItems(unavailableTypes, manager.PrintMessage);

            var pick = await context.Router.PickItem(
                player.Id,
                new PickItemRequest(availableTypes));

            // Достаём оригинальный предмет (не клон) и кладём игроку
            var originalItem = itemManager.GetItemByType(pick.ItemType);
            if (originalItem != null)
            {
                manager.PrintMessage($"Игрок {player.Name} выбрал предмет \"{originalItem.Name}\"");
                manager.PutItemInPlayerItemBag(originalItem, player);
            }
        }
        private void PrintInavailableItems(List<ItemType> items, Action<string> print)
        {
            foreach (var item in items)
            {
                switch (item)
                {
                    case ItemType.Coin:
                        print("В запасе не осталось золотых монет.");
                        break;
                    case ItemType.Sword:
                        print("В запасе не осталось мечей.");
                        break;
                    case ItemType.Armor:
                        print("В запасе не осталось доспехов.");
                        break;
                    case ItemType.MagicStick:
                        print("В запасе не осталось волшебных палок.");
                        break;
                }
            }
        }
    }
}
