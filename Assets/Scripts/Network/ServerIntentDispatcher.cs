using Assets.Scripts.Network.Messages;
using Mirror;
using System.Collections.Generic;
using UnityEngine;

namespace Assets.Scripts.Network
{
    /// <summary>
    /// The server's single set of intent handlers, dispatched by room
    /// (Phase 6.3).
    ///
    /// <para><b>The problem this solves:</b> Mirror keeps exactly one handler
    /// per message type, process-wide. Registration used to happen per game —
    /// the old <c>GameNetworkController.InitializeGame</c> claimed
    /// `PlayCardIntent` and `UseRuleEffectIntent`, and the
    /// <c>NetworkDecisionRouter</c> constructor claimed the four
    /// `Resolve*Intent`s. With one room that is
    /// invisible. With two, the second room's registration silently replaces
    /// the first, and the first room stops receiving anything its players
    /// send — no error, no warning, just a dead room.</para>
    ///
    /// <para><b>The shape:</b> register each type exactly once, when the
    /// server starts. Every handler resolves the sender's room from a
    /// connection index and dispatches into it. The per-room authorization
    /// that already exists (<c>RoomChannel.IsSeatedAt</c>) stays where it is —
    /// this layer answers "which room?", not "may you?".</para>
    ///
    /// <para>Rooms must therefore <b>not</b> unregister handlers when they end.
    /// A finished room leaves the index (<see cref="UnbindRoom"/>); the
    /// handlers stay up for everyone else. Handlers are cleared wholesale by
    /// <c>NetworkServer.Shutdown()</c>, which is why registration belongs to
    /// server start rather than to any one game.</para>
    /// </summary>
    public class ServerIntentDispatcher
    {
        // Mirror's connection id → the room that connection is playing in.
        // Not seat id: an arriving message only carries its connection.
        private readonly Dictionary<int, Room> _roomByConnection = new();

        public int BoundConnectionCount => _roomByConnection.Count;

        /// <summary>
        /// Claims every intent message type. Call once per server start —
        /// NetworkServer.Shutdown() clears the handler table, and
        /// NetworkManager.OnStartServer runs after Mirror has registered its
        /// own, so this is the right moment.
        /// </summary>
        public void RegisterHandlers()
        {
            NetworkServer.RegisterHandler<PlayCardIntent>(OnPlayCard);
            NetworkServer.RegisterHandler<UseRuleEffectIntent>(OnUseRuleEffect);
            NetworkServer.RegisterHandler<ResolveCardPickIntent>(OnResolveCardPick);
            NetworkServer.RegisterHandler<ResolveItemPickIntent>(OnResolveItemPick);
            NetworkServer.RegisterHandler<ResolveRuleEffectPickIntent>(OnResolveRuleEffectPick);
            NetworkServer.RegisterHandler<ResolveConfirmIntent>(OnResolveConfirm);
        }

        // ---- Index maintenance ----

        /// <summary>
        /// Points a connection at the room it just joined. Bound at join
        /// rather than at game start: the room exists from lobby time now, so
        /// there is no window where a connection has a room but the index
        /// does not know it.
        /// </summary>
        public void Bind(int connectionId, Room room) => _roomByConnection[connectionId] = room;

        public void Unbind(int connectionId) => _roomByConnection.Remove(connectionId);

        /// <summary>
        /// Drops every connection pointing at a finished room, so its former
        /// players' late intents resolve to nothing instead of poking a dead
        /// session.
        /// </summary>
        public void UnbindRoom(Room room)
        {
            var doomed = new List<int>();
            foreach (var kv in _roomByConnection)
                if (kv.Value == room) doomed.Add(kv.Key);
            foreach (var connectionId in doomed)
                _roomByConnection.Remove(connectionId);
        }

        public void Clear() => _roomByConnection.Clear();

        // ---- Dispatch ----

        private bool TryResolveRoom(NetworkConnectionToClient conn, out Room room)
        {
            room = null;
            if (conn == null) return false;
            if (!_roomByConnection.TryGetValue(conn.connectionId, out room) || room == null)
            {
                // Normal enough to be a warning rather than an error: a
                // client can send an intent it queued just as its room ended.
                Debug.LogWarning($"[IntentDispatcher] Intent from connection {conn.connectionId}, which belongs to no active room.");
                return false;
            }
            return true;
        }

        private void OnPlayCard(NetworkConnectionToClient conn, PlayCardIntent msg)
        {
            if (TryResolveRoom(conn, out var room)) room.HandlePlayCard(conn, msg);
        }

        private void OnUseRuleEffect(NetworkConnectionToClient conn, UseRuleEffectIntent msg)
        {
            if (TryResolveRoom(conn, out var room)) room.HandleUseRuleEffect(conn, msg);
        }

        private void OnResolveCardPick(NetworkConnectionToClient conn, ResolveCardPickIntent msg)
        {
            if (TryResolveRoom(conn, out var room)) room.Router?.OnResolveCardPick(conn, msg);
        }

        private void OnResolveItemPick(NetworkConnectionToClient conn, ResolveItemPickIntent msg)
        {
            if (TryResolveRoom(conn, out var room)) room.Router?.OnResolveItemPick(conn, msg);
        }

        private void OnResolveRuleEffectPick(NetworkConnectionToClient conn, ResolveRuleEffectPickIntent msg)
        {
            if (TryResolveRoom(conn, out var room)) room.Router?.OnResolveRuleEffectPick(conn, msg);
        }

        private void OnResolveConfirm(NetworkConnectionToClient conn, ResolveConfirmIntent msg)
        {
            if (TryResolveRoom(conn, out var room)) room.Router?.OnResolveConfirm(conn, msg);
        }
    }
}
