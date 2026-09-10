using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ScaryTales;
using ScaryTales.Lobby;

namespace ScaryTales.Tests.Lobby
{
    [TestFixture]
    public class SeatOrderTests
    {
        private static List<Player> Table(params int[] seatIds) =>
            seatIds.Select(id => new Player(id, $"И{id}")).ToList();

        private static int[] IdsOf(IEnumerable<Player> players) => players.Select(p => p.Id).ToArray();

        [Test]
        public void Игроки_расставляются_по_выбранным_стульям()
        {
            var players = Table(1, 2, 3);
            var chairs = new Dictionary<int, int> { [1] = 3, [2] = 1, [3] = 2 };

            Assert.That(IdsOf(SeatOrder.Arrange(players, chairs)), Is.EqualTo(new[] { 2, 3, 1 }));
        }

        [Test]
        public void Не_выбравшие_стул_уезжают_в_хвост()
        {
            var players = Table(1, 2, 3);
            var chairs = new Dictionary<int, int> { [3] = 1 };

            Assert.That(IdsOf(SeatOrder.Arrange(players, chairs)), Is.EqualTo(new[] { 3, 1, 2 }));
        }

        [Test]
        public void Не_выбравшие_сохраняют_свой_порядок()
        {
            // Ключ у них одинаковый, и неустойчивая сортировка перемешала бы их
            // между собой без причины — за столом это выглядит как случайно
            // поменявшийся порядок ходов.
            var players = Table(5, 4, 3, 2);
            var chairs = new Dictionary<int, int> { [3] = 1 };

            Assert.That(IdsOf(SeatOrder.Arrange(players, chairs)), Is.EqualTo(new[] { 3, 5, 4, 2 }));
        }

        [Test]
        public void Пустая_карта_стульев_оставляет_порядок_как_есть()
        {
            var players = Table(3, 1, 2);

            Assert.That(IdsOf(SeatOrder.Arrange(players, new Dictionary<int, int>())),
                Is.EqualTo(new[] { 3, 1, 2 }));
        }

        [Test]
        public void Null_вместо_карты_стульев_оставляет_порядок_как_есть()
        {
            var players = Table(3, 1, 2);

            Assert.That(IdsOf(SeatOrder.Arrange(players, null)), Is.EqualTo(new[] { 3, 1, 2 }));
        }

        [Test]
        public void Null_вместо_игроков_даёт_пустой_список()
        {
            Assert.That(SeatOrder.Arrange(null, new Dictionary<int, int>()), Is.Empty);
        }

        [Test]
        public void Исходный_список_не_меняется()
        {
            // Комната потом сама перезаписывает свой список; порча входа
            // сделала бы порядок зависящим от того, сколько раз позвали.
            var players = Table(1, 2, 3);
            var chairs = new Dictionary<int, int> { [3] = 1 };

            SeatOrder.Arrange(players, chairs);

            Assert.That(IdsOf(players), Is.EqualTo(new[] { 1, 2, 3 }));
        }

        [Test]
        public void Стул_за_несуществующим_местом_никому_не_мешает()
        {
            var players = Table(1, 2);
            var chairs = new Dictionary<int, int> { [99] = 1, [2] = 2 };

            Assert.That(IdsOf(SeatOrder.Arrange(players, chairs)), Is.EqualTo(new[] { 2, 1 }));
        }
    }
}
