using Assets.Scripts;
using Assets.Scripts.Network.Messages;
using ScaryTales;

namespace Assets.Scripts.Network
{
    /// <summary>
    /// Server-only. Subscribes to one GameSession's GameManager events and
    /// re-emits each as a wire DomainEvent, delivered to that session's room
    /// and nowhere else. Clients consume them via ClientGameView and update
    /// their local mirror.
    ///
    /// Phase 6.2: every send goes through the RoomChannel this broadcaster
    /// was built with. There is deliberately no way to reach a wider
    /// audience from here.
    /// </summary>
    public class ServerEventBroadcaster
    {
        private readonly GameSession _session;
        private readonly RoomChannel _channel;

        public ServerEventBroadcaster(GameSession session, RoomChannel channel)
        {
            _session = session;
            _channel = channel;
            var gm = session.GameManager;
            gm.OnCardAddedToHand += HandleCardAddedToHand;
            gm.OnCardAddedToHandFromDiscardPile += HandleCardAddedToHandFromDiscard;
            gm.OnCardPlayed += HandleCardPlayed;
            gm.OnCardMovedToBoard += HandleCardMovedToBoard;
            gm.OnCardMovedToBeforePlayer += HandleCardMovedToBeforePlayer;
            gm.OnCardMovedToTimeOfDaySlot += HandleCardMovedToTimeOfDay;
            gm.OnCardMovedToDiscardPile += HandleCardMovedToDiscard;
            gm.OnItemAddToPlayer += HandleItemAdded;
            gm.OnItemRemovedFromPlayer += HandleItemRemoved;
            gm.OnAddPointsToPlayer += HandlePointsAwarded;
            gm.OnMessagePrinted += HandleMessagePrinted;
            gm.OnDeckCountChanged += HandleDeckCountChanged;
        }

        private void HandleDeckCountChanged(int remaining)
        {
            _channel.SendToRoom(new DeckCountChangedEvent { Remaining = remaining });
        }

        private void HandleCardAddedToHand(Card card, Player player)
        {
            _channel.SendToRoom(new CardDrawnEvent
            {
                PlayerId = player.Id,
                CardId = card.Id,
            });
        }

        private void HandleCardAddedToHandFromDiscard(Card card, Player player)
        {
            _channel.SendToRoom(new CardAddedToHandFromDiscardEvent
            {
                PlayerId = player.Id,
                CardId = card.Id,
            });
        }

        private void HandleCardPlayed(Card card)
        {
            // Owner may already be cleared by the time this fires depending
            // on play order; fall back to current player.
            var ownerId = card.Owner?.Id ?? _session.CurrentPlayer?.Id ?? 0;
            _channel.SendToRoom(new CardPlayedEvent
            {
                PlayerId = ownerId,
                CardId = card.Id,
            });
        }

        private void HandleCardMovedToBoard(Card card)
        {
            _channel.SendToRoom(new CardMovedToBoardEvent { CardId = card.Id });
        }

        private void HandleCardMovedToBeforePlayer(Card card)
        {
            _channel.SendToRoom(new CardMovedToBeforePlayerEvent
            {
                CardId = card.Id,
                OwnerId = card.Owner?.Id ?? 0,
            });
        }

        private void HandleCardMovedToTimeOfDay(Card card)
        {
            _channel.SendToRoom(new CardMovedToTimeOfDaySlotEvent { CardId = card.Id });
        }

        private void HandleCardMovedToDiscard(Card card)
        {
            _channel.SendToRoom(new CardMovedToDiscardPileEvent { CardId = card.Id });
        }

        private void HandleItemAdded(Item item, Player player)
        {
            _channel.SendToRoom(new ItemAddedToPlayerEvent
            {
                PlayerId = player.Id,
                ItemId = item.Id,
                ItemType = (int)item.Type,
            });
        }

        private void HandleItemRemoved(Item item, Player player)
        {
            _channel.SendToRoom(new ItemRemovedFromPlayerEvent
            {
                PlayerId = player.Id,
                ItemId = item.Id,
                ItemType = (int)item.Type,
            });
        }

        private void HandlePointsAwarded(Player player)
        {
            _channel.SendToRoom(new PointsAwardedEvent
            {
                PlayerId = player.Id,
                NewScore = player.Score,
            });
        }

        private void HandleMessagePrinted(string message)
        {
            _channel.SendToRoom(new MessagePrintedEvent { Message = message });
        }
    }
}
