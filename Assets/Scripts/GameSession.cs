using Assets.Libraries.ScaryTales;
using ScaryTales;
using ScaryTales.Abstractions;
using System.Collections.Generic;

namespace Assets.Scripts
{
    /// <summary>
    /// Owns one round of canonical game state. Plain C#, no Unity lifecycle.
    /// After Phase 3 the session lives on the server only — clients hold a
    /// ClientGameView mirror instead. After Phase 4.1 it's player-count
    /// agnostic: there is no "local" or "opponent" on the canonical session;
    /// each client derives those from its own ClientGameView.LocalPlayerId.
    /// </summary>
    public class GameSession
    {
        public GameManager GameManager { get; }
        public IGameContext Context => GameManager._context;
        public IReadOnlyList<Player> Players => Context.Players;
        public Rule CurrentRuleInGame { get; }
        public Rule CurrentFinalRule { get; }
        public bool CanChooseRule { get; set; }
        public Player CurrentPlayer => Context.GameState.GetCurrentPlayer();

        public GameSession(
            GameManager gameManager,
            Rule currentRuleInGame,
            Rule currentFinalRule)
        {
            GameManager = gameManager;
            CurrentRuleInGame = currentRuleInGame;
            CurrentFinalRule = currentFinalRule;
            CanChooseRule = false;
        }
    }
}
