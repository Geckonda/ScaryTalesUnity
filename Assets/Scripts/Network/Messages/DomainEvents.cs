using Mirror;

namespace Assets.Scripts.Network.Messages
{
    // Phase 3 wire format (server → clients).
    //
    // DomainEvents express *what happened* on the server's canonical state.
    // Clients render from this stream — they no longer run the engine.
    //
    // Mirror auto-serializes structs of primitives + supported types.
    // Keep payloads minimal: ids, not whole objects. Clients resolve ids
    // against their local snapshot (built up from prior events).

    /// <summary>
    /// Identity for a player visible on a client. Sent in GameStartedEvent so
    /// each client knows who is at the table.
    /// </summary>
    public struct PlayerInfo
    {
        public int Id;
        public string Name;
    }

    /// <summary>
    /// One-shot at game start. Carries the deck order so every client
    /// renders face-down cards in the same sequence; identities are not
    /// revealed until the corresponding CardDrawnEvent.
    ///
    /// Sent per-client (not via SendToAll): each recipient gets the same
    /// shared payload but with LocalPlayerId set to *their* id, since each
    /// client needs to know which seat is theirs.
    /// </summary>
    public struct GameStartedEvent : NetworkMessage
    {
        public PlayerInfo[] Players;
        public int[] DeckOrder;
        public int StartPlayerId;
        public int LocalPlayerId;     // per-client: which Players[i] is "me"
        public int CurrentRuleId;     // rule template id (host's pick)
        public int CurrentFinalRuleId;
    }

    /// <summary>
    /// A player drew a card from the deck. Card identity is included so the
    /// owning player's client can show the face; other clients render a
    /// face-down card with this id (used later for animations).
    /// </summary>
    public struct CardDrawnEvent : NetworkMessage
    {
        public int PlayerId;
        public int CardId;
    }

    /// <summary>
    /// A player drew the top of the discard pile back into hand
    /// (REfA11 path).
    /// </summary>
    public struct CardAddedToHandFromDiscardEvent : NetworkMessage
    {
        public int PlayerId;
        public int CardId;
    }

    /// <summary>
    /// The active player played a card from their hand. The card has left
    /// the hand but its destination depends on its type — clients should
    /// expect a follow-up CardMovedTo* event.
    /// </summary>
    public struct CardPlayedEvent : NetworkMessage
    {
        public int PlayerId;
        public int CardId;
    }

    public struct CardMovedToBoardEvent : NetworkMessage
    {
        public int CardId;
    }

    public struct CardMovedToBeforePlayerEvent : NetworkMessage
    {
        public int CardId;
        public int OwnerId;
    }

    public struct CardMovedToTimeOfDaySlotEvent : NetworkMessage
    {
        public int CardId;
    }

    public struct CardMovedToDiscardPileEvent : NetworkMessage
    {
        public int CardId;
    }

    /// <summary>
    /// A player gained an item from the supply.
    /// </summary>
    public struct ItemAddedToPlayerEvent : NetworkMessage
    {
        public int PlayerId;
        public int ItemId;
        public int ItemType; // ScaryTales.Enums.ItemType cast to int
    }

    /// <summary>
    /// A player lost an item from their bag (rule effects spend items
    /// like Armor / Sword / MagicStick to trigger their actions).
    /// </summary>
    public struct ItemRemovedFromPlayerEvent : NetworkMessage
    {
        public int PlayerId;
        public int ItemId;
        public int ItemType; // ScaryTales.Enums.ItemType cast to int
    }

    /// <summary>
    /// A player's score changed. Send the new total, not a delta — easier
    /// for clients to render and resilient against missed events.
    /// </summary>
    public struct PointsAwardedEvent : NetworkMessage
    {
        public int PlayerId;
        public int NewScore;
    }

    /// <summary>
    /// Server-side log line worth surfacing to players (the existing
    /// PrintMessage flow).
    /// </summary>
    public struct MessagePrintedEvent : NetworkMessage
    {
        public string Message;
    }

    /// <summary>
    /// The turn advanced. CurrentPlayerId is the player who is now active.
    /// </summary>
    public struct TurnAdvancedEvent : NetworkMessage
    {
        public int CurrentPlayerId;
        public int TurnCount;
        public bool IsNight;
    }

    /// <summary>
    /// Day/Night flipped without a turn boundary (Day/Night card effects).
    /// </summary>
    public struct PhaseChangedEvent : NetworkMessage
    {
        public bool IsNight;
    }

    /// <summary>
    /// Server is asking a specific player to make a decision. The active
    /// player's client shows UI; others render "waiting on Alice…" by
    /// looking up PlayerId.
    /// </summary>
    public struct DecisionRequestedEvent : NetworkMessage
    {
        public int RequestId;
        public int PlayerId;
        public int Kind;             // DecisionKind enum cast to int
        public int[] CandidateIds;   // card ids, item types, or rule effect ids
        public string Prompt;        // optional, used by Confirm
    }

    /// <summary>
    /// Server has received and applied a decision; clients can dismiss any
    /// "waiting" UI for that requestId.
    /// </summary>
    public struct DecisionResolvedEvent : NetworkMessage
    {
        public int RequestId;
    }

    /// <summary>
    /// Indexes the kinds of DecisionRequestedEvent so clients know which
    /// candidate-id namespace they're in (cards vs items vs rule effects).
    /// Keeps wire compatibility independent of the C# DecisionRequest
    /// hierarchy in core.
    /// </summary>
    public enum DecisionKind
    {
        PickCard = 0,
        PickItem = 1,
        PickRuleEffect = 2,
        Confirm = 3,
    }

    /// <summary>
    /// Game-over signal. Scores are sent in player-id order matching
    /// GameStartedEvent.Players.
    /// </summary>
    public struct GameEndedEvent : NetworkMessage
    {
        public int WinnerId;
        public int[] FinalScores; // parallel to GameStartedEvent.Players
    }

    /// <summary>
    /// The game was torn down before it could finish. Today the only cause
    /// is a player leaving mid-game (Phase 6.1) — the server cancels their
    /// pending decisions, stops the turn loop, and tells everyone else why.
    ///
    /// Deliberately *not* a GameEndedEvent with a synthetic winner: an
    /// aborted game has no winner and no meaningful final scores, and
    /// clients must not render a podium for one.
    /// </summary>
    public struct GameAbortedEvent : NetworkMessage
    {
        public int LeftPlayerId; // whose departure ended it, or 0 if not player-caused
        public string Reason;    // display text, already localized by the server
    }
}
