using Mirror;

namespace Assets.Scripts.Network.Messages
{
    // Phase 3 wire format (client → server).
    //
    // Intents express a player's *request* to do something. The server
    // validates against canonical state; if valid, the engine reacts and
    // emits DomainEvents back to all clients.
    //
    // One Mirror message struct per intent kind. Decision-resolution intents
    // are split per resolution shape so each carries exactly the fields it
    // needs (Mirror generates serializers for plain structs).

    /// <summary>
    /// "I want to play this card." Sent by the active player's client when
    /// they finish a drag-and-drop. Server validates: is it their turn, is
    /// the card in their hand, is the game running.
    /// </summary>
    public struct PlayCardIntent : NetworkMessage
    {
        public int CardId;
    }

    /// <summary>
    /// "Here is my answer to a PickCard decision the server asked for."
    /// RequestId matches the DecisionRequestedEvent the server broadcast.
    /// </summary>
    public struct ResolveCardPickIntent : NetworkMessage
    {
        public int RequestId;
        public int CardId;
    }

    /// <summary>
    /// "Here is my answer to a PickItem decision."
    /// </summary>
    public struct ResolveItemPickIntent : NetworkMessage
    {
        public int RequestId;
        public int ItemType; // ScaryTales.Enums.ItemType cast to int
    }

    /// <summary>
    /// "Here is my answer to a PickRuleEffect decision."
    /// HasPick=false means the player skipped (legacy null-IRuleEffect path).
    /// </summary>
    public struct ResolveRuleEffectPickIntent : NetworkMessage
    {
        public int RequestId;
        public bool HasPick;
        public int RuleEffectId;
    }

    /// <summary>
    /// "Here is my answer to a Confirm (yes/no) decision."
    /// </summary>
    public struct ResolveConfirmIntent : NetworkMessage
    {
        public int RequestId;
        public bool Confirmed;
    }

    /// <summary>
    /// "I want to apply this rule effect this turn." Sent by the active
    /// player when they choose a rule effect from the rule UI. Server
    /// validates (it's their turn, they haven't used the rule yet,
    /// IsEffectAvailable returns true) and runs the effect. Skipping the
    /// rule is implicit — the client just doesn't send anything.
    /// </summary>
    public struct UseRuleEffectIntent : NetworkMessage
    {
        public int RuleEffectId;
    }
}
