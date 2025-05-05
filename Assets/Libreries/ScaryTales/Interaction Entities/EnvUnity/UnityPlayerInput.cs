using Assets.Scripts.Network;
using ScaryTales.Abstractions;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace ScaryTales.Interaction_Entities.EnvUnity
{
    public class UnityPlayerInput : MonoBehaviour, IPlayerInput
    {
        private Card _selectedCard;
        private bool _isCardSelected;
        private List<CardView> _cardViewsToSelect = new();

        public async Task<Card> SelectCard(List<Card> cards)
        {
            _selectedCard = null;
            _isCardSelected = false;

            // Очищаем список карт для выбора
            _cardViewsToSelect.Clear();

            // Подписываемся на события кликов по картам
            foreach (var card in cards)
            {
                var cardView = CardViewService.Instance.GetCardView(card);
                if (cardView != null)
                {
                    Debug.Log($"Карта {card.Name} доступна для выбора");
                    cardView.OnCardClicked += OnCardClicked;
                    _cardViewsToSelect.Add(cardView);
                    cardView.SetHighlight(true);
                }
            }

            Debug.Log("Запускаем ожидание выбора карты...");

            // Ожидаем, пока игрок не выберет карту
            while (!_isCardSelected)
            {
                await Task.Yield(); // Ожидание следующего кадра
            }

            // Отписываемся от событий кликов
            foreach (var cardView in _cardViewsToSelect)
            {
                cardView.OnCardClicked -= OnCardClicked;
                cardView.SetHighlight(false);
            }

            Debug.Log("Карта выбрана: " + _selectedCard.Name);
            return _selectedCard;
        }

        private void OnCardClicked(Card card)
        {
            _selectedCard = card;
            _isCardSelected = true;
        }

        private Item _selectedItem;
        private bool _isItemSelected;

        /// <summary>
        /// Асинхронный метод для выбора предмета из списка.
        /// </summary>
        /// <param name="items">Список предметов, из которых нужно выбрать.</param>
        /// <returns>Выбранный предмет.</returns>
        public async Task<Item> SelectItem(List<Item> items)
        {
            // Список для хранения представлений предметов, доступных для выбора
            List<ItemView> _itemViewsToSelect = new();

            // Сбрасываем выбранный предмет и флаг выбора
            _selectedItem = null;
            _isItemSelected = false;

            // Получаем контейнер для отображения предметов
            var itemContainer = ItemContainer.Instance.contentPanel;

            // Создаем представления для каждого предмета и добавляем их в контейнер
            foreach (var item in items)
            {
                var itemView = ItemViewService.Instance.CreateItemView(item, itemContainer);
                if (itemView != null)
                {
                    Debug.Log($"Предмет '{item.Name}' доступен для выбора");
                    // Подписываемся на событие клика по предмету
                    itemView.OnItemClicked += OnItemClicked;
                    _itemViewsToSelect.Add(itemView);
                }
            }

            // Отображаем предметы в контейнере
            ItemContainer.Instance.Show(_itemViewsToSelect);

            // Ожидаем, пока пользователь выберет предмет
            while (!_isItemSelected)
            {
                await Task.Yield(); // Освобождаем поток, чтобы не блокировать выполнение
            }

            GameNetworkController.Instance.CmdSelectItem((int)_selectedItem.Type);

            // Прячем контейнер после выбора предмета
            ItemContainer.Instance.Hide();

            // Отписываемся от событий кликов по предметам
            foreach (var itemView in _itemViewsToSelect)
            {
                itemView.OnItemClicked -= OnItemClicked;
                itemView.SetHighlight(false); // Снимаем выделение с предметов
            }

            // Очищаем контейнер от представлений предметов
            ItemContainer.Instance.ClearContentPanelchildren();

            // Возвращаем выбранный предмет
            return _selectedItem;
        }

        private void OnItemClicked(Item item)
        {
            _selectedItem = item;
            _isItemSelected = true;
        }

        public Task<bool> YesOrNo()
        {
            throw new System.NotImplementedException();
        }
    }
}