using Assets.Libreries.ScaryTales.Rules;
using Assets.Scripts;
using Assets.Scripts.Network.Messages;
using Mirror;
using ScaryTales;
using ScaryTales.Abstractions;
using ScaryTales.Interaction_Entities.EnvUnity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace Assets.Scripts.Network
{
    /// <summary>
    /// Phase 3 server-authoritative network controller.
    ///
    /// Server-side: owns the canonical GameSession, registers handlers for
    /// every Intent the clients send, drives the turn loop, and uses
    /// ServerEventBroadcaster to translate engine events into wire
    /// DomainEvents.
    ///
    /// Client-side: nothing happens here. Clients react to DomainEvents via
    /// ClientGameView (constructed in UnGameManager.Awake) and send Intents
    /// directly via NetworkClient.Send.
    /// </summary>
    public class GameNetworkController : NetworkBehaviour
    {
        // ---- Server-only state ----
        private Dictionary<int, NetworkConnectionToClient> _playerConnections = new();
        private GameSession _serverSession;
        private NetworkDecisionRouter _router;
        private ServerEventBroadcaster _broadcaster;
        // Awaited by the server turn loop; completes with the chosen card
        // id when the active player's PlayCardIntent arrives.
        private TaskCompletionSource<int> _waitingForPlay;
        // Phase 6.1. _gameOver is set by *both* endings — the natural one
        // and an abort — and is what makes every teardown path idempotent.
        // _aborted narrows that to "ended early", which is the only case
        // where clients get a GameAbortedEvent instead of a GameEndedEvent.
        private bool _gameOver;
        private bool _aborted;

        public GameSession ServerSession => _serverSession;
        public bool IsAborted => _aborted;
        // Non-zero means the room is wedged waiting on somebody. Read by
        // the abort path's log line so a stuck room is visible.
        public int PendingDecisionCount => _router?.PendingDecisionCount ?? 0;

        [Header("Rules in play")]
        [Tooltip("Rule id from RuleCatalog used during the game. The server is the only place this is chosen; clients learn it from GameStartedEvent. A lobby picker would drive these two fields.")]
        [SerializeField] private int _inGameRuleId = RuleCatalog.DefaultInGameRuleId;

        [Tooltip("Rule id from RuleCatalog scored at the end of the game.")]
        [SerializeField] private int _finalRuleId = RuleCatalog.DefaultFinalRuleId;

        // ---- Server-side composition root ----

        [Server]
        public void InitializeGame(List<Player> players, Dictionary<int, NetworkConnectionToClient> connectionMap)
        {
            _playerConnections = connectionMap;
            _router = new NetworkDecisionRouter(_playerConnections);

            var notifier = new UnityNotifier("Server");
            var board = new GameBoard();
            var builder = new GameBuilder(notifier, board, players);
            var gameManager = builder.Build(_router);

            // The server is the only authority on which rules are in play;
            // the ids go out in GameStartedEvent so clients rebuild the same
            // ones instead of hardcoding their own copy.
            var inGameRule = RuleCatalog.Create(_inGameRuleId);
            if (inGameRule == null)
            {
                Debug.LogError($"[InitializeGame] unknown in-game rule id {_inGameRuleId}; falling back to the default.");
                _inGameRuleId = RuleCatalog.DefaultInGameRuleId;
                inGameRule = RuleCatalog.Create(_inGameRuleId);
            }

            var finalRule = RuleCatalog.Create(_finalRuleId);
            if (finalRule == null)
            {
                Debug.LogError($"[InitializeGame] unknown final rule id {_finalRuleId}; falling back to the default.");
                _finalRuleId = RuleCatalog.DefaultFinalRuleId;
                finalRule = RuleCatalog.Create(_finalRuleId);
            }

            _serverSession = new GameSession(gameManager, inGameRule, finalRule);

            _broadcaster = new ServerEventBroadcaster(_serverSession);

            NetworkServer.RegisterHandler<PlayCardIntent>(OnPlayCardIntent);
            NetworkServer.RegisterHandler<UseRuleEffectIntent>(OnUseRuleEffectIntent);

            // The host machine also runs the host's UnGameManager — give it
            // a reference to the canonical session so any host-only debug
            // tooling still works. Non-host clients leave HostSession null.
            if (UnGameManager.Instance != null)
                UnGameManager.Instance.SetHostSession(_serverSession);

            // Per-client GameStartedEvent so each recipient learns their
            // own LocalPlayerId from the same shared payload.
            var deckIds = _serverSession.Context.Deck.GetCardIds().ToArray();
            var playerInfos = players
                .Select(p => new PlayerInfo { Id = p.Id, Name = p.Name })
                .ToArray();
            var startPlayerId = _serverSession.CurrentPlayer?.Id ?? players[0].Id;

            foreach (var p in players)
            {
                if (_playerConnections.TryGetValue(p.Id, out var conn))
                {
                    conn.Send(new GameStartedEvent
                    {
                        Players = playerInfos,
                        DeckOrder = deckIds,
                        StartPlayerId = startPlayerId,
                        LocalPlayerId = p.Id,
                        CurrentRuleId = _inGameRuleId,
                        CurrentFinalRuleId = _finalRuleId,
                    });
                }
                else
                {
                    Debug.LogError($"[InitializeGame] missing connection for player {p.Id}");
                }
            }

            // Kick off the canonical turn loop. async void on a server-side
            // entry point — exceptions land in Unity's logger via the
            // try/catch in RunGameLoopAsync.
            RunGameLoopAsync();
        }

        [Server]
        private async void RunGameLoopAsync()
        {
            try
            {
                var ctx = _serverSession.Context;
                var gm = _serverSession.GameManager;

                // Initial setup mirrors the legacy UnGameManager.StartGame
                // flow: place the night card, deal 5 to each player.
                var night = ctx.Deck.TakeCardByName("Ночь");
                if (night != null)
                    gm.PutCardInTimeOfDaySlot(night);

                foreach (var player in ctx.Players)
                {
                    for (int i = 0; i < 5; i++)
                    {
                        await Task.Delay(50);
                        ThrowIfGameOver();
                        gm.DrawCard(player);
                    }
                }

                while (!ctx.GameState.IsGameOver)
                {
                    // An abort that lands while the loop happens to be
                    // running rather than awaiting has nothing to cancel,
                    // so the loop has to check for itself — otherwise it
                    // sails on and parks on a fresh _waitingForPlay that
                    // nobody will ever answer.
                    ThrowIfGameOver();

                    var current = ctx.GameState.GetCurrentPlayer();
                    NetworkServer.SendToAll(new TurnAdvancedEvent
                    {
                        CurrentPlayerId = current.Id,
                        TurnCount = ctx.GameState.TurnCount,
                        IsNight = ctx.GameState.IsNight,
                    });

                    gm.DrawCard(current);

                    if (current.Hand.Count == 0)
                    {
                        gm.EndGame();
                        break;
                    }

                    _waitingForPlay = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                    int chosenCardId = await _waitingForPlay.Task;
                    var card = current.Hand.FirstOrDefault(c => c.Id == chosenCardId);
                    if (card == null)
                    {
                        Debug.LogWarning($"[Server turn loop] PlayCardIntent for unknown card {chosenCardId}; loop continues.");
                        continue;
                    }
                    await gm.PlayCard(card);
                    ThrowIfGameOver();

                    await gm.ActivateAllPlayerPermanentCardEffects(current);
                    ThrowIfGameOver();

                    ctx.GameState.NextTurn();
                }

                if (_gameOver) return;
                _gameOver = true;

                int winnerId = ctx.Players.OrderByDescending(p => p.Score).First().Id;
                var scores = ctx.Players.Select(p => p.Score).ToArray();
                NetworkServer.SendToAll(new GameEndedEvent
                {
                    WinnerId = winnerId,
                    FinalScores = scores,
                });
                Teardown();
            }
            catch (OperationCanceledException)
            {
                // Expected: AbortGame cancelled a parked decision or the
                // wait for a card, unwinding whatever effect was suspended.
                // AbortGame has already told the clients why.
                Debug.Log("[Server turn loop] stopped by abort.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Server turn loop] {e}");
                // An engine fault leaves the room in an unknown state.
                // Ending it beats leaving everyone staring at a table that
                // will never advance.
                AbortGame(0, "Ошибка на сервере. Игра прервана.");
            }
        }

        /// <summary>
        /// Bails out of the turn loop if the room ended while we were away.
        /// Throwing (rather than returning a bool the caller might forget to
        /// check) puts every abort on the single OperationCanceledException
        /// path, whether the loop was suspended on a decision or just
        /// between statements.
        /// </summary>
        private void ThrowIfGameOver()
        {
            if (_gameOver)
                throw new OperationCanceledException("Game ended while the turn loop was running.");
        }

        // ---- Teardown (Phase 6.1) ----

        /// <summary>
        /// Ends the game early and tells everyone why. Safe to call more
        /// than once and from any of the paths that can discover the room
        /// is doomed — a player leaving, an engine fault, the server
        /// shutting down.
        ///
        /// Cancelling the parked decisions is what actually unwedges the
        /// room: the suspended effect resumes by throwing, the turn loop
        /// unwinds, and nothing is left holding the session alive.
        /// </summary>
        [Server]
        public void AbortGame(int leftPlayerId, string reason)
        {
            // A game that already reached its natural end is over, not
            // aborted — don't overwrite the winner screen with a teardown
            // notice when the server later stops.
            if (_gameOver) return;
            _gameOver = true;
            _aborted = true;

            int pending = _router?.PendingDecisionCount ?? 0;
            Debug.LogWarning($"[Server] Aborting game: {reason} (pending decisions: {pending}).");

            if (NetworkServer.active)
            {
                NetworkServer.SendToAll(new GameAbortedEvent
                {
                    LeftPlayerId = leftPlayerId,
                    Reason = reason,
                });
            }

            // Order matters: release the turn loop's own wait first, then
            // the router's, so whichever the loop is sitting on throws.
            _waitingForPlay?.TrySetCanceled();
            _router?.CancelAll(reason);

            Teardown();
        }

        /// <summary>
        /// Drops this game's server-side handlers and releases the session.
        /// Mirror keeps one handler per message type process-wide, so a
        /// finished game left registered would intercept the next one's
        /// intents.
        /// </summary>
        [Server]
        private void Teardown()
        {
            if (NetworkServer.active)
            {
                NetworkServer.UnregisterHandler<PlayCardIntent>();
                NetworkServer.UnregisterHandler<UseRuleEffectIntent>();
            }
            _router?.Dispose();
        }

        public override void OnStopServer()
        {
            // Covers the paths that kill the server without going through
            // AbortGame (StopHost, application quit, scene reload).
            AbortGame(0, "Сервер остановлен.");
            base.OnStopServer();
        }

        // ---- Server-side intent handlers ----

        [Server]
        private void OnPlayCardIntent(NetworkConnectionToClient conn, PlayCardIntent msg)
        {
            if (_gameOver) return;
            var current = _serverSession?.CurrentPlayer;
            if (current == null) return;
            if (!_playerConnections.TryGetValue(current.Id, out var expectedConn) || expectedConn != conn)
            {
                Debug.LogWarning("[Server] PlayCardIntent from wrong connection.");
                return;
            }
            // TrySetResult rather than SetResult: an abort can cancel this
            // TCS between the IsCompleted check and the call.
            _waitingForPlay?.TrySetResult(msg.CardId);
        }

        [Server]
        private async void OnUseRuleEffectIntent(NetworkConnectionToClient conn, UseRuleEffectIntent msg)
        {
            if (_gameOver) return;
            var current = _serverSession?.CurrentPlayer;
            if (current == null) return;
            if (!_playerConnections.TryGetValue(current.Id, out var expectedConn) || expectedConn != conn)
                return;

            var effect = _serverSession.CurrentRuleInGame.Effects.FirstOrDefault(e => e.Id == msg.RuleEffectId);
            if (effect == null) return;
            if (!effect.IsEffectAvailable(_serverSession.Context))
            {
                Debug.LogWarning($"[Server] UseRuleEffectIntent for unavailable effect {msg.RuleEffectId}.");
                return;
            }
            try
            {
                await effect.ApplyEffect(_serverSession.Context);
            }
            catch (OperationCanceledException)
            {
                // The room was aborted while this effect was waiting on a
                // decision. Expected, and already reported to the clients.
            }
            catch (Exception e)
            {
                Debug.LogError($"[Server] UseRuleEffectIntent application failed: {e}");
            }
        }
    }

}
