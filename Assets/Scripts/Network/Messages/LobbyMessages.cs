using Mirror;

namespace Assets.Scripts.Network.Messages
{
    /// <summary>
    /// Server → all clients. Sent every time the lobby roster changes
    /// (player joins, player leaves) so non-host clients can render the
    /// player list and count without having access to the server's
    /// canonical roster.
    /// </summary>
    public struct LobbyStateUpdate : NetworkMessage
    {
        public int PlayerCount;
        public int MinPlayers;
        public int MaxPlayers;
        public string[] PlayerNames;
    }
}
