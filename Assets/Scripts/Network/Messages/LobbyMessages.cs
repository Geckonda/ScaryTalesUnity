using Mirror;

namespace Assets.Scripts.Network.Messages
{
    // Room lifecycle wire format (Phase 6.4b).
    //
    // Connecting to the server no longer puts you in a game. It puts you in
    // the lobby, from which you either create a room (and get a code to read
    // out to friends) or join one by code. Everything below is that handshake.

    /// <summary>
    /// Client → server. Make me a new room and put me in it as its owner.
    /// The room name is cosmetic; the server answers with the code that
    /// matters.
    ///
    /// <c>PlayerName</c> is a request, not a fact: the server sanitizes it
    /// and may hand back something else entirely — see <c>Room.SanitizeName</c>.
    /// Empty is fine and means "call me whatever".
    /// </summary>
    public struct CreateRoomIntent : NetworkMessage
    {
        public string RoomName;
        public string PlayerName;
    }

    /// <summary>
    /// Client → server. Put me in the room with this code. Case and stray
    /// spaces or dashes don't matter — the server normalizes before looking
    /// it up. <c>PlayerName</c> behaves as in <see cref="CreateRoomIntent"/>.
    /// </summary>
    public struct JoinRoomIntent : NetworkMessage
    {
        public string Code;
        public string PlayerName;
    }

    /// <summary>Client → server. Take me out of whatever room I'm in.</summary>
    public struct LeaveRoomIntent : NetworkMessage { }

    /// <summary>
    /// Client → server. Begin the game. Only the room's owner is obeyed.
    ///
    /// Replaces the host's direct call to StartGameNow: on a dedicated server
    /// there is no host player, so the moment of starting has to travel over
    /// the wire like anything else.
    /// </summary>
    public struct StartGameIntent : NetworkMessage { }

    /// <summary>
    /// Client → server. Занять место за столом с этим номером.
    ///
    /// <para>Номер места здесь — это <b>позиция в очереди ходов</b>, а не
    /// <see cref="Player.Id"/>. Их нельзя путать: id выдаётся при входе и
    /// служит личностью игрока (привязка соединения, авторизация интентов,
    /// владелец комнаты, задел под переподключение), а позиция — то, что
    /// игрок выбирает сам и что определяет лишь порядок ходов.</para>
    ///
    /// <para>Повторный интент переносит игрока на другое место, если оно
    /// свободно.</para>
    /// </summary>
    public struct ClaimChairIntent : NetworkMessage
    {
        public int Chair; // 0..MaxPlayers-1
    }

    /// <summary>
    /// Server → one client. You are in. Carries the code so the client can
    /// display it for reading out, and whether this client is the owner —
    /// which is what now decides who sees the Start button, since
    /// <c>NetworkServer.active</c> stops meaning "host" once the server is
    /// a separate process.
    /// </summary>
    public struct RoomJoinedEvent : NetworkMessage
    {
        public string Code;
        public string RoomName;
        public bool IsOwner;

        /// <summary>
        /// Место этого игрока в комнате — его личность на весь срок партии.
        /// Клиенту нужно, чтобы узнавать себя в списке мест: сравнивать по
        /// имени нельзя, имена могут совпасть.
        /// </summary>
        public int SeatId;
    }

    /// <summary>Why a create or join was refused.</summary>
    public enum RoomJoinFailure
    {
        UnknownCode = 0,
        RoomFull = 1,
        GameInProgress = 2,
        AlreadyInRoom = 3,
        ServerFull = 4,
    }

    /// <summary>
    /// Server → one client. The create or join did not happen. Reason is a
    /// <see cref="RoomJoinFailure"/>; the client turns it into text so the
    /// wording stays a client concern.
    /// </summary>
    public struct RoomJoinFailedEvent : NetworkMessage
    {
        public int Reason;
    }

    /// <summary>
    /// Server → every client in one room. Sent whenever that room's roster
    /// changes, so clients can render the player list without access to the
    /// server's canonical roster.
    /// </summary>
    public struct LobbyStateUpdate : NetworkMessage
    {
        public int PlayerCount;
        public int MinPlayers;
        public int MaxPlayers;
        public string[] PlayerNames;
        // Whether the room would accept a StartGameIntent right now. Sent
        // rather than recomputed on the client so the min-player rule lives
        // in exactly one place.
        public bool CanStart;

        // Кто где сидит. Обе длиной MaxPlayers и индексируются НОМЕРОМ
        // МЕСТА — так клиенту не приходится сопоставлять два списка, чтобы
        // нарисовать ряд стульев.
        //
        // Свободное место: seat 0 (0 — сентинел «никого», реальные id
        // начинаются с 1) и пустое имя.
        public int[] ChairSeats;
        public string[] ChairNames;
    }
}
