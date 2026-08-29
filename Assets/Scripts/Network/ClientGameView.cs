using Assets.Scripts.Network.Messages;
using Mirror;
using ScaryTales;
using ScaryTales.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Assets.Scripts.Network
{
    /// <summary>
    /// Client-side mirror of the game. Receives DomainEvents from the
    /// server, mutates a denormalized snapshot, and re-fires equivalent C#
    /// events so existing UI subscribers (BoardUI, PlayerHandUI,
    /// TextUIManager) keep working without knowing they're now driven by
    /// the network rather than an in-process engine.
    ///
    /// The client does not run the engine. Effects do not execute here.
    /// All state changes arrive as discrete events.
    ///
    /// Card identity mapping: every client builds the same full Card
    /// catalog from GameBuilder.MakeCardTemplates() and assigns the same
    /// IDs (the Deck constructor's IDs are stable as long as templates
    /// match). The server sends DeckOrder in GameStartedEvent so all peers
    /// share an initial face-down sequence; identities only become visible
    /// to a given player when CardDrawnEvent reveals them.
    /// </summary>
    public class ClientGameView
    {
        // Identity ----------------------------------------------------------

        public List<Player> Players { get; private set; } = new();
        public Player LocalPlayer { get; private set; }
        // All players except LocalPlayer, in the same order as Players.
        // For 2-player this is a one-element list; UI components that used
        // to bind LocalOpponent should pick Opponents.FirstOrDefault().
        public IReadOnlyList<Player> Opponents { get; private set; } = new List<Player>();

        // Phase / turn state ------------------------------------------------

        public bool IsNight { get; private set; }
        public int CurrentPlayerId { get; private set; }
        public int TurnCount { get; private set; }
        public Player CurrentPlayer => FindPlayer(CurrentPlayerId);

        // Which rules the server chose for this game. Ids only — the UI
        // rebuilds the Rule objects from RuleCatalog, so no client hardcodes
        // its own copy and hopes it matches the server's.
        public int CurrentRuleId { get; private set; }
        public int CurrentFinalRuleId { get; private set; }

        // Card catalog: every Card object shared by id, built once at game
        // start. Deck order arrives separately and only governs face-down
        // sequencing, not identity.
        private readonly Dictionary<int, Card> _allCards = new();

        // Cards currently visible somewhere (board, before-player, time-of-
        // day, discard, in any hand). Used by id-based lookups in handlers.
        public IReadOnlyDictionary<int, Card> AllCards => _allCards;

        // Visual deck order (face-down to all). Tracks what would be on top
        // of the deck so a future visual could animate from the top.
        public List<int> DeckOrder { get; private set; } = new();

        // GameBoard mirror. The same data structure the engine uses, but
        // mutated directly by event handlers — no GameManager.
        public GameBoard Board { get; } = new();

        // Events the UI subscribes to ---------------------------------------
        // Names mirror the existing GameManager events so UI handlers can
        // be redirected with a 1-line subscription change.

        public event Action<Card, Player> OnCardAddedToHand;
        public event Action<Card, Player> OnCardAddedToHandFromDiscardPile;
        public event Action<Card> OnCardPlayed;
        public event Action<Card> OnCardMovedToDiscardPile;
        public event Action<Card> OnCardMovedToBoard;
        public event Action<Card> OnCardMovedToBeforePlayer;
        public event Action<Card> OnCardMovedToTimeOfDaySlot;
        public event Action<Item, Player> OnItemAddToPlayer;
        public event Action<Item, Player> OnItemRemovedFromPlayer;
        public event Action<Player> OnAddPointsToPlayer;
        public event Action<string> OnMessagePrinted;

        // Higher-level events
        public event Action OnGameStarted;
        public event Action<int> OnTurnAdvanced; // arg = currentPlayerId
        public event Action<bool> OnPhaseChanged; // arg = isNight
        public event Action<int> OnGameEnded;     // arg = winnerId

        // Decision flow events. UI listens to know when to show pick prompts
        // and when to dismiss them.
        public event Action<DecisionRequestedEvent> OnDecisionRequested;
        public event Action<int> OnDecisionResolved; // arg = requestId

        // Lifecycle ---------------------------------------------------------

        public ClientGameView()
        {
            // Pre-build the full card catalog (same construction the server
            // uses) so cards are available by id from the moment events
            // start arriving.
            BuildCardCatalog();

            NetworkClient.RegisterHandler<GameStartedEvent>(HandleGameStarted);
            NetworkClient.RegisterHandler<CardDrawnEvent>(HandleCardDrawn);
            NetworkClient.RegisterHandler<CardAddedToHandFromDiscardEvent>(HandleCardFromDiscardToHand);
            NetworkClient.RegisterHandler<CardPlayedEvent>(HandleCardPlayed);
            NetworkClient.RegisterHandler<CardMovedToBoardEvent>(HandleCardMovedToBoard);
            NetworkClient.RegisterHandler<CardMovedToBeforePlayerEvent>(HandleCardMovedToBeforePlayer);
            NetworkClient.RegisterHandler<CardMovedToTimeOfDaySlotEvent>(HandleCardMovedToTimeOfDay);
            NetworkClient.RegisterHandler<CardMovedToDiscardPileEvent>(HandleCardMovedToDiscard);
            NetworkClient.RegisterHandler<ItemAddedToPlayerEvent>(HandleItemAdded);
            NetworkClient.RegisterHandler<ItemRemovedFromPlayerEvent>(HandleItemRemoved);
            NetworkClient.RegisterHandler<PointsAwardedEvent>(HandlePointsAwarded);
            NetworkClient.RegisterHandler<MessagePrintedEvent>(HandleMessagePrinted);
            NetworkClient.RegisterHandler<TurnAdvancedEvent>(HandleTurnAdvanced);
            NetworkClient.RegisterHandler<PhaseChangedEvent>(HandlePhaseChanged);
            NetworkClient.RegisterHandler<DecisionRequestedEvent>(HandleDecisionRequested);
            NetworkClient.RegisterHandler<DecisionResolvedEvent>(HandleDecisionResolved);
            NetworkClient.RegisterHandler<GameEndedEvent>(HandleGameEnded);
        }

        // Helpers ----------------------------------------------------------

        public Player FindPlayer(int id) => Players.FirstOrDefault(p => p.Id == id);
        public Card FindCard(int id) => _allCards.TryGetValue(id, out var c) ? c : null;

        private void BuildCardCatalog()
        {
            // The Deck constructor assigns sequential IDs starting at 1 in
            // template iteration order. We replicate that here without
            // actually using the Deck's shuffle (the server's shuffle
            // arrives via DeckOrder).
            var templates = GameBuilder.MakeCardTemplates();
            int nextId = 1;
            foreach (var template in templates)
            {
                for (int i = 0; i < template.CardCountInDeck; i++)
                {
                    var card = template.Clone();
                    card.Id = nextId++;
                    _allCards[card.Id] = card;
                }
            }
        }

        // Handlers ----------------------------------------------------------

        private void HandleGameStarted(GameStartedEvent evt)
        {
            Players = evt.Players
                .Select(pi => new Player(pi.Id, pi.Name))
                .ToList();
            LocalPlayer = FindPlayer(evt.LocalPlayerId);
            Opponents = Players.Where(p => p.Id != evt.LocalPlayerId).ToList();
            CurrentPlayerId = evt.StartPlayerId;
            DeckOrder = evt.DeckOrder?.ToList() ?? new List<int>();
            CurrentRuleId = evt.CurrentRuleId;
            CurrentFinalRuleId = evt.CurrentFinalRuleId;
            IsNight = false;
            TurnCount = 0;

            OnGameStarted?.Invoke();
        }

        private void HandleCardDrawn(CardDrawnEvent evt)
        {
            var player = FindPlayer(evt.PlayerId);
            var card = FindCard(evt.CardId);
            if (player == null || card == null) return;

            // Top of the visual deck pops to a hand.
            if (DeckOrder.Count > 0 && DeckOrder[0] == evt.CardId)
                DeckOrder.RemoveAt(0);
            else
                DeckOrder.Remove(evt.CardId); // resilient to out-of-order events

            player.AddCardToHand(card);
            card.Position = CardPosition.InHand;
            card.Owner = player;

            OnCardAddedToHand?.Invoke(card, player);
        }

        private void HandleCardFromDiscardToHand(CardAddedToHandFromDiscardEvent evt)
        {
            var player = FindPlayer(evt.PlayerId);
            var card = FindCard(evt.CardId);
            if (player == null || card == null) return;

            // Card was on top of discard; remove it.
            var discard = Board.GetCardsFromDiscardPile();
            discard.Remove(card);

            player.AddCardToHand(card);
            card.Position = CardPosition.InHand;
            card.Owner = player;

            OnCardAddedToHandFromDiscardPile?.Invoke(card, player);
        }

        private void HandleCardPlayed(CardPlayedEvent evt)
        {
            var card = FindCard(evt.CardId);
            var player = FindPlayer(evt.PlayerId);
            if (card == null || player == null) return;

            player.RemoveCardFromHand(card);
            // The follow-up CardMovedTo* event will set the final position.
            OnCardPlayed?.Invoke(card);
        }

        private void HandleCardMovedToBoard(CardMovedToBoardEvent evt)
        {
            var card = FindCard(evt.CardId);
            if (card == null) return;
            Board.AddCardOnBoard(card);
            card.Position = CardPosition.OnGameBoard;
            OnCardMovedToBoard?.Invoke(card);
        }

        private void HandleCardMovedToBeforePlayer(CardMovedToBeforePlayerEvent evt)
        {
            var card = FindCard(evt.CardId);
            var owner = FindPlayer(evt.OwnerId);
            if (card == null) return;
            Board.AddCardOnBoard(card); // legacy behavior: BeforePlayer cards live on the board
            card.Position = CardPosition.BeforePlayer;
            if (owner != null) card.Owner = owner;
            OnCardMovedToBeforePlayer?.Invoke(card);
        }

        private void HandleCardMovedToTimeOfDay(CardMovedToTimeOfDaySlotEvent evt)
        {
            var card = FindCard(evt.CardId);
            if (card == null) return;
            var current = Board.GetCardFromTimeOfDaySlot();
            if (current != null)
            {
                Board.AddCardToDiscardPile(current);
                current.Position = CardPosition.Discarded;
            }
            Board.SetTimeOfDaySlot(card);
            card.Position = CardPosition.TimeOfDay;
            OnCardMovedToTimeOfDaySlot?.Invoke(card);
        }

        private void HandleCardMovedToDiscard(CardMovedToDiscardPileEvent evt)
        {
            var card = FindCard(evt.CardId);
            if (card == null) return;
            // Could be coming from board or hand or anywhere; clean up
            // wherever it was tracked.
            Board.RemoveCardFromBoard(card);
            foreach (var p in Players) p.RemoveCardFromHand(card);
            Board.AddCardToDiscardPile(card);
            card.Position = CardPosition.Discarded;
            card.Owner = null;
            OnCardMovedToDiscardPile?.Invoke(card);
        }

        private void HandleItemAdded(ItemAddedToPlayerEvent evt)
        {
            var player = FindPlayer(evt.PlayerId);
            if (player == null) return;
            // Build a fresh Item instance from type. The full Item template
            // catalog is in GameBuilder.MakeItemTemplates(); we look up by
            // ItemType to construct the right concrete subclass.
            var item = MakeItemOfType((ItemType)evt.ItemType);
            if (item == null) return;
            item.Id = evt.ItemId;
            player.AddItemToItemBag(item);
            OnItemAddToPlayer?.Invoke(item, player);
        }

        private void HandleItemRemoved(ItemRemovedFromPlayerEvent evt)
        {
            var player = FindPlayer(evt.PlayerId);
            if (player == null) return;
            // Use type-based removal — IDs assigned on the server's
            // ItemManager don't necessarily match what the client built
            // locally, but type matching is enough since the bag has at
            // most a small handful of each.
            player.RemoveItemFromItemBag((ItemType)evt.ItemType);
            // Construct a token Item for subscribers that need details.
            var item = MakeItemOfType((ItemType)evt.ItemType);
            if (item != null) item.Id = evt.ItemId;
            OnItemRemovedFromPlayer?.Invoke(item, player);
        }

        private static Item MakeItemOfType(ItemType type)
        {
            foreach (var tpl in GameBuilder.MakeItemTemplates())
                if (tpl.Type == type) return tpl.Clone();
            return null;
        }

        private void HandlePointsAwarded(PointsAwardedEvent evt)
        {
            var player = FindPlayer(evt.PlayerId);
            if (player == null) return;
            // We don't have a public setter on Player.Score; use AddPoints
            // with the delta (we store NewScore in the event for resilience,
            // but the existing UI just re-renders from player.Score).
            int delta = evt.NewScore - player.Score;
            if (delta > 0) player.AddPoints(delta);
            OnAddPointsToPlayer?.Invoke(player);
        }

        private void HandleMessagePrinted(MessagePrintedEvent evt)
        {
            OnMessagePrinted?.Invoke(evt.Message);
        }

        private void HandleTurnAdvanced(TurnAdvancedEvent evt)
        {
            CurrentPlayerId = evt.CurrentPlayerId;
            TurnCount = evt.TurnCount;
            IsNight = evt.IsNight;
            OnTurnAdvanced?.Invoke(evt.CurrentPlayerId);
        }

        private void HandlePhaseChanged(PhaseChangedEvent evt)
        {
            IsNight = evt.IsNight;
            OnPhaseChanged?.Invoke(evt.IsNight);
        }

        private void HandleDecisionRequested(DecisionRequestedEvent evt)
        {
            OnDecisionRequested?.Invoke(evt);
        }

        private void HandleDecisionResolved(DecisionResolvedEvent evt)
        {
            OnDecisionResolved?.Invoke(evt.RequestId);
        }

        private void HandleGameEnded(GameEndedEvent evt)
        {
            OnGameEnded?.Invoke(evt.WinnerId);
        }
    }
}
