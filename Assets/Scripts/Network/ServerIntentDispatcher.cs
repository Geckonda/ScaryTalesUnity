using Assets.Scripts.Network.Messages;
using Mirror;
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
    /// A finished room leaves the registry's connection index; the
    /// handlers stay up for everyone else. Handlers are cleared wholesale by
    /// <c>NetworkServer.Shutdown()</c>, which is why registration belongs to
    /// server start rather than to any one game.</para>
    /// </summary>
    public class ServerIntentDispatcher
    {
        // Where "which room is this connection in" is answered. Owned by the
        // registry rather than duplicated here, so the routing table and the
        // room list cannot drift apart.
        private readonly RoomRegistry _registry;

        public ServerIntentDispatcher(RoomRegistry registry)
        {
            _registry = registry;
        }

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
            NetworkServer.RegisterHandler<StartGameIntent>(OnStartGame);
            NetworkServer.RegisterHandler<ClaimChairIntent>(OnClaimChair);
        }

        // ---- Dispatch ----

        private bool TryResolveRoom(NetworkConnectionToClient conn, out Room room)
        {
            room = null;
            if (conn == null) return false;
            if (!_registry.TryGetByConnection(conn.connectionId, out room) || room == null)
            {
                // Normal enough to be a warning rather than an error: a
                // client can send an intent it queued just as its room ended.
                Debug.LogWarning($"[IntentDispatcher] Intent from connection {conn.connectionId}, which belongs to no active room.");
                return false;
            }
            return true;
        }

        private void OnStartGame(NetworkConnectionToClient conn, StartGameIntent msg)
        {
            if (TryResolveRoom(conn, out var room)) room.HandleStartGame(conn);
        }

        private void OnClaimChair(NetworkConnectionToClient conn, ClaimChairIntent msg)
        {
            if (TryResolveRoom(conn, out var room)) room.HandleClaimChair(conn, msg);
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
