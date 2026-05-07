using Assets.Libreries.ScaryTales;
using ScaryTales;
using ScaryTales.Abstractions;
using ScaryTales.Decisions;
using System.Linq;

namespace Assets.Scripts
{
    /// <summary>
    /// Owns one round of game state. Plain C#, no Unity lifecycle —
    /// MonoBehaviours host a session but don't carry game state themselves.
    /// Phase 2 introduces this; Phase 3 will replace its router and Phase 4
    /// will generalize LocalPlayer/LocalOpponent into a player list.
    /// </summary>
    public class GameSession
    {
        public GameManager GameManager { get; }
        public IGameContext Context => GameManager._context;
        public Player LocalPlayer { get; }
        public Player LocalOpponent { get; }
        public Rule CurrentRuleInGame { get; }
        public Rule CurrentFinalRule { get; }
        public bool CanChooseRule { get; set; }
        public Player CurrentPlayer => Context.GameState.GetCurrentPlayer();

        public GameSession(
            GameManager gameManager,
            Rule currentRuleInGame,
            Rule currentFinalRule,
            Player localPlayer,
            Player localOpponent)
        {
            GameManager = gameManager;
            CurrentRuleInGame = currentRuleInGame;
            CurrentFinalRule = currentFinalRule;
            LocalPlayer = localPlayer;
            LocalOpponent = localOpponent;
            CanChooseRule = false;

            // Legacy: GameManager mirrors local/opponent on its own surface
            // because some old callers read from there. Phase 5 candidate.
            GameManager.LocalPlayer = localPlayer;
            GameManager.LocalOpponent = localOpponent;

            // The Phase-1 adapter needs an external lookup for rule effects
            // (rules don't live in core). Wire it once at session start.
            if (Context.Router is PlayerInputAdapterRouter adapter)
            {
                adapter.SetRuleEffectLookup(id =>
                    CurrentRuleInGame.Effects.FirstOrDefault(e => e.Id == id));
            }
        }
    }
}
