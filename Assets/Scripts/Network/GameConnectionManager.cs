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
        private Dictionary<int, NetworkConnectionToClient> _connectionMap = new();
        private bool _gameStarted = false;

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

            var player = new Player(conn.connectionId, $"Player{_players.Count + 1}");
            _players.Add(player);
            _connectionMap[conn.connectionId] = conn;

            Debug.Log($"[Server] Player connected: {_players.Count}/{_maxPlayers}");
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
            if (_gameStarted)
            {
                // Mid-game disconnect: per Phase-3 non-goals, undefined for v1.
                // We just log and let the game continue with the parked
                // connection. NetworkDecisionRouter will hang on any
                // pending decision from the dropped player.
                Debug.LogWarning($"[Server] Player {conn.connectionId} disconnected mid-game (v1 ignores this).");
                base.OnServerDisconnect(conn);
                return;
            }

            int removed = _players.RemoveAll(p => p.Id == conn.connectionId);
            _connectionMap.Remove(conn.connectionId);
            if (removed > 0)
            {
                Debug.Log($"[Server] Player disconnected: {_players.Count}/{_maxPlayers}");
                OnRosterChanged?.Invoke();
                BroadcastLobbyState();
            }
            base.OnServerDisconnect(conn);
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

        /// <summary>
        /// Stops whatever network role this peer was in (host / client /
        /// server) and reloads the current scene. Reloading restores the
        /// scene to its inspector defaults, so MenuCanvas reappears, the
        /// lobby panel goes back to hidden, and any game-time GameObjects
        /// (cards, decks, etc.) get cleaned up automatically. Called on
        /// disconnect and on game-over.
        /// </summary>
        public static void ReturnToMenu()
        {
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

            var active = SceneManager.GetActiveScene();
            SceneManager.LoadScene(active.name);
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

            var controller = controllerObj.GetComponent<GameNetworkController>();
            controller.InitializeGame(_players, _connectionMap);
        }
    }
}
