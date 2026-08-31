using Assets.Libraries.ScaryTales.Abstractions;
using ScaryTales.Abstractions;
using ScaryTales.Decisions;
using ScaryTales.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace ScaryTales
{
    public class GameManager : IGameManager
    {
        public readonly IGameContext _context;
        private readonly INotifier _notifier;
        public event Action<Card, Player>? OnCardAddedToHand;
        public event Action<Card, Player>? OnCardAddedToHandFromDiscardPile;
        public event Action<Card>? OnCardPlayed;
        public event Action<Card>? OnCardMovedToDiscardPile;
        public event Action<Card>? OnCardMovedToBoard;
        public event Action<Card>? OnCardMovedToBeforePlayer;
        public event Action<Card>? OnCardMovedToTimeOfDaySlot;
        public event Action<Item, Player>? OnItemAddToPlayer;
        public event Action<Item, Player>? OnItemRemovedFromPlayer;
        public event Action<Player>? OnAddPointsToPlayer;
        public event Action<string>? OnMessagePrinted;

        public GameManager(IGameState gameState, IGameBoard gameBoard,
            List<Player> players, Deck deck, ItemManager items,
            INotifier notifier, IDecisionRouter router)
        {
            _context = new GameContext(
                gameState, gameBoard,
                    players, deck, items, this, router);
            _notifier = notifier;
        }
        public void PrintMessage(string message)
        {
            _notifier.Notify(message);
            OnMessagePrinted?.Invoke(message);
        }
        /// <summary>
        /// Пытается вытянуть карту из колоды, если она не пуста.
        /// </summary>
        /// <returns>Карта или null. БЕЗ ВЛАДЕЛЬЦА</returns>
        public Card? TryDrawCardFromDeck()
        {
            var deck = _context.Deck;
            var card = deck.DrawCard();
            if (card == null)
            {
                PrintMessage("В колоде не осталось карт");
                return null;
            }
            else if (deck.CardsRemaining == 1)
            {
                PrintMessage("В колоде осталась последняя карта");
                return card;
            }
            else
                return card;
        }
        /// <summary>
        /// Взять 1 карту из колоды и передать игроку
        /// </summary>
        /// <param name="player"></param>
        public void DrawCard(Player player)
        {
            var cardFromDeck = TryDrawCardFromDeck();
            if (cardFromDeck != null)
            {
                PutCardInPlayerHand(cardFromDeck, player);
            }
        }

        public async Task PlayCard(Card card)
        {
            var player = _context.GameState.GetCurrentPlayer();
            var board = _context.GameBoard;
            if (player.HasCard(card))
            {
                player.RemoveCardFromHand(card);
                PrintMessage($"Игрок {player.Name} разыгрывает карту {card.Name}.");
                PutCardOnBoard(card);
                AddPointsToPlayer(player, card.Points);
                await Task.Delay(1000);
                await ActivateInstantCardEffect(card);
                if(card.PositionAfterPlay != CardPosition.OnGameBoard)
                {
                    board.RemoveCardFromBoard(card);
                    MoveCardToItsPosition(card);
                }
            }
        }
        /// <summary>
        /// Активирует все постоянные эффекты активных карт игрока
        /// </summary>
        public async Task ActivateAllPlayerPermanentCardEffects(Player player)
        {
            var board = _context.GameBoard;
            var cards = board.GetCardsOnBoard(player);
            foreach (var card in cards)
                await ActivatePermanentCardEffect(card);
        }
        /// <summary>
        /// Активируется мгновенный эффект карты
        /// </summary>
        public async Task ActivateInstantCardEffect(Card card)
        {
            if (card.Effect.Type == CardEffectTimeType.Instant)
               await card.ActivateEffect(_context);
        }
        /// <summary>
        /// Активируется постоянный эффект карты
        /// </summary>
        public async Task ActivatePermanentCardEffect(Card card)
        {
            if (card.Effect.Type == CardEffectTimeType.PermanentAtTheEnd)
               await card.ActivateEffect(_context);
        }
        /// <summary>
        /// Присвоение пользователю ПО
        /// </summary>
        /// <param name="player">Кому присвоить</param>
        /// <param name="points">Сколько ПО присвоить</param>
        public void AddPointsToPlayer(Player player, int points)
        {
            if (points > 0)
            {
                PrintMessage($"Игрок {player.Name} получает {points} ПО.");
                player.AddPoints(points);
                OnAddPointsToPlayer?.Invoke(player);
            }
        }
        public void MoveCardToItsPosition(Card card)
        {
            var board = _context.GameBoard;
            switch (card.PositionAfterPlay)
            {
                case (CardPosition.OnGameBoard):
                    {
                        PutCardOnBoard(card);
                        PrintMessage($"Карта {card.Name} была разыграна на стол.");
                        break;
                    }
                case (CardPosition.BeforePlayer):
                    {
                        PutCardBeforePlayer(card);
                        PrintMessage($"Карта {card.Name} была разыграна на стол перед игроком.");
                        break;
                    }
                case (CardPosition.Discarded):
                    {
                        PutCardToDiscardPile(card);
                        PrintMessage($"Карта {card.Name} была разыграна и сброшена.");
                        break;
                    }
                case (CardPosition.TimeOfDay):
                    {
                        PutCardInTimeOfDaySlot(card);
                        PrintMessage($"Карта {card.Name} была разыграна.");
                        break;
                    }
            }
        }
        public void PutCardToDiscardPile(Card card)
        {
            var board = _context.GameBoard;
            board.AddCardToDiscardPile(card);
            card.Position = CardPosition.Discarded;
            card.Owner = null;
            OnCardMovedToDiscardPile?.Invoke(card);
        }
        public void PutCardOnBoard(Card card)
        {
            var board = _context.GameBoard;
            board.AddCardOnBoard(card);
            card.Position = CardPosition.OnGameBoard;
            OnCardMovedToBoard?.Invoke(card);
        }
        public void PutCardBeforePlayer(Card card)
        {
            var board = _context.GameBoard;
            board.AddCardOnBoard(card); // Временно
            card.Position = CardPosition.BeforePlayer;
            OnCardMovedToBeforePlayer?.Invoke(card);
        }
        public void PutCardInPlayerHand(Card card, Player player)
        {
            player.AddCardToHand(card);
            card.Position = CardPosition.InHand;
            card.Owner = player;
            OnCardAddedToHand?.Invoke(card, player);
        }
        public void PutCardInPlayerHandFromDiscardPile(Card card, Player player)
        {
            player.AddCardToHand(card);
            card.Position = CardPosition.InHand;
            card.Owner = player;
            OnCardAddedToHandFromDiscardPile?.Invoke(card, player);
        }

        public void PutCardInTimeOfDaySlot(Card card)
        {
            var board = _context.GameBoard;
            var oldCard = board.GetCardFromTimeOfDaySlot();
            if(oldCard != null)
                PutCardToDiscardPile(oldCard);
            board.SetTimeOfDaySlot(card);
            card.Position = CardPosition.TimeOfDay;
            OnCardMovedToTimeOfDaySlot?.Invoke(card);
        }
        public void PutItemInPlayerItemBag(Item item, Player player)
        {
            player.AddItemToItemBag(item);
            OnItemAddToPlayer?.Invoke(item, player);
        }
        public void RemoveItemFromPlayerItemBag(ItemType type, Player player)
        {
            // Snapshot the item being removed so subscribers can identify
            // which one disappeared (player.RemoveItemFromItemBag(type) drops
            // it without returning a reference).
            var item = player.ShowItemsFromItemBag().FirstOrDefault(x => x.Type == type);
            if (item == null) return;
            player.RemoveItemFromItemBag(item);
            OnItemRemovedFromPlayer?.Invoke(item, player);
        }
        public void EndGame()
        {
            _context.GameState.EndGame();
        }

        public async Task ActivateRuleEffect(IRuleEffect effect)
        {
             await effect.ApplyEffect(_context);
        }
    }
}
