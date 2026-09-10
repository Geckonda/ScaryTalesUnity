using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using NUnit.Framework;
using ScaryTales;
using ScaryTales.Abstractions;
using ScaryTales.Tests.Fakes;

namespace ScaryTales.Tests.Core
{
    /// <summary>
    /// Темп показа вынесен за интерфейс, чтобы прогон без зрителей не ждал
    /// впустую. Правила от этого не зависят — вот это и проверяется.
    /// </summary>
    [TestFixture]
    public class GameClockTests
    {
        [Test]
        public void Розыгрыш_карты_спрашивает_паузу_у_часов()
        {
            var game = GameFixture.Start(playerCount: 1);
            var card = game.InHand(new TestCard(name: "Карта", points: 2) { Id = 500 });

            game.Manager.PlayCard(card).GetAwaiter().GetResult();

            Assert.That(game.Clock.BeatCount, Is.EqualTo(1));
        }

        [Test]
        public void Партия_без_зрителей_идёт_без_задержек()
        {
            // Смысл всей затеи: с зашитым Task.Delay(1000) двадцать розыгрышей
            // заняли бы двадцать секунд.
            var game = GameFixture.Start(playerCount: 1);
            var watch = Stopwatch.StartNew();

            for (int i = 0; i < 20; i++)
            {
                var card = game.InHand(new TestCard(name: $"Карта {i}") { Id = 600 + i });
                game.Manager.PlayCard(card).GetAwaiter().GetResult();
            }

            watch.Stop();
            Assert.That(watch.ElapsedMilliseconds, Is.LessThan(1000),
                "розыгрыш всё ещё ждёт по-настоящему");
        }

        [Test]
        public void Настоящие_часы_не_ждут_на_неположительном_числе()
        {
            var clock = new RealGameClock();

            Assert.That(clock.Beat(0), Is.SameAs(Task.CompletedTask));
            Assert.That(clock.Beat(-5), Is.SameAs(Task.CompletedTask));
        }

        [Test]
        public void По_умолчанию_партия_получает_настоящие_часы()
        {
            // Умолчание важнее удобства тестов: партия с живыми игроками не
            // должна зависеть от того, что кто-то не забыл передать часы.
            var players = new List<Player> { new Player(1, "А") };
            var manager = new GameManager(
                new GameState(players), new GameBoard(), players,
                new Deck(new List<Card>()), new ItemManager(GameBuilder.MakeItemTemplates()),
                new RecordingNotifier(), new ScriptedRouter());

            Assert.That(manager._context.Clock, Is.TypeOf<RealGameClock>());
        }
    }
}
