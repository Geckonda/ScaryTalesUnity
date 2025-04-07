using ScaryTales.Abstractions;
using ScaryTales;
using UnityEngine;

namespace Assets.Scripts.Factories
{
    public class ItemViewFactory
    {
        private readonly IGameManager _gameManager;
        private readonly Transform _gameBoardPanel;
        private readonly GameObject _itemPrefab;

        public ItemViewFactory(IGameManager gameManager, Transform gameBoardPanel, GameObject itemPrefab)
        {
            _gameManager = gameManager;
            _gameBoardPanel = gameBoardPanel;
            _itemPrefab = itemPrefab;
        }

        public ItemView CreateItemView(Item item, Transform parent)
        {
            if (_itemPrefab == null) return null;

            GameObject itemInstance = GameObject.Instantiate(_itemPrefab, parent);
            ItemView itemView = itemInstance.GetComponent<ItemView>();

            if (itemView == null)
            {
                Debug.LogError("Компонент ItemView не найден!");
                return null;
            }

            itemView.Initialize(item);
            return itemView;
        }
    }
}
