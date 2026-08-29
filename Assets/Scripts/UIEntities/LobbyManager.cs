using Assets.Scripts.Network.Messages;
using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Assets.Scripts.UIEntities
{
    /// <summary>
    /// Drives the pre-game lobby UI (Phase 6.6).
    ///
    /// <para>Entirely client-side now. It used to read the server's roster
    /// directly whenever <c>NetworkServer.active</c> said "you are the host" —
    /// which stops meaning anything once the server is its own process. Both
    /// facts it needs arrive over the wire instead: <c>RoomJoinedEvent</c> says
    /// which room you are in and whether you own it, <c>LobbyStateUpdate</c>
    /// says who else is here and whether the game may start.</para>
    /// </summary>
    public class LobbyManager : MonoBehaviour
    {
        [Tooltip("Root GameObject for the lobby UI; shown once you are in a room, hidden when the game starts.")]
        [SerializeField] private GameObject _lobbyPanel;

        [Tooltip("TMP text for the room code and player list.")]
        [SerializeField] private TMP_Text _statusText;

        [Tooltip("Start button. Shown only to the player who created the room.")]
        [SerializeField] private Button _startButton;

        [Tooltip("TMP text shown to everyone who is not the room's creator.")]
        [SerializeField] private TMP_Text _waitingText;

        [Tooltip("Extra GameObjects to deactivate when the game actually starts (e.g. your MenuCanvas with Create Room / Join Room buttons).")]
        [SerializeField] private GameObject[] _hideOnGameStart;

        // Everything below arrives from the server; none of it is inferred.
        private bool _inRoom;
        private bool _isOwner;
        private string _code = string.Empty;
        private string _roomName = string.Empty;
        private bool _hasLobbyState;
        private LobbyStateUpdate _lobbyState;
        private bool _gameStarted;

        private bool _handlersRegistered;
        private bool _viewSubscribed;

        private void Awake()
        {
            if (_startButton != null)
                _startButton.onClick.AddListener(OnStartClicked);
            RegisterHandlers();
            Refresh();
        }

        private void OnEnable()
        {
            RegisterHandlers();
            TrySubscribeToView();
            Refresh();
        }

        private void OnDisable()
        {
            var view = UnGameManager.Instance != null ? UnGameManager.Instance.ClientView : null;
            if (view != null && _viewSubscribed)
            {
                view.OnGameStarted -= HandleGameStarted;
                _viewSubscribed = false;
            }
        }

        private void RegisterHandlers()
        {
            if (_handlersRegistered) return;
            // NetworkClient.RegisterHandler can be called any time; a second
            // call with the same type replaces the first. The guard just keeps
            // it tidy across enable/disable cycles.
            NetworkClient.RegisterHandler<RoomJoinedEvent>(OnRoomJoined);
            NetworkClient.RegisterHandler<LobbyStateUpdate>(OnLobbyState);
            _handlersRegistered = true;
        }

        private void TrySubscribeToView()
        {
            if (_viewSubscribed) return;
            var view = UnGameManager.Instance != null ? UnGameManager.Instance.ClientView : null;
            if (view == null) return;
            view.OnGameStarted += HandleGameStarted;
            _viewSubscribed = true;
        }

        private void Update()
        {
            // UnGameManager may construct its ClientGameView after this
            // component wakes; keep trying until it exists.
            if (!_viewSubscribed) TrySubscribeToView();
        }

        private void OnRoomJoined(RoomJoinedEvent evt)
        {
            _inRoom = true;
            _isOwner = evt.IsOwner;
            _code = evt.Code;
            _roomName = evt.RoomName;
            Refresh();
        }

        private void OnLobbyState(LobbyStateUpdate msg)
        {
            _lobbyState = msg;
            _hasLobbyState = true;
            Refresh();
        }

        private void Refresh()
        {
            // Once the game has started this script is done driving
            // visibility — HandleGameStarted hid the panel, and the scene
            // reload on game-end resets everything.
            if (_gameStarted) return;

            if (_lobbyPanel != null)
                _lobbyPanel.SetActive(_inRoom);
            if (!_inRoom) return;

            // _statusText and _waitingText occupy the same place in the
            // panel, so exactly one of them may ever be active. The owner
            // gets the status line and the Start button; everyone else gets
            // the waiting line. Both texts carry the code, because whoever is
            // looking may be the one reading it out to a friend.
            if (_startButton != null)
                _startButton.gameObject.SetActive(_isOwner);
            if (_statusText != null)
                _statusText.gameObject.SetActive(_isOwner);
            if (_waitingText != null)
                _waitingText.gameObject.SetActive(!_isOwner);

            string roster = _hasLobbyState
                ? $"Игроки: {_lobbyState.PlayerCount}/{_lobbyState.MaxPlayers}\n{string.Join(", ", _lobbyState.PlayerNames ?? new string[0])}"
                : "Ожидание...";
            string header = string.IsNullOrEmpty(_roomName) || _roomName == _code
                ? $"Код комнаты: {_code}"
                : $"{_roomName} — код: {_code}";

            // Write only into the one that is showing — filling both is how
            // the same text ended up rendered twice on top of itself.
            if (_isOwner)
            {
                if (_statusText != null)
                    _statusText.text = $"{header}\n{roster}";
                if (_startButton != null)
                    _startButton.interactable = _hasLobbyState && _lobbyState.CanStart;
            }
            else if (_waitingText != null)
            {
                _waitingText.text = $"{header}\n{roster}\nЖдём, пока создатель начнёт игру...";
            }
        }

        private void OnStartClicked()
        {
            if (!_inRoom || !_isOwner) return;
            // Sent rather than called directly: on a dedicated server the
            // creator is an ordinary client with no access to the room object.
            NetworkClient.Send(new StartGameIntent());
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
