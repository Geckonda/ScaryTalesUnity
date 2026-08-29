using Assets.Libreries.ScaryTales.Rules;
using Assets.Scripts;
using Assets.Scripts.Network.Messages;
using Mirror;
using ScaryTales;
using ScaryTales.Interaction_Entities.EnvUnity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace Assets.Scripts.Network
{
    /// <summary>
    /// One game room: its lobby, its players, its engine, its turn loop
    /// (Phase 6.4).
    ///
    /// <para>This is the former <c>GameNetworkController</c>, grown to absorb
    /// the roster and seat bookkeeping that used to sit in
    /// <see cref="GameConnectionManager"/> as process-global state. Everything
    /// a game needs now hangs off one object, so several can coexist in one
    /// process — which is the whole point of Phase 6.</para>
    ///
    /// <para><b>No longer a NetworkBehaviour.</b> Phase 3.3 deleted every
    /// <c>Cmd</c>/<c>Rpc</c> from it, leaving a networked object with no
    /// networked behaviour — a prefab and a <c>NetworkServer.Spawn</c> call
    /// that bought nothing and made per-room instancing awkward. It is plain
    /// C# now, constructed directly by its owner.</para>
    ///
    /// <para>Server-side only. Clients hold a <c>ClientGameView</c> built from
    /// the DomainEvent stream and never see this class.</para>
    /// </summary>
    public class Room
    {
        // ---- Configuration, fixed at construction ----
        private readonly int _minPlayers;
        private readonly int _maxPlayers;
        private int _inGameRuleId;
        private int _finalRuleId;

        // ---- Lobby ----
        private readonly List<Player> _players = new();
        // Reverse index: Mirror's connection id → seat id. Needed because a
        // disconnect only hands us a connection.
        private readonly Dictionary<int, int> _seatByConnection = new();
        // Monotonic within this room, never reused, and never 0 — 0 is the
        // "no player" sentinel on the wire (GameAbortedEvent.LeftPlayerId,
        // ServerEventBroadcaster's owner fallbacks).
        private int _nextSeatId = 1;
        private bool _gameStarted;

        /// <summary>This room's seats, and its only route to their clients.</summary>
        public RoomChannel Channel { get; } = new();

        // ---- Game ----
        private GameSession _session;
        private NetworkDecisionRouter _router;
        private ServerEventBroadcaster _broadcaster;
        // Awaited by the turn loop; completes with the chosen card id when
        // the active player's PlayCardIntent arrives.
        private TaskCompletionSource<int> _waitingForPlay;
        // _gameOver is set by *both* endings — the natural one and an abort —
        // and is what makes every teardown path idempotent. _aborted narrows
        // that to "ended early", the only case where clients get a
        // GameAbortedEvent instead of a GameEndedEvent.
        private bool _gameOver;
        private bool _aborted;

        // ---- Identity ----
        /// <summary>Short code players type to join. Assigned by the registry.</summary>
        public string Code { get; }
        /// <summary>Cosmetic, chosen by whoever created the room.</summary>
        public string Name { get; }
        /// <summary>
        /// Seat of the player who created the room — the only one allowed to
        /// start the game. 0 until someone joins. Deliberately a seat rather
        /// than a connection, so ownership survives the connection (the same
        /// reasoning as Player.Id in 6.1).
        /// </summary>
        public int OwnerSeatId { get; private set; }

        // ---- Observation ----
        public IReadOnlyList<Player> Players => _players;
        public int PlayerCount => _players.Count;
        public int MinPlayers => _minPlayers;
        public int MaxPlayers => _maxPlayers;
        public bool IsGameStarted => _gameStarted;
        public bool IsAborted => _aborted;
        public bool CanStart => !_gameStarted && _players.Count >= _minPlayers;
        /// <summary>
        /// No live connections left. Not the same as an empty roster: a
        /// mid-game departure deliberately keeps its seat (see OnSeatVacated),
        /// so the roster can be non-empty while nobody is actually here.
        /// This is the condition the owner destroys a room on.
        /// </summary>
        public bool IsAbandoned => Channel.Count == 0;
        public GameSession Session => _session;
        /// <summary>ServerIntentDispatcher reaches the router through here.</summary>
        public NetworkDecisionRouter Router => _router;
        /// <summary>Non-zero means the room is waiting on somebody.</summary>
        public int PendingDecisionCount => _router?.PendingDecisionCount ?? 0;

        public Room(string code, string name, int minPlayers, int maxPlayers, int inGameRuleId, int finalRuleId)
        {
            Code = code;
            Name = name;
            _minPlayers = minPlayers;
            _maxPlayers = maxPlayers;
            _inGameRuleId = inGameRuleId;
            _finalRuleId = finalRuleId;
        }

        // ---- Membership ----

        public enum JoinResult { Ok, RoomFull, GameInProgress }

        /// <summary>Whether this room would take another player right now.</summary>
        private JoinResult CanAccept()
        {
            if (_players.Count >= _maxPlayers) return JoinResult.RoomFull;
            if (_gameStarted) return JoinResult.GameInProgress;
            return JoinResult.Ok;
        }

        /// <summary>
        /// Seats a connection, if the room will have it. The seat id becomes
        /// the new <see cref="Player.Id"/> — deliberately not the connection
        /// id, so a seat outlives the connection sitting in it (Phase 6.1).
        /// </summary>
        public JoinResult TryAddPlayer(NetworkConnectionToClient conn, out Player player)
        {
            player = null;
            var accepted = CanAccept();
            if (accepted != JoinResult.Ok) return accepted;

            int seatId = _nextSeatId++;
            player = new Player(seatId, $"Player{_players.Count + 1}");
            _players.Add(player);
            Channel.Bind(seatId, conn);
            _seatByConnection[conn.connectionId] = seatId;
            // First one in owns the room.
            if (OwnerSeatId == 0) OwnerSeatId = seatId;

            BroadcastLobbyState();
            return JoinResult.Ok;
        }

        public bool HasConnection(int connectionId) => _seatByConnection.ContainsKey(connectionId);

        /// <summary>
        /// Releases whatever seat this connection held and applies the
        /// departure policy. Returns false if the connection wasn't seated
        /// here at all.
        /// </summary>
        public bool RemoveConnection(int connectionId)
        {
            if (!_seatByConnection.TryGetValue(connectionId, out int seatId)) return false;
            _seatByConnection.Remove(connectionId);
            Channel.Unbind(seatId);
            OnSeatVacated(seatId);
            return true;
        }

        /// <summary>
        /// The one place that decides what a player leaving means. Called
        /// after the seat has been unbound from its connection but while the
        /// seat itself still exists.
        ///
        /// <para><b>Current policy (Phase 6.1): a mid-game departure ends the
        /// room.</b> The alternative — carrying on a player short — needs the
        /// engine to drop somebody from the turn order mid-game, which is a
        /// change to Assets/Libreries and a re-audit of all 18 effects.</para>
        ///
        /// <para>This is also the seam for reconnect. The seat is deliberately
        /// left in <c>_players</c> rather than trimmed, so the id in
        /// GameAbortedEvent still resolves to a name on the clients — and so a
        /// future reconnect flow has a seat to hand back. Adding it means
        /// replacing the immediate AbortGame with a grace window, and
        /// rebinding this seat in the Channel when the player returns. Note
        /// that a grace window is not enough on its own: the room would also
        /// have to stop asking the missing seat for decisions while it waits,
        /// which is why this ends the room today instead of half-waiting.</para>
        /// </summary>
        private void OnSeatVacated(int seatId)
        {
            var player = _players.FirstOrDefault(p => p.Id == seatId);
            string name = player?.Name ?? $"Player {seatId}";

            if (!_gameStarted)
            {
                _players.RemoveAll(p => p.Id == seatId);
                Debug.Log($"[Room {Code}] {name} left the lobby: {_players.Count}/{_maxPlayers}");
                BroadcastLobbyState();
                return;
            }

            Debug.LogWarning($"[Room] {name} (seat {seatId}) left mid-game — ending the room.");
            AbortGame(seatId, $"{name} покинул игру. Партия завершена.");
        }

        public void BroadcastLobbyState()
        {
            if (!NetworkServer.active) return;
            Channel.SendToRoom(new LobbyStateUpdate
            {
                PlayerCount = _players.Count,
                MinPlayers = _minPlayers,
                MaxPlayers = _maxPlayers,
                PlayerNames = _players.Select(p => p.Name).ToArray(),
                CanStart = CanStart,
            });
        }

        // ---- Composition root for this room's game ----

        /// <summary>
        /// Entry point for the owner's StartGameIntent. The check is against
        /// the owner's *seat*, so a stray intent from another player in the
        /// room — or from a connection that has since been reseated — is
        /// refused rather than trusted.
        /// </summary>
        public void HandleStartGame(NetworkConnectionToClient conn)
        {
            if (!Channel.IsSeatedAt(OwnerSeatId, conn))
            {
                Debug.LogWarning($"[Room {Code}] StartGameIntent from a non-owner; ignored.");
                return;
            }
            StartGame();
        }

        public void StartGame()
        {
            if (!CanStart)
            {
                Debug.LogWarning($"[Room] StartGame ignored: started={_gameStarted}, players={_players.Count}/min={_minPlayers}.");
                return;
            }
            _gameStarted = true;
            Debug.Log($"[Room] Starting game with {_players.Count} players.");

            _router = new NetworkDecisionRouter(Channel);

            var notifier = new UnityNotifier("Server");
            var board = new GameBoard();
            var builder = new GameBuilder(notifier, board, _players);
            var gameManager = builder.Build(_router);

            // The server is the only authority on which rules are in play;
            // the ids go out in GameStartedEvent so clients rebuild the same
            // ones instead of hardcoding their own copy.
            var inGameRule = RuleCatalog.Create(_inGameRuleId);
            if (inGameRule == null)
            {
                Debug.LogError($"[Room] unknown in-game rule id {_inGameRuleId}; falling back to the default.");
                _inGameRuleId = RuleCatalog.DefaultInGameRuleId;
                inGameRule = RuleCatalog.Create(_inGameRuleId);
            }

            var finalRule = RuleCatalog.Create(_finalRuleId);
            if (finalRule == null)
            {
                Debug.LogError($"[Room] unknown final rule id {_finalRuleId}; falling back to the default.");
                _finalRuleId = RuleCatalog.DefaultFinalRuleId;
                finalRule = RuleCatalog.Create(_finalRuleId);
            }

            _session = new GameSession(gameManager, inGameRule, finalRule);
            _broadcaster = new ServerEventBroadcaster(_session, Channel);

            // Intent handlers are NOT registered here. Mirror keeps one per
            // message type process-wide, so claiming them per game is what
            // would break the moment a second room existed.
            // ServerIntentDispatcher owns them and routes by connection.

            // Host-model convenience: the host machine also runs a client, so
            // hand its UnGameManager the canonical session for host-only debug
            // tooling. Meaningless on a dedicated server, and ambiguous once
            // one host holds several rooms — it simply takes the last one.
            if (UnGameManager.Instance != null)
                UnGameManager.Instance.SetHostSession(_session);

            // Per-client GameStartedEvent so each recipient learns their own
            // LocalPlayerId from the same shared payload.
            var deckIds = _session.Context.Deck.GetCardIds().ToArray();
            var playerInfos = _players
                .Select(p => new PlayerInfo { Id = p.Id, Name = p.Name })
                .ToArray();
            var startPlayerId = _session.CurrentPlayer?.Id ?? _players[0].Id;

            foreach (var p in _players)
            {
                bool sent = Channel.SendToSeat(p.Id, new GameStartedEvent
                {
                    Players = playerInfos,
                    DeckOrder = deckIds,
                    StartPlayerId = startPlayerId,
                    LocalPlayerId = p.Id,
                    CurrentRuleId = _inGameRuleId,
                    CurrentFinalRuleId = _finalRuleId,
                });
                if (!sent)
                    Debug.LogError($"[Room] missing connection for player {p.Id}");
            }

            // async void on a server-side entry point — exceptions land in
            // Unity's logger via the try/catch in RunGameLoopAsync.
            RunGameLoopAsync();
        }

        private async void RunGameLoopAsync()
        {
            try
            {
                var ctx = _session.Context;
                var gm = _session.GameManager;

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
                    Channel.SendToRoom(new TurnAdvancedEvent
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
                        Debug.LogWarning($"[Room] PlayCardIntent for unknown card {chosenCardId}; loop continues.");
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
                Channel.SendToRoom(new GameEndedEvent
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
                Debug.Log("[Room] turn loop stopped by abort.");
            }
            catch (Exception e)
            {
                // One room's engine fault must not take down the others, so
                // this catch is the boundary — nothing propagates past it.
                Debug.LogError($"[Room] turn loop: {e}");
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

        // ---- Teardown ----

        /// <summary>
        /// Ends the game early and tells everyone why. Safe to call more than
        /// once and from any of the paths that can discover the room is
        /// doomed — a player leaving, an engine fault, the server shutting
        /// down.
        ///
        /// Cancelling the parked decisions is what actually unwedges the
        /// room: the suspended effect resumes by throwing, the turn loop
        /// unwinds, and nothing is left holding the session alive.
        /// </summary>
        public void AbortGame(int leftPlayerId, string reason)
        {
            // Nothing to abort before the game exists, and a game that
            // reached its natural end is over rather than aborted — don't
            // overwrite the winner screen with a teardown notice.
            if (!_gameStarted || _gameOver) return;
            _gameOver = true;
            _aborted = true;

            Debug.LogWarning($"[Room] Aborting game: {reason} (pending decisions: {PendingDecisionCount}).");

            if (NetworkServer.active)
            {
                Channel.SendToRoom(new GameAbortedEvent
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
        /// Releases this game's session and its parked decisions.
        ///
        /// Note what it does not do: unregister Mirror handlers. Those are
        /// process-wide and shared by every room, so a finished room pulling
        /// them down would silence all the others. Retiring a room is purely
        /// an index operation on the RoomRegistry, which the owner does when
        /// the last connection actually leaves.
        /// </summary>
        private void Teardown()
        {
            _router?.Dispose();
        }

        // ---- Intent handlers ----
        // Called by ServerIntentDispatcher once it has resolved that the
        // sender belongs to this room. Authorization (is it really this
        // player's turn) stays here, where the session is.

        public void HandlePlayCard(NetworkConnectionToClient conn, PlayCardIntent msg)
        {
            if (_gameOver) return;
            var current = _session?.CurrentPlayer;
            if (current == null) return;
            if (!Channel.IsSeatedAt(current.Id, conn))
            {
                Debug.LogWarning("[Room] PlayCardIntent from wrong connection.");
                return;
            }
            // TrySetResult rather than SetResult: an abort can cancel this
            // TCS between the IsCompleted check and the call.
            _waitingForPlay?.TrySetResult(msg.CardId);
        }

        public async void HandleUseRuleEffect(NetworkConnectionToClient conn, UseRuleEffectIntent msg)
        {
            if (_gameOver) return;
            var current = _session?.CurrentPlayer;
            if (current == null) return;
            if (!Channel.IsSeatedAt(current.Id, conn))
                return;

            var effect = _session.CurrentRuleInGame.Effects.FirstOrDefault(e => e.Id == msg.RuleEffectId);
            if (effect == null) return;
            if (!effect.IsEffectAvailable(_session.Context))
            {
                Debug.LogWarning($"[Room] UseRuleEffectIntent for unavailable effect {msg.RuleEffectId}.");
                return;
            }
            try
            {
                await effect.ApplyEffect(_session.Context);
            }
            catch (OperationCanceledException)
            {
                // The room was aborted while this effect was waiting on a
                // decision. Expected, and already reported to the clients.
            }
            catch (Exception e)
            {
                Debug.LogError($"[Room] UseRuleEffectIntent application failed: {e}");
            }
        }
    }
}
