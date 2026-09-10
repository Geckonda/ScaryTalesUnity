using ScaryTales.Lobby;
using System;
using System.Collections.Generic;

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

        // Алфавит, длина кода и правила приведения живут в
        // ScaryTales.Lobby.RoomCode: это решения про то, как один человек
        // диктует код другому, и проверять их надо без поднятого сервера.
        // Здесь остаётся только то, что знает про живые комнаты.
        private readonly Random _random = new();

        /// <summary>Подбирает код, которым не занята ни одна живая комната.</summary>
        public string AllocateCode() => RoomCode.Allocate(_byCode.ContainsKey, _random);

        /// <inheritdoc cref="RoomCode.Normalize"/>
        public static string Normalize(string raw) => RoomCode.Normalize(raw);

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
