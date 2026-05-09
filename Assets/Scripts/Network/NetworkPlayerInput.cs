using Mirror;

namespace Assets.Scripts.Network
{
    /// <summary>
    /// Empty per-connection NetworkBehaviour. Mirror's NetworkManager
    /// spawns a player object per connection; this is what's attached to
    /// that prefab. After Phase 3 it carries no game state — input now
    /// flows via NetworkClient.Send(...intent...) and DomainEvents
    /// arriving back via ClientGameView.
    ///
    /// Kept (rather than deleted) so the existing Player prefab in the
    /// scene continues to resolve. Phase 5 follow-up: rename to
    /// PlayerConnection or similar, or replace with NetworkIdentity-only
    /// prefab and delete this entirely.
    /// </summary>
    public class NetworkPlayerInput : NetworkBehaviour
    {
    }
}
