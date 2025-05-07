using Mirror;
using ScaryTales;
using ScaryTales.Abstractions;
using ScaryTales.Enums;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Assets.Scripts.Network
{
    /// <summary>
    /// Реализация ввода игрока через сеть.
    /// Делегирует выбор карты, предмета и подтверждение действия через сетевой контроллер.
    /// </summary>
    public class NetworkPlayerInput : NetworkBehaviour, IPlayerInput
    {
        private int _playerId;
        private INetworkController _network;

        /// <summary>
        /// Конструктор сетевого ввода игрока.
        /// </summary>
        /// <param name="playerId">Уникальный ID игрока</param>
        /// <param name="network">Сетевой контроллер, обрабатывающий взаимодействие с клиентом</param>
        public void Initialize(int playerId, INetworkController network)
        {
            _playerId = playerId;
            _network = network;
        }

        /// <summary>
        /// Запрашивает у игрока выбор карты из списка.
        /// </summary>
        /// <param name="cards">Доступные карты</param>
        /// <returns>Выбранная игроком карта</returns>
        //public async Task<Card> SelectCard(List<Card> cards)
        //{
        //    await _network.SendAvailableCards(_playerId, cards);
        //    var selectedId = await _network.WaitForCardSelection(_playerId);
        //    return cards.FirstOrDefault(c => c.Id == selectedId);
        //}

        /// <summary>
        /// Запрашивает у игрока выбор предмета из списка.
        /// </summary>
        /// <param name="items">Доступные предметы</param>
        /// <returns>Выбранный игроком предмет</returns>
        //public async Task<Item> SelectItem(List<Item> items)
        //{
        //    await _network.SendAvailableItems(_playerId, items);
        //    var selectedId = await _network.WaitForItemSelection(_playerId);
        //    return items.FirstOrDefault(i => i.Id == selectedId);
        //}

        /// <summary>
        /// Запрашивает у игрока подтверждение выполнения действия.
        /// </summary>
        /// <param name="description">Описание действия (может быть выведено игроку)</param>
        /// <returns>True, если игрок подтвердил действие; иначе — false</returns>
        public async Task<bool> ConfirmAction(string description)
        {
            return await _network.WaitForYesOrNo(_playerId);
        }

        public Task<bool> YesOrNo()
        {
            throw new System.NotImplementedException();
        }

        private TaskCompletionSource<Item> _itemSelectionTcs;
        private List<Item> _availableItems;
        public async Task<Item> SelectItem(List<Item> availableItems)
        {
            _availableItems = availableItems;
            // Ждём выбора от клиента, который реально играет
            _itemSelectionTcs = new TaskCompletionSource<Item>();

            // Ждать, пока этот TCS не будет завершён
            return await _itemSelectionTcs.Task;
        }

        // Этот метод вызывается из RPC, когда сервер узнаёт, что выбрал другой игрок
        public void OnItemSelectedFromRemote(int itemType)
        {
            var item = UnGameManager.Instance._context.ItemManager.GetCloneItemByType((ItemType)itemType);
            if (item != null && _itemSelectionTcs != null && !_itemSelectionTcs.Task.IsCompleted)
            {
                _itemSelectionTcs.SetResult(item);
            }
        }

        private TaskCompletionSource<Card> _cardSelectionTcs;
        private List<Card> _availableCards;
        public async Task<Card> SelectCard(List<Card> availableCards)
        {
            _availableCards = availableCards;
            // Ждём выбора от клиента, который реально играет
            _cardSelectionTcs = new TaskCompletionSource<Card>();

            // Ждать, пока этот TCS не будет завершён
            return await _cardSelectionTcs.Task;
        }

        // Этот метод вызывается из RPC, когда сервер узнаёт, что выбрал другой игрок
        public void OnCardSelectedFromRemote(int cardId)
        {
            var card = UnGameManager.Instance._context.GameBoard.GetCardFromBoard(cardId);
            if (card != null && _cardSelectionTcs != null && !_cardSelectionTcs.Task.IsCompleted)
            {
                _cardSelectionTcs.SetResult(card);
            }
        }
    }
}
