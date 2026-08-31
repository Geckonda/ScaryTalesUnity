using ScaryTales.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ScaryTales
{
    public class GameState : IGameState
    {
        public bool IsNight { get; private set; } 
        public bool IsGameOver { get; private set; } 
        public int TurnCount { get; private set; } 
        public int CurrentPlayerIndex { get; private set; } 
        public List<Player> Players { get; private set; }
        public GameState(List<Player> players)
        {
            Players = players;
            CurrentPlayerIndex = 0; // Начинаем с первого игрока
            IsNight = true;
            IsGameOver = false;
            TurnCount = 0;
        }

        public void EndGame()
        {
            IsGameOver = true;
        }

        public Player GetCurrentPlayer() => Players[CurrentPlayerIndex];

        public List<Player> GetPlayers() => Players;

        public void NextTurn()
        {
            TurnCount++;
            CurrentPlayerIndex = (CurrentPlayerIndex + 1) % Players.Count;
        }

        /// <summary>
        /// Убирает игрока из партии посреди неё (он отключился) и чинит
        /// очередь ходов.
        ///
        /// <para>Очередь — это индекс в списке, поэтому удаление сдвигает её
        /// саму. Разбор случаев на списке [A,B,C], где ходит B (индекс 1):
        /// ушёл A — B уезжает на индекс 0, и индекс надо уменьшить, иначе ход
        /// перескочит на C; ушёл C — слева ничего не сдвинулось, индекс тот
        /// же; ушёл сам B — на его индекс встал C, то есть ход естественным
        /// образом переходит к следующему, и трогать индекс не надо, только
        /// не дать ему уехать за край списка.</para>
        ///
        /// <para>Счётчик ходов не трогаем: ход ушедшего игрока не состоялся,
        /// но и не начинался заново.</para>
        /// </summary>
        public bool RemovePlayer(Player player)
        {
            int index = Players.IndexOf(player);
            if (index < 0) return false;

            Players.RemoveAt(index);

            if (Players.Count == 0)
            {
                CurrentPlayerIndex = 0;
                return true;
            }

            if (index < CurrentPlayerIndex) CurrentPlayerIndex--;
            CurrentPlayerIndex %= Players.Count;
            return true;
        }

        public void ToggleNightPhase()
        {
            IsNight = !IsNight;
        }

        public void SetPhase(bool isNight)
        {
            IsNight = isNight;
        }

        public string GetTimeOfday()
            => IsNight ? "Ночь" : "День";
    }
}
