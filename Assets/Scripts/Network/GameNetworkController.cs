using Assets.Libreries.ScaryTales.Rules.Templates.A;
using Assets.Libreries.ScaryTales.Rules.Templates.B;
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
        public static GameNetworkController Instance { get; set; }

        // ---- Server-only state ----
        private Dictionary<int, NetworkConnectionToClient> _playerConnections = new();
        private GameSession _serverSession;
        private NetworkDecisionRouter _router;
        private ServerEventBroadcaster _broadcaster;
        // Awaited by the server turn loop; completes with the chosen card
        // id when the active player's PlayCardIntent arrives.
        private TaskCompletionSource<int> _waitingForPlay;

        public GameSession ServerSession => _serverSession;

        private void Awake()
        {
            if (Instance == null)
                Instance = this;
            else
                Destroy(gameObject);
        }

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

            // Hardcoded rules match the legacy UnGameManager defaults.
            // Phase 4.2 lobby will let the host pick.
            _serverSession = new GameSession(gameManager, new A1(), new B2());

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
                        CurrentRuleId = 0,
                        CurrentFinalRuleId = 0,
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
                        gm.DrawCard(player);
                    }
                }

                while (!ctx.GameState.IsGameOver)
                {
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

                    await gm.ActivateAllPlayerPermanentCardEffects(current);
                    ctx.GameState.NextTurn();
                }

                int winnerId = ctx.Players.OrderByDescending(p => p.Score).First().Id;
                var scores = ctx.Players.Select(p => p.Score).ToArray();
                NetworkServer.SendToAll(new GameEndedEvent
                {
                    WinnerId = winnerId,
                    FinalScores = scores,
                });
            }
            catch (Exception e)
            {
                Debug.LogError($"[Server turn loop] {e}");
            }
        }

        // ---- Server-side intent handlers ----

        [Server]
        private void OnPlayCardIntent(NetworkConnectionToClient conn, PlayCardIntent msg)
        {
            var current = _serverSession?.CurrentPlayer;
            if (current == null) return;
            if (!_playerConnections.TryGetValue(current.Id, out var expectedConn) || expectedConn != conn)
            {
                Debug.LogWarning("[Server] PlayCardIntent from wrong connection.");
                return;
            }
            if (_waitingForPlay != null && !_waitingForPlay.Task.IsCompleted)
                _waitingForPlay.SetResult(msg.CardId);
        }

        [Server]
        private async void OnUseRuleEffectIntent(NetworkConnectionToClient conn, UseRuleEffectIntent msg)
        {
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
            catch (Exception e)
            {
                Debug.LogError($"[Server] UseRuleEffectIntent application failed: {e}");
            }
        }
    }

}
