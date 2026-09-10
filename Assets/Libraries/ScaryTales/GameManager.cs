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
        public event Action<Card>? OnCardRemovedFromGame;
        public event Action<Item, Player>? OnItemAddToPlayer;
        public event Action<Item, Player>? OnItemRemovedFromPlayer;
        public event Action<Player>? OnAddPointsToPlayer;
        public event Action<string>? OnMessagePrinted;

        /// <summary>Все показали руки или спрятали обратно — см. <see cref="RevealAllHands"/>.</summary>
        public event Action<Player, bool>? OnHandsRevealed;

        /// <summary>Карта выступила вперёд или вернулась — см. <see cref="ShowcaseCard"/>.</summary>
        public event Action<Card, bool>? OnCardShowcased;

        /// <summary>Игрок смотрит верх колоды — см. <see cref="ShowPeekedCards"/>.</summary>
        public event Action<Player, int>? OnCardsPeeked;


        /// <summary>
        /// Сколько карт осталось в колоде. Клиенту иначе неоткуда это узнать:
        /// колода нарисована рубашкой, и опустевшую от полной не отличить.
        ///
        /// <para>Считать на клиенте по событиям взятия нельзя — не всё, что
        /// уходит из колоды, сопровождается событием: карта Ночи забирается в
        /// слот времени суток напрямую, а Волшебник раскрывает карту, минуя
        /// руку. Поэтому число присылает тот, кто им владеет.</para>
        /// </summary>
        public event Action<int>? OnDeckCountChanged;

        /// <summary>
        /// Сообщить о текущем размере колоды. Зовётся отсюда при каждом
        /// взятии и снаружи — теми, кто трогает колоду напрямую (раздача
        /// карты Ночи, возврат руки вышедшего игрока).
        /// </summary>
        public void NotifyDeckChanged() => OnDeckCountChanged?.Invoke(_context.Deck.CardsRemaining);

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

        /// <inheritdoc/>
        public void RevealAllHands(Player by, bool revealed) => OnHandsRevealed?.Invoke(by, revealed);

        /// <inheritdoc/>
        public void ShowcaseCard(Card card, bool stepOut) => OnCardShowcased?.Invoke(card, stepOut);

        /// <inheritdoc/>
        public void ShowPeekedCards(Player by, int count) => OnCardsPeeked?.Invoke(by, count);

        /// <inheritdoc/>
        public void GrantExtraPlay()
        {
            _context.GameState.GrantExtraPlay();
            PrintMessage($"Игрок {_context.GameState.GetCurrentPlayer().Name} может разыграть ещё одну карту в этом ходу.");
        }

        /// <summary>
        /// Пытается вытянуть карту из колоды, если она не пуста.
        /// </summary>
        /// <returns>Карта или null. БЕЗ ВЛАДЕЛЬЦА</returns>
        public Card? TryDrawCardFromDeck()
        {
            var deck = _context.Deck;
            var card = deck.DrawCard();
            // Единственная воронка, через которую карты покидают колоду в
            // ходе партии, — поэтому сообщать о новом размере достаточно
            // отсюда (и из тех мест снаружи, что трогают колоду напрямую).
            if (card != null) NotifyDeckChanged();
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
            // Запрет спрашивается здесь, а не в цикле хода: через этот метод
            // карты из колоды попадают в руку ВЕЗДЕ — и обязательным взятием
            // хода, и эффектами карт (Купец, Заколдованный лес), и правилами
            // партии. Проверяй мы в одном цикле хода, Ведьма запрещала бы
            // только обязательное взятие, а Купец обходил бы её.
            if (IsDeckDrawBlockedFor(player))
            {
                PrintMessage($"Игрок {player.Name} не может брать карты из колоды.");
                return;
            }

            var cardFromDeck = TryDrawCardFromDeck();
            if (cardFromDeck != null)
            {
                PutCardInPlayerHand(cardFromDeck, player);
            }
        }

        /// <summary>
        /// Запрещает ли игроку брать из колоды хоть одна его карта на столе.
        /// </summary>
        private bool IsDeckDrawBlockedFor(Player player)
        {
            if (player == null) return false;

            return _context.GameBoard.GetCardsOnBoard(player).Any(c => c.BlocksDeckDraw);
        }

        /// <summary>
        /// Правила начала хода, которые накладывают карты перед игроком:
        /// сначала уходят те, чей срок вышел, потом оставшиеся выдают право на
        /// дополнительные розыгрыши.
        ///
        /// <para>Порядок важен. Сброс идёт первым, потому что уходящая карта
        /// не должна ни дарить розыгрыш на прощание, ни мешать обязательному
        /// взятию — Ведьма уходит ровно тогда, когда брать снова можно и
        /// нужно.</para>
        ///
        /// <para>Право на розыгрыши выдаётся заново каждый ход: оно живёт один
        /// ход и гасится <c>ClearExtraPlays</c> в начале следующего. Здесь —
        /// для карт, которые уже лежали на столе к началу хода. Ту, что
        /// выложили прямо сейчас, обслуживает <see cref="PlayCard"/>: своё
        /// право она даёт сразу, не дожидаясь следующего хода.</para>
        /// </summary>
        public void ApplyStartOfTurnBoardRules(Player player)
        {
            if (player == null) return;

            var board = _context.GameBoard;

            // ToList: сброс меняет коллекцию, по которой идём.
            foreach (var card in board.GetCardsOnBoard(player).ToList())
            {
                if (!card.ShouldDiscardAtTurnStart(_context)) continue;

                PrintMessage($"Карта {card.Name} уходит в сброс: её условие выполнено.");
                board.RemoveCardFromBoard(card);
                PutCardToDiscardPile(card);
            }

            foreach (var card in board.GetCardsOnBoard(player))
                for (int i = 0; i < card.ExtraPlaysPerTurn; i++)
                    GrantExtraPlay();
        }

        public async Task PlayCard(Card card)
        {
            var player = _context.GameState.GetCurrentPlayer();
            var board = _context.GameBoard;
            if (player.HasCard(card))
            {
                player.RemoveCardFromHand(card);
                // Сказать об уходе из руки ДО того, как карта куда-то ляжет:
                // клиент повторяет порядок вызовов сервера, а зеркало руки
                // иначе копит разыгранное — из руки его убирало только
                // попадание в сброс, и карты со стола оставались в ней навсегда.
                OnCardPlayed?.Invoke(card);
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

                // Постоянное правило начинает действовать сразу, в тот же ход,
                // когда карту выложили: «можете играть 1 дополнительную карту
                // в свой ход» — а ход, в который её выложили, тоже свой.
                //
                // На этом ходу право выдаётся здесь, на следующих — в начале
                // хода (ApplyStartOfTurnBoardRules). Задвоения нет: в начале
                // ЭТОГО хода карты на столе ещё не было.
                //
                // Проверка «осталась на столе» не формальность: карта, которая
                // после розыгрыша уходит в сброс, в игре не остаётся, и
                // постоянных правил у неё быть не может.
                if (card.ExtraPlaysPerTurn > 0 && board.GetCardsOnBoard(player).Contains(card))
                    for (int i = 0; i < card.ExtraPlaysPerTurn; i++)
                        GrantExtraPlay();
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
        /// <inheritdoc/>
        public void RemoveCardFromGame(Card card)
        {
            // Карта может ещё лежать в колоде: Помощница смотрит её верх, не
            // снимая карт, и снять лишнюю — часть удаления, а не отдельный шаг.
            // Иначе у зовущего появился бы соблазн «удалить» карту, оставив её
            // при этом в колоде.
            if (_context.Deck.TakeCardById(card.Id) != null) NotifyDeckChanged();

            card.Owner = null;
            card.Position = CardPosition.RemovedFromGame;
            OnCardRemovedFromGame?.Invoke(card);
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
