using System;
using System.Collections.Generic;
using System.Text;

namespace Assets.Scripts.Network
{
    /// <summary>
    /// Every room this server is holding, indexed both ways: by the code
    /// players type, and by the connection that is sitting in one (Phase
    /// 6.4b).
    ///
    /// <para>The connection index is the routing table
    /// <see cref="ServerIntentDispatcher"/> reads to decide which room an
    /// arriving intent belongs to. It lives here rather than in the
    /// dispatcher so there is exactly one answer to "where is this player",
    /// and no chance of the two drifting apart.</para>
    /// </summary>
    public class RoomRegistry
    {
        // Codes are compared case-insensitively so a player can type what
        // they heard without worrying about capitals.
        private readonly Dictionary<string, Room> _byCode = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, Room> _byConnection = new();

        public int RoomCount => _byCode.Count;
        public IEnumerable<Room> Rooms => _byCode.Values;

        // ---- Codes ----

        /// <summary>
        /// Letters only, and missing I, L and O.
        ///
        /// Letters only because the codes are meant to be read aloud to a
        /// friend, and mixing digits in brings the whole S/5, Z/2, B/8 family
        /// of mishearings with it. I, L and O are dropped because they are the
        /// ones people mistype as 1 and 0 even when reading from a screen.
        /// What is left is 23 unambiguous characters.
        /// </summary>
        private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ";
        private const int CodeLength = 4; // 23^4 ≈ 280k — ample for one server

        private readonly Random _random = new();

        /// <summary>
        /// Picks a code no live room is using. Retries on collision, then
        /// gives up rather than looping forever — a server that cannot find a
        /// free code in this many tries is holding an implausible number of
        /// rooms, and failing loudly beats hanging.
        /// </summary>
        public string AllocateCode()
        {
            const int maxAttempts = 100;
            var buffer = new StringBuilder(CodeLength);
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                buffer.Clear();
                for (int i = 0; i < CodeLength; i++)
                    buffer.Append(Alphabet[_random.Next(Alphabet.Length)]);

                var candidate = buffer.ToString();
                if (!_byCode.ContainsKey(candidate)) return candidate;
            }
            return null;
        }

        /// <summary>
        /// Turns what a player typed into something comparable with a stored
        /// code: upper case, with the spaces and dashes people add when
        /// reading a code out loud removed.
        ///
        /// Characters outside the alphabet are deliberately *left in* rather
        /// than stripped — a code containing them simply won't match, and
        /// "unknown code" is a truer answer than silently deleting what the
        /// player typed and matching some other room.
        /// </summary>
        public static string Normalize(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;
            var buffer = new StringBuilder(raw.Length);
            foreach (var c in raw)
            {
                if (char.IsWhiteSpace(c) || c == '-' || c == '_') continue;
                buffer.Append(char.ToUpperInvariant(c));
            }
            return buffer.ToString();
        }

        // ---- Rooms ----

        public void Add(Room room) => _byCode[room.Code] = room;

        public bool TryGetByCode(string rawCode, out Room room)
        {
            room = null;
            var code = Normalize(rawCode);
            return code.Length != 0 && _byCode.TryGetValue(code, out room);
        }

        /// <summary>Removes a room and every connection pointing at it.</summary>
        public void Remove(Room room)
        {
            if (room == null) return;
            _byCode.Remove(room.Code);

            var doomed = new List<int>();
            foreach (var kv in _byConnection)
                if (kv.Value == room) doomed.Add(kv.Key);
            foreach (var connectionId in doomed)
                _byConnection.Remove(connectionId);
        }

        // ---- Connections ----

        public void BindConnection(int connectionId, Room room) => _byConnection[connectionId] = room;

        public void UnbindConnection(int connectionId) => _byConnection.Remove(connectionId);

        public bool TryGetByConnection(int connectionId, out Room room) =>
            _byConnection.TryGetValue(connectionId, out room);

        public void Clear()
        {
            _byCode.Clear();
            _byConnection.Clear();
        }
    }
}
