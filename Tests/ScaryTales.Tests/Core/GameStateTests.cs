using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ScaryTales;

namespace ScaryTales.Tests.Core
{
    [TestFixture]
    public class GameStateTests
    {
        private static GameState WithPlayers(params string[] names)
        {
            var players = names.Select((n, i) => new Player(i + 1, n)).ToList();
            return new GameState(players);
        }

        // ---- Старт ----

        [Test]
        public void Партия_начинается_ночью_с_первого_игрока()
        {
            var state = WithPlayers("А", "Б", "В");

            Assert.That(state.IsNight, Is.True, "первая карта времени суток — Ночь");
            Assert.That(state.IsGameOver, Is.False);
            Assert.That(state.TurnCount, Is.Zero);
            Assert.That(state.CurrentPlayerIndex, Is.Zero);
            Assert.That(state.GetCurrentPlayer().Name, Is.EqualTo("А"));
            Assert.That(state.ExtraPlays, Is.Zero);
        }

        // ---- Очередь ходов ----

        [Test]
        public void Ход_переходит_по_кругу()
        {
            var state = WithPlayers("А", "Б", "В");

            var order = new List<string>();
            for (int i = 0; i < 7; i++)
            {
                order.Add(state.GetCurrentPlayer().Name);
                state.NextTurn();
            }

            Assert.That(order, Is.EqualTo(new[] { "А", "Б", "В", "А", "Б", "В", "А" }));
        }

        [Test]
        public void Счётчик_ходов_растёт_на_каждом_переходе()
        {
            var state = WithPlayers("А", "Б");
            state.NextTurn();
            state.NextTurn();

            Assert.That(state.TurnCount, Is.EqualTo(2));
        }

        [Test]
        public void Переход_хода_гасит_непотраченные_розыгрыши()
        {
            // Право разыграть лишнюю карту — подарок на ЭТОТ ход. Утечь
            // следующему игроку оно не должно.
            var state = WithPlayers("А", "Б");
            state.GrantExtraPlay();
            state.GrantExtraPlay();

            state.NextTurn();

            Assert.That(state.ExtraPlays, Is.Zero);
        }

        // ---- Дополнительные розыгрыши ----

        [Test]
        public void Розыгрыши_копятся_и_тратятся_по_одному()
        {
            var state = WithPlayers("А");
            state.GrantExtraPlay();
            state.GrantExtraPlay();

            Assert.That(state.ExtraPlays, Is.EqualTo(2));
            Assert.That(state.TryConsumeExtraPlay(), Is.True);
            Assert.That(state.ExtraPlays, Is.EqualTo(1));
            Assert.That(state.TryConsumeExtraPlay(), Is.True);
            Assert.That(state.ExtraPlays, Is.Zero);
        }

        [Test]
        public void Трата_на_нуле_отказывает_и_не_уходит_в_минус()
        {
            // На этом отказе цикл хода заканчивает ход. Уйди счётчик в минус —
            // ход бы не кончился никогда.
            var state = WithPlayers("А");

            Assert.That(state.TryConsumeExtraPlay(), Is.False);
            Assert.That(state.ExtraPlays, Is.Zero);
        }

        [Test]
        public void Сброс_розыгрышей_обнуляет_счётчик()
        {
            var state = WithPlayers("А");
            state.GrantExtraPlay();
            state.GrantExtraPlay();

            state.ClearExtraPlays();

            Assert.That(state.ExtraPlays, Is.Zero);
            Assert.That(state.TryConsumeExtraPlay(), Is.False);
        }

        // ---- Время суток ----

        [Test]
        public void Время_суток_переключается_и_задаётся_явно()
        {
            var state = WithPlayers("А");

            state.ToggleNightPhase();
            Assert.That(state.IsNight, Is.False);
            Assert.That(state.GetTimeOfday(), Is.EqualTo("День"));

            state.ToggleNightPhase();
            Assert.That(state.IsNight, Is.True);
            Assert.That(state.GetTimeOfday(), Is.EqualTo("Ночь"));

            state.SetPhase(false);
            Assert.That(state.IsNight, Is.False);
            state.SetPhase(false);
            Assert.That(state.IsNight, Is.False, "явная установка не переключает, а задаёт");
        }

        // ---- Конец партии ----

        [Test]
        public void Конец_партии_взводится_и_не_отменяется()
        {
            var state = WithPlayers("А");

            state.EndGame();
            Assert.That(state.IsGameOver, Is.True);

            state.EndGame();
            Assert.That(state.IsGameOver, Is.True);
        }

        // ---- Уход игрока ----
        //
        // Очередь — это индекс в списке, и удаление сдвигает её саму.
        // Каждый случай разбирается отдельно, потому что ошибка на единицу
        // здесь означает пропущенный ход у живого игрока.

        [Test]
        public void Уход_игрока_слева_не_даёт_очереди_перескочить()
        {
            var state = WithPlayers("А", "Б", "В");
            state.NextTurn(); // ходит Б

            state.RemovePlayer(state.Players.First(p => p.Name == "А"));

            Assert.That(state.GetCurrentPlayer().Name, Is.EqualTo("Б"), "ход перескочил через игрока");
        }

        [Test]
        public void Уход_игрока_справа_не_трогает_очередь()
        {
            var state = WithPlayers("А", "Б", "В");
            state.NextTurn(); // ходит Б

            state.RemovePlayer(state.Players.First(p => p.Name == "В"));

            Assert.That(state.GetCurrentPlayer().Name, Is.EqualTo("Б"));
        }

        [Test]
        public void Уход_ходящего_игрока_передаёт_ход_следующему()
        {
            var state = WithPlayers("А", "Б", "В");
            state.NextTurn(); // ходит Б

            state.RemovePlayer(state.Players.First(p => p.Name == "Б"));

            Assert.That(state.GetCurrentPlayer().Name, Is.EqualTo("В"));
            Assert.That(state.TurnCount, Is.EqualTo(1), "несостоявшийся ход не считается заново");
        }

        [Test]
        public void Уход_последнего_по_списку_заворачивает_очередь_в_начало()
        {
            var state = WithPlayers("А", "Б", "В");
            state.NextTurn();
            state.NextTurn(); // ходит В — последний в списке

            state.RemovePlayer(state.Players.First(p => p.Name == "В"));

            Assert.That(state.GetCurrentPlayer().Name, Is.EqualTo("А"), "индекс уехал за край списка");
        }

        [Test]
        public void Уход_предпоследнего_оставляет_одного_ходящим()
        {
            var state = WithPlayers("А", "Б");
            state.NextTurn(); // ходит Б

            state.RemovePlayer(state.Players.First(p => p.Name == "А"));

            Assert.That(state.Players.Count, Is.EqualTo(1));
            Assert.That(state.GetCurrentPlayer().Name, Is.EqualTo("Б"));
        }

        [Test]
        public void Уход_всех_игроков_не_оставляет_индекс_за_краем()
        {
            var state = WithPlayers("А", "Б");

            state.RemovePlayer(state.Players[0]);
            state.RemovePlayer(state.Players[0]);

            Assert.That(state.Players, Is.Empty);
            Assert.That(state.CurrentPlayerIndex, Is.Zero);
        }

        [Test]
        public void Уход_чужого_игрока_отказывает_и_ничего_не_меняет()
        {
            var state = WithPlayers("А", "Б");

            Assert.That(state.RemovePlayer(new Player(99, "Никто")), Is.False);
            Assert.That(state.Players.Count, Is.EqualTo(2));
            Assert.That(state.CurrentPlayerIndex, Is.Zero);
        }

        [Test]
        public void Очередь_остаётся_целой_после_ухода_и_продолжения_партии()
        {
            // Сквозная проверка: после ухода круг должен остаться кругом, без
            // пропусков и повторов.
            var state = WithPlayers("А", "Б", "В", "Г");
            state.NextTurn(); // ходит Б
            state.RemovePlayer(state.Players.First(p => p.Name == "Г"));

            var order = new List<string>();
            for (int i = 0; i < 6; i++)
            {
                order.Add(state.GetCurrentPlayer().Name);
                state.NextTurn();
            }

            Assert.That(order, Is.EqualTo(new[] { "Б", "В", "А", "Б", "В", "А" }));
        }
    }
}
