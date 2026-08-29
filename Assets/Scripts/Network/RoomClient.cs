using Assets.Scripts.Network.Messages;
using Mirror;
using System;

namespace Assets.Scripts.Network
{
    /// <summary>
    /// The client's single set of room-lifecycle handlers, fanned out to
    /// whoever is interested.
    ///
    /// <para><b>Why this exists:</b> Mirror keeps one handler per message type
    /// on the client exactly as it does on the server. Two components each
    /// calling <c>NetworkClient.RegisterHandler&lt;RoomJoinedEvent&gt;</c> means
    /// the second silently replaces the first, and one of them goes deaf with
    /// no error. That is the same trap <see cref="ServerIntentDispatcher"/>
    /// exists to close on the server side; this is its client twin.</para>
    ///
    /// <para>Register once here, subscribe to the C# events instead.</para>
    /// </summary>
    public static class RoomClient
    {
        /// <summary>You are in a room. Carries its code and whether you own it.</summary>
        public static event Action<RoomJoinedEvent> Joined;

        /// <summary>The create or join was refused, with the reason why.</summary>
        public static event Action<RoomJoinFailure> JoinFailed;

        /// <summary>Your room's roster changed.</summary>
        public static event Action<LobbyStateUpdate> LobbyStateChanged;

        /// <summary>
        /// (Re)claims the three handlers. Safe and cheap to call from every
        /// interested component's Awake — <c>ReplaceHandler</c> is idempotent
        /// and, unlike RegisterHandler, does not warn about replacing.
        ///
        /// It must be callable more than once, and per scene load: Mirror's
        /// <c>NetworkClient.Shutdown()</c> clears the whole handler table, and
        /// that runs on every <c>StopClient()</c> — so a guard that only ever
        /// registered once would leave the client deaf after the first trip
        /// back to the menu.
        /// </summary>
        public static void Bind()
        {
            // requireAuthentication: false — these arrive during the join
            // handshake, before anything has marked the connection
            // authenticated, and a dropped one leaves the player staring at a
            // button that appears to do nothing.
            NetworkClient.ReplaceHandler<RoomJoinedEvent>(m => Joined?.Invoke(m), false);
            NetworkClient.ReplaceHandler<RoomJoinFailedEvent>(m => JoinFailed?.Invoke((RoomJoinFailure)m.Reason), false);
            NetworkClient.ReplaceHandler<LobbyStateUpdate>(m => LobbyStateChanged?.Invoke(m), false);
        }
    }
}
