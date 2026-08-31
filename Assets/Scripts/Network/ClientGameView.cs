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
        // Game torn down early — args are the reason to display and the
        // player who left (or null if the abort wasn't player-caused).
        public event Action<string, Player> OnGameAborted;

        /// <summary>Игрок вышел, но партия продолжается: (текст, кто ушёл).</summary>
        public event Action<string, Player> OnPlayerLeft;

        /// <summary>Карта ушла из руки обратно в колоду.</summary>
        public event Action<Card, Player> OnCardReturnedToDeck;

        /// <summary>Чем кончилась моя попытка применить правило: сработало ли.</summary>
        public event Action<bool> OnRuleEffectResolved;

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

            Defer<GameStartedEvent>(HandleGameStarted);
            Defer<CardDrawnEvent>(HandleCardDrawn);
            Defer<CardAddedToHandFromDiscardEvent>(HandleCardFromDiscardToHand);
            Defer<CardPlayedEvent>(HandleCardPlayed);
            Defer<CardMovedToBoardEvent>(HandleCardMovedToBoard);
            Defer<CardMovedToBeforePlayerEvent>(HandleCardMovedToBeforePlayer);
            Defer<CardMovedToTimeOfDaySlotEvent>(HandleCardMovedToTimeOfDay);
            Defer<CardMovedToDiscardPileEvent>(HandleCardMovedToDiscard);
            Defer<ItemAddedToPlayerEvent>(HandleItemAdded);
            Defer<ItemRemovedFromPlayerEvent>(HandleItemRemoved);
            Defer<PointsAwardedEvent>(HandlePointsAwarded);
            Defer<MessagePrintedEvent>(HandleMessagePrinted);
            Defer<TurnAdvancedEvent>(HandleTurnAdvanced);
            Defer<PhaseChangedEvent>(HandlePhaseChanged);
            Defer<DecisionRequestedEvent>(HandleDecisionRequested);
            Defer<DecisionResolvedEvent>(HandleDecisionResolved);
            Defer<GameEndedEvent>(HandleGameEnded);
            Defer<PlayerLeftEvent>(HandlePlayerLeft);
            Defer<CardReturnedToDeckEvent>(HandleCardReturnedToDeck);
            Defer<RuleEffectResolvedEvent>(HandleRuleEffectResolved);

            // Прерывание партии — единственное событие в обход очереди.
            //
            // Очередь ждёт анимаций, а анимации к этому моменту могут не
            // доиграть уже никогда: партии нет, сервера может не быть тоже.
            // Отложенное сюда сообщение о причине игрок просто не увидел бы,
            // а причина — это всё, что ему осталось узнать. Ещё не показанные
            // события выбрасываем: доигрывать раздачу в партию, которой уже
            // нет, незачем.
            NetworkClient.RegisterHandler<GameAbortedEvent>(msg =>
            {
                _pending.Clear();
                HandleGameAborted(msg);
            });
        }

        // Event queue -------------------------------------------------------
        //
        // События с сервера НЕ применяются в момент получения. Сервер шлёт их
        // на полной скорости, а каждое из них запускает анимацию на секунду —
        // в итоге карты раздавались поверх ещё летящей карты дня/ночи, а
        // запрос выбора приходил, пока стол ещё двигался.
        //
        // Вместо этого каждое сообщение кладётся в очередь, а качает её
        // UnGameManager: следующее событие применяется только когда доиграли
        // анимации предыдущего. Получается воспроизведение потока событий в
        // темпе анимаций.
        //
        // Порядок сохраняется сам: Mirror доставляет по надёжному каналу
        // строго по порядку, а очередь — FIFO.

        private readonly Queue<Action> _pending = new();

        public bool HasPendingEvents => _pending.Count > 0;

        /// <summary>Длина очереди. Для диагностики: растёт — значит анимации не успевают.</summary>
        public int PendingEventCount => _pending.Count;

        /// <summary>
        /// Применяет одно отложенное событие. Зовётся насосом; сам класс
        /// ничего не применяет по своей инициативе.
        /// </summary>
        public void ApplyNextEvent()
        {
            if (_pending.Count == 0) return;
            _pending.Dequeue()();
        }

        /// <summary>
        /// Подписывает обработчик так, что тот попадает в очередь, а не
        /// выполняется на месте. Единственный способ регистрации в этом
        /// классе — прямой RegisterHandler обошёл бы очередь и вернул старое
        /// поведение.
        /// </summary>
        private void Defer<T>(Action<T> handler) where T : struct, NetworkMessage
        {
            NetworkClient.RegisterHandler<T>(msg => _pending.Enqueue(() => handler(msg)));
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

        /// <summary>
        /// Убирает карту с доски перед тем, как положить её куда-то ещё.
        ///
        /// <para><b>Зачем цикл, а не одно удаление.</b> Сервер и клиент
        /// узнают о переездах карты по-разному. Разыгрывая карту, сервер
        /// сперва кладёт её на общий стол, а потом молча снимает и кладёт на
        /// её настоящее место (перед игроком, в слот дня/ночи, в сброс) —
        /// снятие событием не сопровождается. Клиент же получал оба события
        /// и на каждое делал <c>AddCardOnBoard</c>, так что в его снимке
        /// доски заводилось ДВА вхождения одной карты. А
        /// <c>RemoveCardFromBoard</c> — это <c>List.Remove</c>, то есть одно
        /// вхождение: карта уходила в сброс, а её двойник оставался на доске
        /// навсегда.</para>
        ///
        /// <para>Так подсветка правила A1-2 и уверяла, что на столе есть
        /// злодей, когда последнего уже убили. Цикл заодно вычищает
        /// дубликаты, накопленные до этой правки.</para>
        /// </summary>
        private void DetachFromBoard(Card card)
        {
            var onBoard = Board.GetCardsOnBoard();
            while (onBoard.Contains(card))
                Board.RemoveCardFromBoard(card);
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

            // Карта могла приехать в руку не из колоды, а СО СТОЛА: так
            // работают кражи (Огр, Принцесса) — в ядре это
            // RemoveCardFromBoard без события плюс обычное «положить в руку».
            DetachFromBoard(card);

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
            DetachFromBoard(card);
            Board.AddCardOnBoard(card);
            card.Position = CardPosition.OnGameBoard;
            OnCardMovedToBoard?.Invoke(card);
        }

        private void HandleCardMovedToBeforePlayer(CardMovedToBeforePlayerEvent evt)
        {
            var card = FindCard(evt.CardId);
            var owner = FindPlayer(evt.OwnerId);
            if (card == null) return;
            DetachFromBoard(card);
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
            // Карта дня/ночи тоже успевает побывать на общем столе по дороге.
            DetachFromBoard(card);
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
            DetachFromBoard(card);
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

        /// <summary>
        /// Игрок вышел, партия продолжается. Убираем его из зеркала, но
        /// НЕ трогаем <see cref="Opponents"/> как источник мест: место в
        /// раскладке остаётся пустым до конца партии. Пересобирать раскладку
        /// на ходу значило бы переселять карты, которые уже лежат на столе, —
        /// цена, не стоящая аккуратной картинки.
        /// </summary>
        private void HandlePlayerLeft(PlayerLeftEvent evt)
        {
            var player = FindPlayer(evt.PlayerId);
            if (player == null) return;

            Players.Remove(player);
            if (player == LocalPlayer) LocalPlayer = null;

            OnPlayerLeft?.Invoke(evt.Reason, player);
        }

        /// <summary>
        /// Карта ушла из руки обратно в колоду — так разбирается рука
        /// вышедшего игрока.
        /// </summary>
        private void HandleCardReturnedToDeck(CardReturnedToDeckEvent evt)
        {
            var card = FindCard(evt.CardId);
            if (card == null) return;

            var owner = card.Owner;
            owner?.RemoveCardFromHand(card);
            card.Owner = null;
            card.Position = CardPosition.InDeck;
            if (!DeckOrder.Contains(card.Id)) DeckOrder.Add(card.Id);

            OnCardReturnedToDeck?.Invoke(card, owner);
        }

        private void HandleRuleEffectResolved(RuleEffectResolvedEvent evt)
        {
            OnRuleEffectResolved?.Invoke(evt.Applied);
        }

        private void HandleGameAborted(GameAbortedEvent evt)
        {
            // LeftPlayerId is 0 when the abort wasn't caused by a specific
            // player (engine fault, server shutdown), and FindPlayer
            // returns null for it — subscribers must cope with that.
            OnGameAborted?.Invoke(evt.Reason, FindPlayer(evt.LeftPlayerId));
        }
    }
}
