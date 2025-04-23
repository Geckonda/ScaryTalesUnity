using Mirror;
using ScaryTales;
using ScaryTales.Abstractions;
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
        public async Task<Card> SelectCard(List<Card> cards)
        {
            await _network.SendAvailableCards(_playerId, cards);
            var selectedId = await _network.WaitForCardSelection(_playerId);
            return cards.FirstOrDefault(c => c.Id == selectedId);
        }

        /// <summary>
        /// Запрашивает у игрока выбор предмета из списка.
        /// </summary>
        /// <param name="items">Доступные предметы</param>
        /// <returns>Выбранный игроком предмет</returns>
        public async Task<Item> SelectItem(List<Item> items)
        {
            await _network.SendAvailableItems(_playerId, items);
            var selectedId = await _network.WaitForItemSelection(_playerId);
            return items.FirstOrDefault(i => i.Id == selectedId);
        }

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
    }
}
