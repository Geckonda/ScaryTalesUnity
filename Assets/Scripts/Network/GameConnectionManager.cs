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

        private RoomRegistry _registry;
        private ServerIntentDispatcher _dispatcher;

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
            if (_registry.TryGetByConnection(conn.connectionId, out _))
            {
                Refuse(conn, RoomJoinFailure.AlreadyInRoom);
                return;
            }
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
            Seat(conn, room);
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

        private void OnJoinRoom(NetworkConnectionToClient conn, JoinRoomIntent msg)
        {
            if (_registry.TryGetByConnection(conn.connectionId, out _))
            {
                Refuse(conn, RoomJoinFailure.AlreadyInRoom);
                return;
            }
            if (!_registry.TryGetByCode(msg.Code, out var room))
            {
                Refuse(conn, RoomJoinFailure.UnknownCode);
                return;
            }
            Seat(conn, room);
        }

        private void OnLeaveRoom(NetworkConnectionToClient conn, LeaveRoomIntent msg)
        {
            ReleaseConnection(conn);
        }

        /// <summary>
        /// Puts a connection in a room, or tells it why not. The room decides
        /// whether it will have them; this only translates the answer.
        /// </summary>
        private void Seat(NetworkConnectionToClient conn, Room room)
        {
            var result = room.TryAddPlayer(conn, out var player);
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
            // Disconnect mid-game (server quit, network failure, kicked):
            // reset to the menu state. Per Phase-3 non-goals there's no
            // reconnect flow — players just bounce back to the menu and
            // start over.
            ReturnToMenu();
        }
    }
}
