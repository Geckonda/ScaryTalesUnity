using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace ScaryTales.Abstractions
{
    /// <summary>
    /// Отвечает за состояние игры
    /// </summary>
    public interface IGameState
    {
        /// <summary>
        /// Проверяет, ночь ли в игре.
        /// </summary>
        bool IsNight { get; }

        /// <summary>
        /// Проверяет, завершена ли игра.
        /// </summary>
        bool IsGameOver { get; }

        /// <summary>
        /// Счетчик текущих ходов.
        /// </summary>
        int TurnCount { get; }

        /// <summary>
        /// Сколько ДОПОЛНИТЕЛЬНЫХ карт текущий игрок ещё может разыграть в
        /// этом ходу. Ноль — обычный ход из одной карты.
        ///
        /// <para>Живёт на состоянии партии, а не на игроке: это свойство хода,
        /// а не человека. Обнуляется в начале каждого хода
        /// (<see cref="ClearExtraPlays"/>) — забытый счётчик перенёс бы чужой
        /// подарок на следующий ход.</para>
        /// </summary>
        int ExtraPlays { get; }

        /// <summary>
        /// Дать текущему игроку право разыграть ещё одну карту в этом ходу.
        /// Зовут эффекты карт («можно разыграть 1 дополнительную карту»).
        /// </summary>
        void GrantExtraPlay();

        /// <summary>
        /// Забрать одно право на дополнительную карту, если оно есть.
        /// </summary>
        /// <returns>Было ли что забирать.</returns>
        bool TryConsumeExtraPlay();

        /// <summary>
        /// Погасить все нерастраченные права на дополнительную карту.
        ///
        /// <para>Зовётся игровым циклом в начале КАЖДОГО хода, а не только при
        /// обычной смене. Смена хода — не единственный способ его начать: ход
        /// брошенного игрока обрывается на месте, очередь двигает
        /// <see cref="RemovePlayer"/>, а <see cref="NextTurn"/> при этом
        /// намеренно пропускается. Подарок ушедшего достался бы следующему.</para>
        /// </summary>
        void ClearExtraPlays();

        /// <summary>
        /// Получить текущего игрока.
        /// </summary>
        Player GetCurrentPlayer();

        /// <summary>
        /// Получить список всех игроков в игре.
        /// </summary>
        List<Player> GetPlayers();

        /// <summary>
        /// Перейти к следующему ходу.
        /// </summary>
        void NextTurn();

        /// <summary>
        /// Убрать игрока из партии посреди неё — он отключился.
        /// </summary>
        /// <returns>true, если игрок был в партии и удалён.</returns>
        bool RemovePlayer(Player player);

        /// <summary>
        /// Завершить игру.
        /// </summary>
        void EndGame();

        /// <summary>
        /// Переключить фазу игры.
        /// </summary>
        void ToggleNightPhase();
        /// <summary>
        /// Устанавливает значение ночи
        /// </summary>
        void SetPhase(bool isNight);
        /// <summary>
        /// Возвращает время суток в виде строки
        /// </summary>
        string GetTimeOfday();
    }

}
