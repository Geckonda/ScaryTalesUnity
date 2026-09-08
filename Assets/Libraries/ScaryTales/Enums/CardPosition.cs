using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ScaryTales.Enums
{
    /// <summary>
    /// Позиция карты
    /// </summary>
    public enum CardPosition
    {
        /// <summary>
        /// В колоде (не разыгранная)
        /// </summary>
        InDeck,
        /// <summary>
        /// В руке игрока
        /// </summary>
        InHand,
        /// <summary>
        /// На игровом поле перед игроком
        /// </summary>
        BeforePlayer,
        /// <summary>
        /// На игровом поле общем
        /// </summary>
        OnGameBoard,
        /// <summary>
        /// Сброшенная карта
        /// </summary>
        Discarded,
        /// <summary>
        /// Время суток
        /// </summary>
        TimeOfDay,
        /// <summary>
        /// Удалена из игры: ни в колоде, ни в руке, ни на столе, ни в сбросе.
        ///
        /// <para>Вернуть такую карту в партию нельзя ничем — этим она и
        /// отличается от сброса, откуда карты возвращаются (Дракон, правила).
        /// Сегодня так уходит вторая из двух карт, подсмотренных Тайной
        /// помощницей.</para>
        ///
        /// <para>Своей зоны у неё нет и не нужно: карта просто никуда не
        /// кладётся, а значение существует ради читаемости логов и ради того,
        /// чтобы <c>MoveCardToItsPosition</c> не пришлось учить новому
        /// случаю — ни одна карта не уходит сюда после разыгрывания.</para>
        /// </summary>
        RemovedFromGame
    }
}
