using ScaryTales;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Assets.Scripts.Network
{
    /// <summary>
    /// Интерфейс, описывающий взаимодействие с игроком через сеть.
    /// Используется для отправки данных и получения действий от игрока.
    /// </summary>
    public interface INetworkController
    {
        /// <summary>
        /// Отправляет игроку список доступных карт для выбора.
        /// </summary>
        /// <param name="playerId">ID игрока</param>
        /// <param name="cards">Список доступных карт</param>
        Task SendAvailableCards(int playerId, List<Card> cards);

        /// <summary>
        /// Ожидает выбор карты от игрока.
        /// </summary>
        /// <param name="playerId">ID игрока</param>
        /// <returns>ID выбранной карты</returns>
        Task<int> WaitForCardSelection(int playerId);

        /// <summary>
        /// Отправляет игроку список доступных предметов для выбора.
        /// </summary>
        /// <param name="playerId">ID игрока</param>
        /// <param name="items">Список доступных предметов</param>
        Task SendAvailableItems(int playerId, List<Item> items);

        /// <summary>
        /// Ожидает выбор предмета от игрока.
        /// </summary>
        /// <param name="playerId">ID игрока</param>
        /// <returns>ID выбранного предмета</returns>
        Task<int> WaitForItemSelection(int playerId);

        /// <summary>
        /// Ожидает от игрока ответ "Да" или "Нет" (подтверждение действия).
        /// </summary>
        /// <param name="playerId">ID игрока</param>
        /// <returns>True, если игрок выбрал "Да"; иначе — false</returns>
        Task<bool> WaitForYesOrNo(int playerId);
    }
}
