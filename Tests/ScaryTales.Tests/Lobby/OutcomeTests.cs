using System.Collections.Generic;
using NUnit.Framework;
using ScaryTales;

namespace ScaryTales.Tests.Lobby
{
    [TestFixture]
    public class OutcomeTests
    {
        private static Player WithScore(int id, int score)
        {
            var player = new Player(id, $"И{id}");
            player.AddPoints(score);
            return player;
        }

        [Test]
        public void Побеждает_тот_у_кого_больше_очков()
        {
            var winners = Outcome.ComputeWinners(new List<Player>
            {
                WithScore(1, 5),
                WithScore(2, 9),
                WithScore(3, 7),
            });

            Assert.That(winners, Is.EqualTo(new[] { 2 }));
        }

        [Test]
        public void При_равном_счёте_побеждают_все_кто_его_набрал()
        {
            // Раньше здесь стоял OrderByDescending().First(), и победа тихо
            // доставалась игроку с меньшим номером места: сортировка
            // устойчивая, так что при равенстве он оставался первым. На экране
            // это выглядело как случайный выбор — и было единственным местом,
            // где правила игры расходились с кодом.
            var winners = Outcome.ComputeWinners(new List<Player>
            {
                WithScore(1, 9),
                WithScore(2, 4),
                WithScore(3, 9),
            });

            Assert.That(winners, Is.EqualTo(new[] { 1, 3 }));
        }

        [Test]
        public void Ничья_всех_за_столом()
        {
            var winners = Outcome.ComputeWinners(new List<Player>
            {
                WithScore(1, 6),
                WithScore(2, 6),
                WithScore(3, 6),
                WithScore(4, 6),
            });

            Assert.That(winners, Is.EqualTo(new[] { 1, 2, 3, 4 }));
        }

        [Test]
        public void Все_с_нулём_это_ничья_а_не_отсутствие_победителя()
        {
            // Партию можно доиграть, не набрав очков вовсе. Это ничья, а не
            // «никто не выиграл».
            var winners = Outcome.ComputeWinners(new List<Player>
            {
                WithScore(1, 0),
                WithScore(2, 0),
            });

            Assert.That(winners, Is.EqualTo(new[] { 1, 2 }));
        }

        [Test]
        public void Один_игрок_побеждает_сам_с_собой()
        {
            Assert.That(Outcome.ComputeWinners(new List<Player> { WithScore(7, 3) }),
                Is.EqualTo(new[] { 7 }));
        }

        [Test]
        public void Без_игроков_победителей_нет()
        {
            // Так бывает, если все вышли до конца партии. Пустой список честнее,
            // чем назначить победителем кого-нибудь.
            Assert.That(Outcome.ComputeWinners(new List<Player>()), Is.Empty);
        }

        [Test]
        public void Null_не_роняет_подсчёт()
        {
            Assert.That(Outcome.ComputeWinners(null), Is.Empty);
        }

        [Test]
        public void Победители_отсортированы_по_id_а_не_по_порядку_мест()
        {
            // За партию список игроков мог перетасоваться: кто-то вышел, кто-то
            // выбрал другой стул. Порядок победителей не должен зависеть от
            // того, кто когда отключился.
            var winners = Outcome.ComputeWinners(new List<Player>
            {
                WithScore(4, 8),
                WithScore(1, 8),
                WithScore(3, 2),
                WithScore(2, 8),
            });

            Assert.That(winners, Is.EqualTo(new[] { 1, 2, 4 }));
        }
    }
}
