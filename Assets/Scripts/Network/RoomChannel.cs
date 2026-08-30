using Mirror;
using System.Collections.Generic;
using UnityEngine;

namespace Assets.Scripts.Network
{
    /// <summary>
    /// One room's membership and its outbound mail slot (Phase 6.2).
    ///
    /// Everything the server sends about a game now goes through here
    /// instead of <c>NetworkServer.SendToAll</c>, which would blast every
    /// room's events at every connected player the moment a second room
    /// exists. The rule is <b>room-scoped by construction</b>: if you hold a
    /// RoomChannel you can only reach that room, so a new send site cannot
    /// leak across rooms by forgetting something.
    ///
    /// It also owns the seat→connection binding introduced in 6.1, which
    /// makes it the single answer to "who is in this room, and where do I
    /// reach them". Phase 6.4's <c>Room</c> is this plus the session, the
    /// router, the broadcaster and the turn loop.
    /// </summary>
    public class RoomChannel
    {
        // Seat id → the connection currently sitting in it. A seat with no
        // live connection is simply absent (see Unbind).
        private readonly Dictionary<int, NetworkConnectionToClient> _seats = new();

        public IReadOnlyDictionary<int, NetworkConnectionToClient> Seats => _seats;
        public int Count => _seats.Count;

        // ---- Membership ----

        public void Bind(int seatId, NetworkConnectionToClient conn) => _seats[seatId] = conn;

        public bool Unbind(int seatId) => _seats.Remove(seatId);

        public bool TryGetConnection(int seatId, out NetworkConnectionToClient conn) =>
            _seats.TryGetValue(seatId, out conn);

        /// <summary>
        /// True when <paramref name="conn"/> is exactly the connection bound
        /// to <paramref name="seatId"/>. This is the authorization primitive
        /// behind "is this intent really from the player it claims to be" —
        /// used by the turn loop's PlayCardIntent check and by the decision
        /// router. Named rather than inlined because getting it subtly wrong
        /// is how a client plays out of turn.
        /// </summary>
        public bool IsSeatedAt(int seatId, NetworkConnectionToClient conn) =>
            conn != null && _seats.TryGetValue(seatId, out var seated) && seated == conn;

        public void Clear() => _seats.Clear();

        // ---- Delivery ----

        /// <summary>
        /// Sends to every seat in this room and nowhere else.
        ///
        /// Note this packs the message once per recipient, where
        /// NetworkServer.SendToAll packed once and reused the segment — its
        /// fast path goes through an <c>internal</c> overload we cannot
        /// reach from this assembly. At 2-4 seats and messages made of a
        /// handful of ints that is not worth caring about, and it is not a
        /// reason to reach back for SendToAll.
        /// </summary>
        public void SendToRoom<T>(T message) where T : struct, NetworkMessage
        {
            if (!NetworkServer.active)
            {
                Debug.LogWarning($"[RoomChannel] Dropped {typeof(T).Name}: server is not active.");
                return;
            }
            foreach (var conn in _seats.Values)
                conn?.Send(message);
        }

        /// <summary>
        /// Sends to one seat. Returns false if that seat has no live
        /// connection — callers that consider this an error (the
        /// GameStartedEvent fan-out, say) can log it.
        /// </summary>
        public bool SendToSeat<T>(int seatId, T message) where T : struct, NetworkMessage
        {
            if (!NetworkServer.active)
            {
                Debug.LogWarning($"[RoomChannel] Dropped {typeof(T).Name} to seat {seatId}: server is not active.");
                return false;
            }
            if (!_seats.TryGetValue(seatId, out var conn) || conn == null)
                return false;
            conn.Send(message);
            return true;
        }
    }
}
