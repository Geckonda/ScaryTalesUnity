using Assets.Scripts.Network;
using Assets.Scripts.Network.Messages;
using Mirror;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Assets.Scripts.UIEntities
{
    /// <summary>
    /// Drives the pre-game lobby UI.
    ///
    /// Visibility:
    /// - The lobby panel auto-hides when no network role is active and
    ///   auto-shows when either NetworkServer is active (host) or
    ///   NetworkClient is connected. It hides again when the game starts.
    ///
    /// Roster:
    /// - On the host, the player list is read directly from the local
    ///   GameConnectionManager.
    /// - On non-host clients, the server broadcasts a LobbyStateUpdate
    ///   message every roster change; this script's NetworkClient handler
    ///   stashes the latest state and renders it into the waiting text.
    /// </summary>
    public class LobbyManager : MonoBehaviour
    {
        [Tooltip("Root GameObject for the lobby UI; auto-toggled based on connection state and game start.")]
        [SerializeField] private GameObject _lobbyPanel;

        [Tooltip("TMP text shown on the host: \"Players: 2/4 + names\".")]
        [SerializeField] private TMP_Text _statusText;

        [Tooltip("Host-only Start button. Hidden on non-host clients.")]
        [SerializeField] private Button _startButton;

        [Tooltip("TMP text shown on non-host clients while waiting for the host to start.")]
        [SerializeField] private TMP_Text _waitingText;

        [Tooltip("Extra GameObjects to deactivate when the game actually starts (e.g. your MenuCanvas with Create Room / Join Room buttons).")]
        [SerializeField] private GameObject[] _hideOnGameStart;

        private GameConnectionManager _net;
        private bool _netSubscribed;
        private bool _viewSubscribed;
        private bool _gameStarted;
        private bool _lobbyHandlerRegistered;
        private bool _hasLobbyState;
        private LobbyStateUpdate _lastLobbyState;

        private void Awake()
        {
            if (_startButton != null)
                _startButton.onClick.AddListener(OnStartClicked);
            RegisterLobbyHandler();
            TrySubscribe();
        }

        private void OnEnable()
        {
            RegisterLobbyHandler();
            TrySubscribe();
            Refresh();
        }

        private void OnDisable()
        {
            if (_net != null && _netSubscribed)
            {
                _net.OnRosterChanged -= Refresh;
                _netSubscribed = false;
            }
            var view = UnGameManager.Instance != null ? UnGameManager.Instance.ClientView : null;
            if (view != null && _viewSubscribed)
            {
                view.OnGameStarted -= HandleGameStarted;
                _viewSubscribed = false;
            }
        }

        private void TrySubscribe()
        {
            if (!_netSubscribed)
            {
                _net = NetworkManager.singleton as GameConnectionManager;
                if (_net != null)
                {
                    _net.OnRosterChanged += Refresh;
                    _netSubscribed = true;
                }
            }
            if (!_viewSubscribed)
            {
                var view = UnGameManager.Instance != null ? UnGameManager.Instance.ClientView : null;
                if (view != null)
                {
                    view.OnGameStarted += HandleGameStarted;
                    _viewSubscribed = true;
                }
            }
        }

        private void RegisterLobbyHandler()
        {
            if (_lobbyHandlerRegistered) return;
            // Mirror's NetworkClient.RegisterHandler can be called any
            // time; subsequent calls with the same type replace the
            // previous handler. Idempotent guard via _lobbyHandlerRegistered
            // keeps it tidy across enable/disable cycles.
            NetworkClient.RegisterHandler<LobbyStateUpdate>(OnLobbyStateMessage);
            _lobbyHandlerRegistered = true;
        }

        private void Update()
        {
            if (_netSubscribed && _viewSubscribed) return;
            TrySubscribe();
            Refresh();
        }

        private void OnLobbyStateMessage(LobbyStateUpdate msg)
        {
            _lastLobbyState = msg;
            _hasLobbyState = true;
            Refresh();
        }

        private void Refresh()
        {
            // Once the game has started, this script is done driving
            // visibility — HandleGameStarted hid the panel and the scene
            // reload on game-end / disconnect resets the whole thing.
            if (_gameStarted) return;

            bool isHost = NetworkServer.active;
            bool clientConnected = NetworkClient.isConnected;
            bool inLobby = isHost || clientConnected;

            if (_lobbyPanel != null)
                _lobbyPanel.SetActive(inLobby);
            if (!inLobby) return;

            if (_startButton != null)
                _startButton.gameObject.SetActive(isHost);
            if (_statusText != null)
                _statusText.gameObject.SetActive(isHost);
            if (_waitingText != null)
                _waitingText.gameObject.SetActive(!isHost);

            if (isHost)
            {
                if (_net == null) return;
                if (_statusText != null)
                {
                    var names = string.Join(", ", _net.Players.Select(p => p.Name));
                    _statusText.text = $"Players: {_net.PlayerCount}/{_net.MaxPlayers}\n{names}";
                }
                if (_startButton != null)
                    _startButton.interactable = _net.CanStart;
            }
            else
            {
                if (_waitingText == null) return;
                if (!_hasLobbyState)
                {
                    _waitingText.text = "Connecting...";
                    return;
                }
                var names = _lastLobbyState.PlayerNames != null
                    ? string.Join(", ", _lastLobbyState.PlayerNames)
                    : string.Empty;
                _waitingText.text =
                    $"Connected. Waiting for host to start...\n";
            }
        }

        private void OnStartClicked()
        {
            if (!NetworkServer.active) return;
            if (_net == null) return;
            _net.StartGameNow();
        }

        private void HandleGameStarted()
        {
            _gameStarted = true;
            if (_lobbyPanel != null) _lobbyPanel.SetActive(false);
            if (_hideOnGameStart != null)
            {
                foreach (var go in _hideOnGameStart)
                    if (go != null) go.SetActive(false);
            }
        }
    }
}
