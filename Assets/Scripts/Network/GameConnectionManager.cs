using Mirror;
using ScaryTales;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Assets.Scripts.Network
{
    /// <summary>
    /// Translates Mirror's server callbacks into operations on rooms, and
    /// owns the pieces that are genuinely per-server rather than per-game:
    /// the room registry and the intent dispatcher.
    ///
    /// <para>Phase 6.4a moved everything else into <see cref="Room"/> — the
    /// roster, the seats, the session, the turn loop, the departure policy.
    /// What is left here is address translation and lifecycle.</para>
    ///
    /// <para><b>Still exactly one room.</b> The registry is a single field, not
    /// a dictionary, because nothing can yet ask for a second one: creating
    /// and joining by code is 6.4b, and it needs the client work in 6.6 to be
    /// usable at all. The seam is <see cref="EnsureRoom"/> and
    /// <see cref="RoomFor"/> — everything downstream of them is already
    /// per-room.</para>
    /// </summary>
    public class GameConnectionManager : NetworkManager
    {
        [Tooltip("Minimum players a room can start a game with.")]
        [SerializeField] private int _minPlayers = 2;

        [Tooltip("Maximum players accepted into a room. Connections beyond this are rejected.")]
        [SerializeField] private int _maxPlayers = 4;

        [Tooltip("If true, a room starts its game automatically when it reaches MaxPlayers — the legacy behavior. Toggle off when you have a LobbyManager with a Start button so the host controls the moment of game start.")]
        [SerializeField] private bool _autoStartWhenFull = true;

        [Header("Rules in play")]
        [Tooltip("Rule id from RuleCatalog used during the game. The server is the only place this is chosen; clients learn it from GameStartedEvent. A lobby picker would drive these two fields.")]
        [SerializeField] private int _inGameRuleId = Assets.Libreries.ScaryTales.Rules.RuleCatalog.DefaultInGameRuleId;

        [Tooltip("Rule id from RuleCatalog scored at the end of the game.")]
        [SerializeField] private int _finalRuleId = Assets.Libreries.ScaryTales.Rules.RuleCatalog.DefaultFinalRuleId;

        // The single room. Becomes Dictionary<string /*code*/, Room> in 6.4b.
        private Room _room;

        // The server's one set of intent handlers. Lives for the life of the
        // server, not of a game, because Mirror keeps one handler per message
        // type process-wide.
        private ServerIntentDispatcher _dispatcher;

        // Fires on the server when a player joins or leaves. LobbyManager
        // listens for this to refresh its UI. Non-host clients don't see it —
        // the roster is server-side state, and they get LobbyStateUpdate.
        public event Action OnRosterChanged;

        // Lobby-facing forwarders. LobbyManager reads these and does not know
        // rooms exist; when 6.4b lands they will resolve against the local
        // player's room rather than the only one.
        private static readonly IReadOnlyList<Player> NoPlayers = new List<Player>();
        public IReadOnlyList<Player> Players => _room?.Players ?? NoPlayers;
        public int PlayerCount => _room?.PlayerCount ?? 0;
        public int MinPlayers => _minPlayers;
        public int MaxPlayers => _maxPlayers;
        public bool CanStart => _room != null && _room.CanStart;

        // ---- Room registry ----

        /// <summary>
        /// The room a connection belongs to. One room today, so membership is
        /// the only question; 6.4b makes this a dictionary lookup.
        /// </summary>
        private Room RoomFor(NetworkConnectionToClient conn) =>
            _room != null && conn != null && _room.HasConnection(conn.connectionId) ? _room : null;

        private Room EnsureRoom()
        {
            if (_room != null) return _room;
            _room = new Room(_minPlayers, _maxPlayers, _inGameRuleId, _finalRuleId);
            _room.RosterChanged += HandleRosterChanged;
            _room.Finished += HandleRoomFinished;
            return _room;
        }

        private void HandleRosterChanged(Room room) => OnRosterChanged?.Invoke();

        /// <summary>
        /// A room's game is over, however it ended. Drop its connections from
        /// the dispatcher index so late intents resolve to nothing instead of
        /// poking a dead session.
        /// </summary>
        private void HandleRoomFinished(Room room)
        {
            _dispatcher?.UnbindRoom(room);
        }

        // ---- Mirror server callbacks ----

        public override void OnServerAddPlayer(NetworkConnectionToClient conn)
        {
            EnsureDispatcher();
            var room = EnsureRoom();

            var result = room.CanAccept();
            if (result != Room.JoinResult.Ok)
            {
                Debug.LogWarning($"[Server] Connection {conn.connectionId} rejected: {result}.");
                conn.Disconnect();
                return;
            }

            base.OnServerAddPlayer(conn);

            if (room.TryAddPlayer(conn, out var player) != Room.JoinResult.Ok)
            {
                conn.Disconnect();
                return;
            }
            _dispatcher.Bind(conn.connectionId, room);

            Debug.Log($"[Server] {player.Name} took seat {player.Id}: {room.PlayerCount}/{_maxPlayers}");

            if (_autoStartWhenFull && room.PlayerCount >= _maxPlayers)
            {
                Debug.Log("[Server] Auto-starting game (room full).");
                StartGameNow();
            }
        }

        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            // Mirror calls this with NetworkServer.localConnection during
            // StopHost() teardown, and by then it can already be null.
            // There is no seat to release in that case.
            if (conn == null)
            {
                Debug.Log("[Server] OnServerDisconnect with a null connection (host teardown).");
                return;
            }

            var room = RoomFor(conn);
            if (room == null)
            {
                // Never seated — rejected at the door, or already released.
                base.OnServerDisconnect(conn);
                return;
            }

            // Anything this connection sends from now on resolves to no room
            // rather than into the game it just left.
            _dispatcher?.Unbind(conn.connectionId);
            // The room applies its own departure policy (end the game if one
            // was running); it may raise Finished from inside this call.
            room.RemoveConnection(conn.connectionId);

            base.OnServerDisconnect(conn);
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            EnsureDispatcher();
        }

        /// <summary>
        /// Creates this server's one intent dispatcher and claims every
        /// intent message type, once.
        ///
        /// Idempotent, and called from OnServerAddPlayer as well as from
        /// OnStartServer, because Mirror does not guarantee the order we would
        /// like: <c>SetupServer()</c> calls <c>NetworkServer.Listen()</c> and
        /// only later does <c>FinishStartHost()</c> call <c>OnStartServer()</c>
        /// — with an async scene load in between when <c>onlineScene</c> is
        /// set. Clients can therefore be accepted before this ever ran, and
        /// binding against a null dispatcher would silently deliver no intents
        /// at all, with nothing in the log.
        /// </summary>
        private void EnsureDispatcher()
        {
            if (_dispatcher != null) return;
            _dispatcher = new ServerIntentDispatcher();
            _dispatcher.RegisterHandlers();
        }

        public override void OnStopServer()
        {
            // NetworkManager is DontDestroyOnLoad, so it survives the scene
            // reload that ReturnToMenu triggers. Without this the next
            // StartHost would come up with the finished room still in place
            // and reject every join.
            ResetServer();
            base.OnStopServer();
        }

        /// <summary>
        /// Drops all rooms and the dispatcher. Split from ReturnToMenu because
        /// a headless server (Phase 6.5) recycles rooms with no notion of
        /// "show the menu".
        /// </summary>
        private void ResetServer()
        {
            if (_room != null)
            {
                // Room is no longer a NetworkBehaviour, so it gets no
                // OnStopServer of its own — the owner has to end it. Without
                // this, stopping the server mid-game would leave parked
                // decisions uncancelled and skip the notice to the clients.
                // A no-op if the game already ended.
                _room.AbortGame(0, "Сервер остановлен.");
                _room.RosterChanged -= HandleRosterChanged;
                _room.Finished -= HandleRoomFinished;
                _room = null;
            }
            // Dropped, not cleared: NetworkServer.Shutdown() clears the
            // handler table immediately after OnStopServer, so these delegates
            // go with it and the next server start builds a fresh dispatcher
            // with an empty index.
            _dispatcher = null;
            OnRosterChanged?.Invoke();
        }

        /// <summary>
        /// Server-only. Called by the host's LobbyManager when they click
        /// Start.
        /// </summary>
        [Server]
        public void StartGameNow()
        {
            if (_room == null)
            {
                Debug.LogWarning("[Server] StartGameNow ignored: no room.");
                return;
            }
            _room.StartGame();
        }

        // ---- Client-side / shared ----

        // Re-entrancy guard. ReturnToMenu → StopHost → StopClient →
        // OnClientDisconnect → ReturnToMenu is a genuine cycle in Mirror's
        // teardown, and it used to recurse and queue several LoadScene calls.
        private static bool _returningToMenu;

        /// <summary>
        /// Stops whatever network role this peer was in (host / client /
        /// server) and reloads the current scene. Reloading restores the
        /// scene to its inspector defaults, so MenuCanvas reappears, the
        /// lobby panel goes back to hidden, and any game-time GameObjects
        /// (cards, decks, etc.) get cleaned up automatically. Called on
        /// disconnect and on game-over.
        ///
        /// Re-entrant by nature: stopping the host disconnects our own
        /// client, which calls straight back in here. The first call wins
        /// and the rest are no-ops until the reloaded scene arrives.
        /// </summary>
        public static void ReturnToMenu()
        {
            if (_returningToMenu) return;
            _returningToMenu = true;

            var nm = NetworkManager.singleton;
            if (nm != null)
            {
                if (NetworkServer.active && NetworkClient.active)
                    nm.StopHost();
                else if (NetworkClient.active)
                    nm.StopClient();
                else if (NetworkServer.active)
                    nm.StopServer();
            }

            // LoadScene finishes at the end of the frame, so the guard has
            // to outlive this method — clear it once the new scene is up,
            // not in a finally.
            SceneManager.sceneLoaded += ClearReturnGuard;
            var active = SceneManager.GetActiveScene();
            SceneManager.LoadScene(active.name);
        }

        private static void ClearReturnGuard(Scene scene, LoadSceneMode mode)
        {
            SceneManager.sceneLoaded -= ClearReturnGuard;
            _returningToMenu = false;
        }

        public override void OnClientDisconnect()
        {
            base.OnClientDisconnect();
            // Disconnect mid-game (host quit, network failure, kicked):
            // reset to the menu state. Per Phase-3 non-goals there's no
            // reconnect flow — players just bounce back to the lobby
            // entry and start over.
            ReturnToMenu();
        }
    }
}
