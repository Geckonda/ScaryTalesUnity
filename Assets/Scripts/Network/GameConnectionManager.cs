using Assets.Scripts.Network.Messages;
using Mirror;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Assets.Scripts.Network
{
    /// <summary>
    /// Turns Mirror's server callbacks into operations on rooms, and owns the
    /// two things that are per-server rather than per-game: the
    /// <see cref="RoomRegistry"/> and the <see cref="ServerIntentDispatcher"/>.
    ///
    /// <para><b>Connecting is no longer joining (Phase 6.4b).</b> A connection
    /// arrives in the lobby with no room and no seat. It gets one only by
    /// sending <c>CreateRoomIntent</c> or <c>JoinRoomIntent</c> — which is what
    /// lets one server hold several games at once, and what makes "type a
    /// code" rather than "type an IP" the way in.</para>
    /// </summary>
    public class GameConnectionManager : NetworkManager
    {
        [Tooltip("Minimum players a room can start a game with.")]
        [SerializeField] private int _minPlayers = 2;

        [Tooltip("Maximum players accepted into a room. Joins beyond this are refused.")]
        [SerializeField] private int _maxPlayers = 4;

        [Tooltip("If true, a room starts its game automatically when it reaches MaxPlayers. Leave off so the room's creator decides when to start.")]
        [SerializeField] private bool _autoStartWhenFull = false;

        [Tooltip("Most rooms this server will hold at once. Beyond it, creating a room is refused rather than letting one process fill up with games.")]
        [SerializeField] private int _maxRooms = 32;

        [Tooltip("TESTING ONLY. Give the first room this code instead of a random one, so you don't have to read a new code off the screen every run. Leave EMPTY for normal random codes. A second room created while this code is taken falls back to a random one.")]
        [SerializeField] private string _fixedRoomCode = "LOCALHOST";

        [Header("Rules in play")]
        [Tooltip("Rule id from RuleCatalog used during the game. The server is the only place this is chosen; clients learn it from GameStartedEvent. A lobby picker would drive these two fields.")]
        [SerializeField] private int _inGameRuleId = Assets.Libreries.ScaryTales.Rules.RuleCatalog.DefaultInGameRuleId;

        [Tooltip("Rule id from RuleCatalog scored at the end of the game.")]
        [SerializeField] private int _finalRuleId = Assets.Libreries.ScaryTales.Rules.RuleCatalog.DefaultFinalRuleId;

        [Header("Dedicated server")]
        [Tooltip("Start as a server with no player of its own when the process is headless or was launched with the server flag below.")]
        [SerializeField] private bool _autoStartDedicatedServer = true;

        [Tooltip("Command-line flag that makes this process a dedicated server. Lets an ordinary (non-headless) build act as one — which is how you run a real server locally without making a Dedicated Server build.")]
        [SerializeField] private string _serverFlag = "-server";

        [Tooltip("Command-line flag that overrides the listen port, e.g. -port 7778. Useful for running a server alongside an editor on one machine.")]
        [SerializeField] private string _portFlag = "-port";

        [Tooltip("Hide the game and menu UI when running as a dedicated server. The canvases would otherwise idle harmlessly, but a server has no business drawing a card table.")]
        [SerializeField] private bool _hideUiOnDedicatedServer = true;

        private RoomRegistry _registry;
        private ServerIntentDispatcher _dispatcher;

        /// <summary>
        /// True on a process that runs the engine for other people and plays
        /// no part in the game itself. The distinction matters wherever the
        /// old host model assumed "server" implied "and also a player here" —
        /// see <see cref="ReturnToMenu"/>.
        /// </summary>
        public static bool IsDedicatedServer { get; private set; }

        // ---- Dedicated server entry point (Phase 6.5) ----

        public override void Start()
        {
            // Mirror's own headlessStartMode path first; if the scene has it
            // configured, it has already started something and we leave it be.
            base.Start();
            if (NetworkServer.active || NetworkClient.active) return;
            if (!_autoStartDedicatedServer || !WantsDedicatedServer()) return;

            IsDedicatedServer = true;
            ApplyPortOverride();
            if (_hideUiOnDedicatedServer) HideClientUi();

            // Mirror caps the frame rate only when it detects a headless
            // process; a normal build running with -server would otherwise
            // render nothing as fast as it possibly can.
            Application.targetFrameRate = sendRate;

            Debug.Log("[Server] Starting as a dedicated server (no local player).");
            StartServer();
        }

        /// <summary>
        /// Two ways in. Headless covers a Dedicated Server build or
        /// <c>-batchmode -nographics</c>; the flag covers a normal build you
        /// want to run as a server, which is the only practical way to test
        /// several rooms without building a separate server target
        /// (<c>Utils.IsHeadless()</c> is graphics-device based, so an ordinary
        /// windowed build never satisfies it).
        /// </summary>
        private bool WantsDedicatedServer()
        {
            // The flag is explicit intent — always honour it.
            if (HasCommandLineFlag(_serverFlag)) return true;
            if (!Utils.IsHeadless()) return false;

            // Headless detection is compile-time in the editor: selecting the
            // Dedicated Server build target defines UNITY_SERVER, so
            // Utils.IsHeadless() is true in Play Mode even though there is a
            // window and a GPU. Without this guard, switching platform to
            // build a server turns the editor itself into one — canvases
            // hidden, blank screen, no way to test as a client.
            //
            // Mirror applies the same rule to its own headlessStartMode
            // (NetworkManager.Start), and editorAutoStart is its field for
            // opting in. Reuse it rather than inventing a second switch.
            return !Application.isEditor || editorAutoStart;
        }

        private void ApplyPortOverride()
        {
            if (!TryGetCommandLineValue(_portFlag, out var raw)) return;
            if (!ushort.TryParse(raw, out var port))
            {
                Debug.LogError($"[Server] Ignoring {_portFlag} '{raw}': not a port number.");
                return;
            }
            if (Transport.active is PortTransport portTransport)
            {
                portTransport.Port = port;
                Debug.Log($"[Server] Listening port overridden to {port}.");
            }
            else
            {
                Debug.LogWarning($"[Server] Transport {Transport.active?.GetType().Name} has no port to override.");
            }
        }

        /// <summary>
        /// Switches off every canvas in the scene. The UI is already inert on
        /// a server — it is driven by ClientGameView, which is fed by
        /// NetworkClient, which never connects here — so this is about not
        /// spending frames on layout rather than about correctness.
        /// </summary>
        private static void HideClientUi()
        {
            var canvases = FindObjectsOfType<Canvas>();
            foreach (var canvas in canvases)
                canvas.gameObject.SetActive(false);
            Debug.Log($"[Server] Client UI hidden ({canvases.Length} canvases).");
        }

        private static bool HasCommandLineFlag(string flag)
        {
            if (string.IsNullOrWhiteSpace(flag)) return false;
            foreach (var arg in System.Environment.GetCommandLineArgs())
                if (string.Equals(arg, flag, System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static bool TryGetCommandLineValue(string flag, out string value)
        {
            value = null;
            if (string.IsNullOrWhiteSpace(flag)) return false;
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], flag, System.StringComparison.OrdinalIgnoreCase))
                {
                    value = args[i + 1];
                    return true;
                }
            }
            return false;
        }

        // ---- Server lifecycle ----

        public override void OnStartServer()
        {
            base.OnStartServer();
            EnsureServerObjects();
        }

        /// <summary>
        /// Builds the registry and the dispatcher and claims every message
        /// type, once.
        ///
        /// Idempotent, and called from <see cref="OnServerConnect"/> as well as
        /// from OnStartServer, because Mirror does not guarantee the order we
        /// would like: <c>SetupServer()</c> calls <c>NetworkServer.Listen()</c>
        /// and only later does <c>FinishStartHost()</c> call <c>OnStartServer()</c>
        /// — with an async scene load in between when <c>onlineScene</c> is
        /// set. Clients can therefore arrive before this ever ran, and a
        /// missing dispatcher would silently deliver no intents at all, with
        /// nothing in the log.
        /// </summary>
        private void EnsureServerObjects()
        {
            if (_dispatcher != null) return;
            _registry = new RoomRegistry();
            _dispatcher = new ServerIntentDispatcher(_registry);
            _dispatcher.RegisterHandlers();

            // Room lifecycle messages are handled here rather than in the
            // dispatcher: they arrive from connections that are in no room
            // yet, so there is nothing to dispatch *to*. Same rule applies
            // though — registered once, for the life of the server.
            NetworkServer.RegisterHandler<CreateRoomIntent>(OnCreateRoom);
            NetworkServer.RegisterHandler<JoinRoomIntent>(OnJoinRoom);
            NetworkServer.RegisterHandler<LeaveRoomIntent>(OnLeaveRoom);
        }

        public override void OnServerConnect(NetworkConnectionToClient conn)
        {
            EnsureServerObjects();
            base.OnServerConnect(conn);
        }

        public override void OnStopServer()
        {
            ResetServer();
            base.OnStopServer();
        }

        /// <summary>
        /// Ends and drops every room. Split from ReturnToMenu because a
        /// headless server (Phase 6.5) recycles rooms with no notion of
        /// "show the menu".
        /// </summary>
        private void ResetServer()
        {
            if (_registry != null)
            {
                // Rooms are no longer NetworkBehaviours, so they get no
                // OnStopServer of their own — the owner has to end them.
                // Without this, stopping the server mid-game would leave
                // parked decisions uncancelled and skip the notice to clients.
                // A no-op for rooms whose game already ended.
                foreach (var room in new List<Room>(_registry.Rooms))
                    room.AbortGame(0, "Сервер остановлен.");
                _registry.Clear();
            }
            _registry = null;
            // Dropped, not cleared: NetworkServer.Shutdown() clears the
            // handler table immediately after OnStopServer, so these delegates
            // go with it and the next server start builds fresh ones.
            _dispatcher = null;
        }

        // ---- Room lifecycle handlers ----

        private void OnCreateRoom(NetworkConnectionToClient conn, CreateRoomIntent msg)
        {
            if (!TryLeaveCurrentRoom(conn)) return;
            if (_registry.RoomCount >= _maxRooms)
            {
                Debug.LogWarning($"[Server] Room creation refused: at capacity ({_maxRooms}).");
                Refuse(conn, RoomJoinFailure.ServerFull);
                return;
            }

            var code = ReserveCode();
            if (code == null)
            {
                Debug.LogError("[Server] Could not allocate a free room code.");
                Refuse(conn, RoomJoinFailure.ServerFull);
                return;
            }

            var name = string.IsNullOrWhiteSpace(msg.RoomName) ? code : msg.RoomName.Trim();
            var room = new Room(code, name, _minPlayers, _maxPlayers, _inGameRuleId, _finalRuleId);
            _registry.Add(room);

            Debug.Log($"[Server] Room '{name}' created with code {code} ({_registry.RoomCount} live).");
            Seat(conn, room, msg.PlayerName);
        }

        /// <summary>
        /// A code for a new room: the fixed testing one if it is set and free,
        /// a random one otherwise.
        ///
        /// The fixed code is a convenience for local runs only — it saves
        /// reading a fresh code off the screen every time. It is stored
        /// normalized so it matches what <see cref="RoomRegistry.TryGetByCode"/>
        /// does to whatever the player types, and it is deliberately allowed
        /// to be a word rather than four letters: only generated codes have to
        /// obey the alphabet. Clearing the field restores random codes with no
        /// other change.
        /// </summary>
        private string ReserveCode()
        {
            var fixedCode = RoomRegistry.Normalize(_fixedRoomCode);
            if (fixedCode.Length > 0 && !_registry.TryGetByCode(fixedCode, out _))
                return fixedCode;
            return _registry.AllocateCode();
        }

        /// <summary>
        /// Clears the way for a create or join: a connection sitting in a room
        /// that has not started yet is simply moved out of it, because that is
        /// plainly what the player meant by pressing the button again. Once
        /// their game is running they are committed, and the request is
        /// refused instead.
        ///
        /// Without this, one stray click left a connection stuck in a room
        /// with no way back — there is no Leave button — and every subsequent
        /// press answered AlreadyInRoom forever.
        /// </summary>
        private bool TryLeaveCurrentRoom(NetworkConnectionToClient conn)
        {
            if (!_registry.TryGetByConnection(conn.connectionId, out var current)) return true;
            if (current.IsGameStarted)
            {
                Refuse(conn, RoomJoinFailure.AlreadyInRoom);
                return false;
            }
            Debug.Log($"[Server] Connection {conn.connectionId} leaving room {current.Code} to join another.");
            ReleaseConnection(conn);
            return true;
        }

        private void OnJoinRoom(NetworkConnectionToClient conn, JoinRoomIntent msg)
        {
            if (!TryLeaveCurrentRoom(conn)) return;
            if (!_registry.TryGetByCode(msg.Code, out var room))
            {
                Refuse(conn, RoomJoinFailure.UnknownCode);
                return;
            }
            Seat(conn, room, msg.PlayerName);
        }

        private void OnLeaveRoom(NetworkConnectionToClient conn, LeaveRoomIntent msg)
        {
            ReleaseConnection(conn);
        }

        /// <summary>
        /// Puts a connection in a room, or tells it why not. The room decides
        /// whether it will have them; this only translates the answer.
        /// </summary>
        private void Seat(NetworkConnectionToClient conn, Room room, string requestedName)
        {
            var result = room.TryAddPlayer(conn, requestedName, out var player);
            if (result != Room.JoinResult.Ok)
            {
                Refuse(conn, result == Room.JoinResult.RoomFull
                    ? RoomJoinFailure.RoomFull
                    : RoomJoinFailure.GameInProgress);
                // A room nobody managed to enter is still worth dropping if
                // that failed join was its only prospect.
                DestroyIfAbandoned(room);
                return;
            }

            _registry.BindConnection(conn.connectionId, room);
            conn.Send(new RoomJoinedEvent
            {
                Code = room.Code,
                RoomName = room.Name,
                IsOwner = player.Id == room.OwnerSeatId,
            });

            Debug.Log($"[Server] {player.Name} took seat {player.Id} in room {room.Code}: {room.PlayerCount}/{_maxPlayers}");

            if (_autoStartWhenFull && room.PlayerCount >= _maxPlayers)
            {
                Debug.Log($"[Server] Auto-starting room {room.Code} (full).");
                room.StartGame();
            }
        }

        private static void Refuse(NetworkConnectionToClient conn, RoomJoinFailure reason)
        {
            conn.Send(new RoomJoinFailedEvent { Reason = (int)reason });
        }

        // ---- Disconnects ----

        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            // Mirror calls this with NetworkServer.localConnection during
            // StopHost() teardown, and by then it can already be null.
            if (conn == null)
            {
                Debug.Log("[Server] OnServerDisconnect with a null connection (host teardown).");
                return;
            }

            ReleaseConnection(conn);
            base.OnServerDisconnect(conn);
        }

        /// <summary>
        /// Takes a connection out of whatever room it was in, applying that
        /// room's departure policy, and disposes of the room if that emptied
        /// it. Safe to call for a connection that was never in a room.
        /// </summary>
        private void ReleaseConnection(NetworkConnectionToClient conn)
        {
            if (_registry == null) return;
            if (!_registry.TryGetByConnection(conn.connectionId, out var room)) return;

            // Anything this connection sends from now on resolves to no room
            // rather than into the game it just left.
            _registry.UnbindConnection(conn.connectionId);
            // The room applies its own departure policy (end the game if one
            // was running); it may raise Finished from inside this call.
            room.RemoveConnection(conn.connectionId);
            DestroyIfAbandoned(room);
        }

        /// <summary>
        /// A room with no live connections is never coming back — nobody can
        /// rejoin a code that only its former players know. Dropping it here
        /// is what keeps a long-lived server's memory flat across many games.
        /// </summary>
        private void DestroyIfAbandoned(Room room)
        {
            if (!room.IsAbandoned) return;
            Debug.Log($"[Server] Room {room.Code} abandoned; destroying ({_registry.RoomCount - 1} left).");
            _registry.Remove(room);
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
            // A dedicated server has no menu to return to and must not tear
            // itself down because one room ended: the other rooms are still
            // playing. Room cleanup is ResetServer's job, and it is already
            // split out for exactly this reason.
            if (IsDedicatedServer)
            {
                Debug.Log("[Server] ReturnToMenu ignored on a dedicated server.");
                return;
            }

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

            // Уходим по собственному решению (кнопка «Выйти», меню по Esc):
            // ReturnToMenu уже работает, и это его же отключение вернулось
            // сюда по кругу.
            if (_returningToMenu) return;

            // Связь оборвалась не по нашей воле: сервер остановлен, хост
            // вышел, сеть отвалилась. Молча перезагрузить сцену — значит
            // выбросить игрока в главное меню без единого слова о том, что
            // случилось. Партия для него кончилась, а конец партии игрок
            // должен увидеть и закрыть сам, как любой другой.
            if (UnGameManager.Instance != null
                && UnGameManager.Instance.HandleConnectionLost())
                return;

            // Показывать нечего — мы в лобби или в меню (не удалось
            // подключиться, сервер отказал). Тогда прежнее поведение.
            ReturnToMenu();
        }
    }
}
