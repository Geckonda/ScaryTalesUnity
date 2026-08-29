using Assets.Scripts.Network.Messages;
using Mirror;
using ScaryTales;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Assets.Scripts.Network
{
    /// <summary>
    /// Owns the lobby roster and the seat↔connection binding for one room.
    ///
    /// Phase 6.1 separated two things that used to be the same number:
    /// a <b>seat id</b> (what <see cref="Player.Id"/> is, stable for the
    /// life of the room, what goes out on the wire) and a
    /// <b>connection id</b> (Mirror's, which a player loses the moment
    /// they drop and never gets back). <see cref="_connectionMap"/> is the
    /// single mutable binding between them, and it is the hook a reconnect
    /// flow would rebind — see <see cref="OnSeatVacated"/>.
    /// </summary>
    public class GameConnectionManager : NetworkManager
    {
        [Tooltip("Minimum players the host can start a game with.")]
        [SerializeField] private int _minPlayers = 2;

        [Tooltip("Maximum players accepted into the lobby. Connections beyond this are rejected.")]
        [SerializeField] private int _maxPlayers = 4;

        [Tooltip("If true, the game starts automatically when the roster reaches MaxPlayers — the legacy behavior. Toggle off when you have a LobbyManager with a Start button so the host controls the moment of game start.")]
        [SerializeField] private bool _autoStartWhenFull = true;

        [SerializeField] private GameObject gameNetworkControllerPrefab;

        private List<Player> _players = new();

        // Seat id → the connection currently sitting in it. The only
        // mutable link between a seat and a network connection.
        private Dictionary<int, NetworkConnectionToClient> _connectionMap = new();

        // Reverse index: Mirror's connection id → seat id. Needed because
        // OnServerDisconnect only hands us a connection.
        private Dictionary<int, int> _seatByConnection = new();

        // Monotonic, never reused within a room, and never 0 — 0 is the
        // "no player" sentinel on the wire (GameAbortedEvent.LeftPlayerId,
        // ServerEventBroadcaster's owner fallbacks).
        private int _nextSeatId = 1;

        private bool _gameStarted = false;
        private GameNetworkController _controller;

        // Fires on the server when a player joins or leaves the lobby.
        // LobbyManager listens for this to refresh its UI. Non-host clients
        // don't see this event because the roster is server-side state —
        // they just show a generic "waiting for host" status.
        public event Action OnRosterChanged;

        public IReadOnlyList<Player> Players => _players;
        public int PlayerCount => _players.Count;
        public int MinPlayers => _minPlayers;
        public int MaxPlayers => _maxPlayers;
        public bool CanStart => !_gameStarted && _players.Count >= _minPlayers;

        public override void OnServerAddPlayer(NetworkConnectionToClient conn)
        {
            if (_players.Count >= _maxPlayers)
            {
                Debug.LogWarning($"[Server] Connection {conn.connectionId} rejected: lobby full ({_maxPlayers}).");
                conn.Disconnect();
                return;
            }
            if (_gameStarted)
            {
                Debug.LogWarning($"[Server] Connection {conn.connectionId} rejected: game already in progress.");
                conn.Disconnect();
                return;
            }

            base.OnServerAddPlayer(conn);

            int seatId = _nextSeatId++;
            var player = new Player(seatId, $"Player{_players.Count + 1}");
            _players.Add(player);
            _connectionMap[seatId] = conn;
            _seatByConnection[conn.connectionId] = seatId;

            Debug.Log($"[Server] {player.Name} took seat {seatId}: {_players.Count}/{_maxPlayers}");
            OnRosterChanged?.Invoke();
            BroadcastLobbyState();

            if (_autoStartWhenFull && _players.Count >= _maxPlayers)
            {
                Debug.Log("[Server] Auto-starting game (lobby full).");
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

            if (!_seatByConnection.TryGetValue(conn.connectionId, out int seatId))
            {
                // Never got a seat — rejected at the door, or already
                // released by an earlier call.
                base.OnServerDisconnect(conn);
                return;
            }

            _seatByConnection.Remove(conn.connectionId);
            _connectionMap.Remove(seatId);

            OnSeatVacated(seatId);

            base.OnServerDisconnect(conn);
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
        /// replacing the immediate AbortGame with a grace window, and rebinding
        /// <c>_connectionMap[seatId]</c> when the player returns. Note that a
        /// grace window is not enough on its own: the room would also have to
        /// stop asking the missing seat for decisions while it waits, which is
        /// why this ends the room today instead of half-waiting.</para>
        /// </summary>
        [Server]
        private void OnSeatVacated(int seatId)
        {
            var player = _players.FirstOrDefault(p => p.Id == seatId);
            string name = player?.Name ?? $"Player {seatId}";

            if (!_gameStarted)
            {
                _players.RemoveAll(p => p.Id == seatId);
                Debug.Log($"[Server] {name} left the lobby: {_players.Count}/{_maxPlayers}");
                OnRosterChanged?.Invoke();
                BroadcastLobbyState();
                return;
            }

            Debug.LogWarning($"[Server] {name} (seat {seatId}) left mid-game — ending the room.");
            if (_controller != null)
            {
                _controller.AbortGame(seatId, $"{name} покинул игру. Партия завершена.");
            }
            else
            {
                // Nothing left to cancel the parked decisions, which is
                // exactly the wedge Phase 6.1 exists to prevent.
                Debug.LogError("[Server] No GameNetworkController to abort the room — pending decisions may be stranded.");
            }
        }

        [Server]
        private void BroadcastLobbyState()
        {
            if (!NetworkServer.active) return;
            NetworkServer.SendToAll(new LobbyStateUpdate
            {
                PlayerCount = _players.Count,
                MinPlayers = _minPlayers,
                MaxPlayers = _maxPlayers,
                PlayerNames = _players.Select(p => p.Name).ToArray(),
            });
        }

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

        public override void OnStopServer()
        {
            // NetworkManager is DontDestroyOnLoad, so it survives the scene
            // reload that ReturnToMenu triggers. Without this the next
            // StartHost would come up with _gameStarted still true and
            // reject every join.
            ResetRoom();
            base.OnStopServer();
        }

        /// <summary>
        /// Returns the room to its pre-lobby state. Split out from
        /// ReturnToMenu because a headless server (Phase 6.5) needs to
        /// recycle a room without any notion of "show the menu".
        /// </summary>
        [Server]
        private void ResetRoom()
        {
            // Fresh instances rather than Clear(): the finished game's
            // controller, router and GameBuilder all hold references to
            // these same objects, and emptying them out from under a
            // still-unwinding turn loop would be a needless hazard.
            _players = new List<Player>();
            _connectionMap = new Dictionary<int, NetworkConnectionToClient>();
            _seatByConnection = new Dictionary<int, int>();
            _nextSeatId = 1;
            _gameStarted = false;
            _controller = null;
            OnRosterChanged?.Invoke();
        }

        /// <summary>
        /// Server-only. Called by the host's LobbyManager when they click
        /// Start. Spawns the GameNetworkController and kicks off
        /// InitializeGame.
        /// </summary>
        [Server]
        public void StartGameNow()
        {
            if (!CanStart)
            {
                Debug.LogWarning($"[Server] StartGameNow ignored: gameStarted={_gameStarted}, players={_players.Count}/min={_minPlayers}.");
                return;
            }
            _gameStarted = true;

            Debug.Log($"[Server] Starting game with {_players.Count} players.");

            var controllerObj = Instantiate(gameNetworkControllerPrefab);
            NetworkServer.Spawn(controllerObj);

            _controller = controllerObj.GetComponent<GameNetworkController>();
            _controller.InitializeGame(_players, _connectionMap);
        }
    }
}
