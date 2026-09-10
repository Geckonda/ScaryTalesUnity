using System.Threading.Tasks;

namespace ScaryTales.Abstractions
{
    /// <summary>
    /// Пауза между шагами партии.
    ///
    /// <para><b>Зачем интерфейс, а не Task.Delay.</b> Движок делает паузы не
    /// потому, что ему нужно подождать вычислений, а потому, что человеку надо
    /// успеть увидеть, что произошло: карта легла — очки начислились — эффект
    /// сработал. Это темп показа, а не часть правил. Партия из сорока ходов с
    /// зашитым Task.Delay(1000) идёт сорок секунд и в тесте, где смотреть
    /// некому.</para>
    ///
    /// <para>Поэтому темп задаётся снаружи: на сервере — настоящей задержкой,
    /// в тестах — <see cref="InstantGameClock"/>, который возвращает
    /// управление сразу. Правила от этого не меняются: пауза ничего не решает,
    /// она только даёт посмотреть.</para>
    /// </summary>
    public interface IGameClock
    {
        /// <param name="milliseconds">Сколько подождать. Неположительное — не ждать.</param>
        Task Beat(int milliseconds);
    }

    /// <summary>Настоящая пауза. Умолчание для партии с живыми игроками.</summary>
    public sealed class RealGameClock : IGameClock
    {
        public Task Beat(int milliseconds) =>
            milliseconds <= 0 ? Task.CompletedTask : Task.Delay(milliseconds);
    }

    /// <summary>
    /// Пауза, которой нет. Для тестов и для любого прогона без зрителей.
    /// </summary>
    public sealed class InstantGameClock : IGameClock
    {
        /// <summary>Сколько раз и на сколько просили подождать — порядок показа проверяем по этому.</summary>
        public int BeatCount { get; private set; }

        public Task Beat(int milliseconds)
        {
            BeatCount++;
            return Task.CompletedTask;
        }
    }
}
