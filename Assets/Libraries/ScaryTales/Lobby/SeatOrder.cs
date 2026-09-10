using System.Collections.Generic;
using System.Linq;

namespace ScaryTales.Lobby
{
    /// <summary>
    /// Порядок ходов, каким его выбрали в лобби.
    /// </summary>
    public static class SeatOrder
    {
        /// <summary>
        /// Расставляет игроков по выбранным стульям.
        ///
        /// <para>Кто стул не выбрал — уезжает в хвост, сохраняя тот порядок, в
        /// котором сидел. Сортировка обязана быть устойчивой (<c>OrderBy</c>, а
        /// не <c>List.Sort</c>): у всех невыбравших ключ одинаковый, и
        /// неустойчивая сортировка перемешала бы их между собой без всякой
        /// причины — за столом это выглядит как случайно поменявшийся порядок
        /// ходов.</para>
        /// </summary>
        /// <param name="players">Игроки в текущем порядке.</param>
        /// <param name="chairBySeat">Место игрока → выбранный им стул.</param>
        /// <returns>Новый список; исходный не меняется.</returns>
        public static List<Player> Arrange(
            IReadOnlyList<Player> players,
            IReadOnlyDictionary<int, int> chairBySeat)
        {
            if (players == null) return new List<Player>();
            if (chairBySeat == null || chairBySeat.Count == 0) return players.ToList();

            return players
                .OrderBy(p => chairBySeat.TryGetValue(p.Id, out int chair) ? chair : int.MaxValue)
                .ToList();
        }
    }
}
