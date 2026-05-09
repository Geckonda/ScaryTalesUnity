using Assets.Scripts;
using Assets.Scripts.Network.Messages;
using Mirror;
using ScaryTales;

namespace Assets.Scripts.Network
{
    /// <summary>
    /// Server-only. Subscribes to the canonical GameSession's GameManager
    /// events and re-emits each as a wire DomainEvent broadcast to every
    /// connected client. Clients consume them via ClientGameView and update
    /// their local mirror.
    /// </summary>
    public class ServerEventBroadcaster
    {
        private readonly GameSession _session;

        public ServerEventBroadcaster(GameSession session)
        {
            _session = session;
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
        }

        private void HandleCardAddedToHand(Card card, Player player)
        {
            NetworkServer.SendToAll(new CardDrawnEvent
            {
                PlayerId = player.Id,
                CardId = card.Id,
            });
        }

        private void HandleCardAddedToHandFromDiscard(Card card, Player player)
        {
            NetworkServer.SendToAll(new CardAddedToHandFromDiscardEvent
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
            NetworkServer.SendToAll(new CardPlayedEvent
            {
                PlayerId = ownerId,
                CardId = card.Id,
            });
        }

        private void HandleCardMovedToBoard(Card card)
        {
            NetworkServer.SendToAll(new CardMovedToBoardEvent { CardId = card.Id });
        }

        private void HandleCardMovedToBeforePlayer(Card card)
        {
            NetworkServer.SendToAll(new CardMovedToBeforePlayerEvent
            {
                CardId = card.Id,
                OwnerId = card.Owner?.Id ?? 0,
            });
        }

        private void HandleCardMovedToTimeOfDay(Card card)
        {
            NetworkServer.SendToAll(new CardMovedToTimeOfDaySlotEvent { CardId = card.Id });
        }

        private void HandleCardMovedToDiscard(Card card)
        {
            NetworkServer.SendToAll(new CardMovedToDiscardPileEvent { CardId = card.Id });
        }

        private void HandleItemAdded(Item item, Player player)
        {
            NetworkServer.SendToAll(new ItemAddedToPlayerEvent
            {
                PlayerId = player.Id,
                ItemId = item.Id,
                ItemType = (int)item.Type,
            });
        }

        private void HandleItemRemoved(Item item, Player player)
        {
            NetworkServer.SendToAll(new ItemRemovedFromPlayerEvent
            {
                PlayerId = player.Id,
                ItemId = item.Id,
                ItemType = (int)item.Type,
            });
        }

        private void HandlePointsAwarded(Player player)
        {
            NetworkServer.SendToAll(new PointsAwardedEvent
            {
                PlayerId = player.Id,
                NewScore = player.Score,
            });
        }

        private void HandleMessagePrinted(string message)
        {
            NetworkServer.SendToAll(new MessagePrintedEvent { Message = message });
        }
    }
}
