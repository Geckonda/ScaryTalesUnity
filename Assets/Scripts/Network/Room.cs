using Assets.Libraries.ScaryTales.Rules;
using Assets.Scripts;
using Assets.Scripts.Network.Messages;
using Mirror;
using ScaryTales;
using ScaryTales.Enums;
using ScaryTales.Interaction_Entities.EnvUnity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Assets.Scripts.Network
{
    /// <summary>
    /// One game room: its lobby, its players, its engine, its turn loop
    /// (Phase 6.4).
    ///
    /// <para>This is the former <c>GameNetworkController</c>, grown to absorb
    /// the roster and seat bookkeeping that used to sit in
    /// <see cref="GameConnectionManager"/> as process-global state. Everything
    /// a game needs now hangs off one object, so several can coexist in one
    /// process — which is the whole point of Phase 6.</para>
    ///
    /// <para><b>No longer a NetworkBehaviour.</b> Phase 3.3 deleted every
    /// <c>Cmd</c>/<c>Rpc</c> from it, leaving a networked object with no
    /// networked behaviour — a prefab and a <c>NetworkServer.Spawn</c> call
    /// that bought nothing and made per-room instancing awkward. It is plain
    /// C# now, constructed directly by its owner.</para>
    ///
    /// <para>Server-side only. Clients hold a <c>ClientGameView</c> built from
    /// the DomainEvent stream and never see this class.</para>
    /// </summary>
    public class Room
    {
        // ---- Configuration, fixed at construction ----
        private readonly int _minPlayers;
        private readonly int _maxPlayers;
        private int _inGameRuleId;
        private int _finalRuleId;

        // ---- Lobby ----
        private readonly List<Player> _players = new();
        // Reverse index: Mirror's connection id → seat id. Needed because a
        // disconnect only hands us a connection.
        private readonly Dictionary<int, int> _seatByConnection = new();
        // Monotonic within this room, never reused, and never 0 — 0 is the
        // "no player" sentinel on the wire (GameAbortedEvent.LeftPlayerId,
        // ServerEventBroadcaster's owner fallbacks).
        private int _nextSeatId = 1;
        // Выбранное место за столом: seat id → номер стула. Отдельно от
        // самого места, потому что это разные вещи: seat id — личность
        // игрока, стул — позиция в очереди ходов. Кто не выбрал, здесь и не
        // числится.
        private readonly Dictionary<int, int> _chairBySeat = new();
        private bool _gameStarted;

        /// <summary>This room's seats, and its only route to their clients.</summary>
        public RoomChannel Channel { get; } = new();

        // ---- Game ----
        private GameSession _session;
        private NetworkDecisionRouter _router;
        private ServerEventBroadcaster _broadcaster;
        // Awaited by the turn loop; completes with the chosen card id when
        // the active player's PlayCardIntent arrives.
        private TaskCompletionSource<int> _waitingForPlay;
        // _gameOver is set by *both* endings — the natural one and an abort —
        // and is what makes every teardown path idempotent. _aborted narrows
        // that to "ended early", the only case where clients get a
        // GameAbortedEvent instead of a GameEndedEvent.
        private bool _gameOver;
        private bool _aborted;
        // Ход прерван уходом того, чей он был. Игровому циклу это говорит не
        // звать NextTurn: RemovePlayer уже поставил следующего игрока на тот
        // же индекс, и второй сдвиг перескочил бы через него.
        private bool _currentTurnAbandoned;

        // ---- Identity ----
        /// <summary>Short code players type to join. Assigned by the registry.</summary>
        public string Code { get; }
        /// <summary>Cosmetic, chosen by whoever created the room.</summary>
        public string Name { get; }
        /// <summary>
        /// Seat of the player who created the room — the only one allowed to
        /// start the game. 0 until someone joins. Deliberately a seat rather
        /// than a connection, so ownership survives the connection (the same
        /// reasoning as Player.Id in 6.1).
        /// </summary>
        public int OwnerSeatId { get; private set; }

        // ---- Observation ----
        public IReadOnlyList<Player> Players => _players;
        public int PlayerCount => _players.Count;
        public int MinPlayers => _minPlayers;
        public int MaxPlayers => _maxPlayers;
        public bool IsGameStarted => _gameStarted;
        public bool IsAborted => _aborted;
        public bool CanStart => !_gameStarted && _players.Count >= _minPlayers;
        /// <summary>
        /// No live connections left. Not the same as an empty roster: a
        /// mid-game departure deliberately keeps its seat (see OnSeatVacated),
        /// so the roster can be non-empty while nobody is actually here.
        /// This is the condition the owner destroys a room on.
        /// </summary>
        public bool IsAbandoned => Channel.Count == 0;
        public GameSession Session => _session;
        /// <summary>ServerIntentDispatcher reaches the router through here.</summary>
        public NetworkDecisionRouter Router => _router;
        /// <summary>Non-zero means the room is waiting on somebody.</summary>
        public int PendingDecisionCount => _router?.PendingDecisionCount ?? 0;

        public Room(string code, string name, int minPlayers, int maxPlayers, int inGameRuleId, int finalRuleId)
        {
            Code = code;
            Name = name;
            _minPlayers = minPlayers;
            _maxPlayers = maxPlayers;
            _inGameRuleId = inGameRuleId;
            _finalRuleId = finalRuleId;
        }

        // ---- Membership ----

        public enum JoinResult { Ok, RoomFull, GameInProgress }

        /// <summary>Whether this room would take another player right now.</summary>
        private JoinResult CanAccept()
        {
            if (_players.Count >= _maxPlayers) return JoinResult.RoomFull;
            if (_gameStarted) return JoinResult.GameInProgress;
            return JoinResult.Ok;
        }

        /// <summary>
        /// Seats a connection, if the room will have it. The seat id becomes
        /// the new <see cref="Player.Id"/> — deliberately not the connection
        /// id, so a seat outlives the connection sitting in it (Phase 6.1).
        /// </summary>
        public JoinResult TryAddPlayer(NetworkConnectionToClient conn, string requestedName, out Player player)
        {
            player = null;
            var accepted = CanAccept();
            if (accepted != JoinResult.Ok) return accepted;

            int seatId = _nextSeatId++;
            player = new Player(seatId, SanitizeName(requestedName));
            _players.Add(player);
            Channel.Bind(seatId, conn);
            _seatByConnection[conn.connectionId] = seatId;
            // First one in owns the room.
            if (OwnerSeatId == 0) OwnerSeatId = seatId;

            BroadcastLobbyState();
            return JoinResult.Ok;
        }

        /// <summary>Longest name a player may end up with, in characters.</summary>
        private const int MaxNameLength = 16;

        /// <summary>
        /// Turns what a player asked to be called into what this room will
        /// call them.
        ///
        /// <para>Sanitizing happens <b>here</b>, on the server, and nowhere
        /// else. The name arrives over the wire and is then displayed to
        /// *other* people, so the client that supplied it is exactly the party
        /// that must not be trusted with it. Doing it in one place also means
        /// every display site — seat labels, the lobby roster, "current
        /// player", server logs — is covered without any of them knowing.</para>
        ///
        /// <para>The rule that matters most is stripping <c>&lt;</c> and
        /// <c>&gt;</c>: those labels are rendered by TextMeshPro, which parses
        /// markup. A player calling themselves <c>&lt;size=400&gt;Вася</c>
        /// would wreck the layout on everyone else's screen, not their own.</para>
        /// </summary>
        private string SanitizeName(string requested)
        {
            var cleaned = new StringBuilder(MaxNameLength);
            bool lastWasSpace = false;

            foreach (var c in requested ?? string.Empty)
            {
                // Rich-text delimiters, control characters and newlines all go:
                // the first would let one player restyle everyone's UI, the
                // rest would break single-line labels.
                if (c == '<' || c == '>' || char.IsControl(c)) continue;

                if (char.IsWhiteSpace(c))
                {
                    // Collapse runs, and never start with a space.
                    if (lastWasSpace || cleaned.Length == 0) continue;
                    lastWasSpace = true;
                    cleaned.Append(' ');
                }
                else
                {
                    lastWasSpace = false;
                    cleaned.Append(c);
                }
                if (cleaned.Length >= MaxNameLength) break;
            }

            var name = cleaned.ToString().TrimEnd();
            if (name.Length == 0)
                name = $"Player{_players.Count + 1}";

            return MakeUnique(name);
        }

        /// <summary>
        /// Two people called "Саша" at one table is a worse experience than
        /// one of them being "Саша (2)". Compared case-insensitively, because
        /// "саша" and "Саша" are the same problem.
        /// </summary>
        private string MakeUnique(string name)
        {
            if (!IsNameTaken(name)) return name;
            for (int suffix = 2; suffix < 100; suffix++)
            {
                var candidate = $"{name} ({suffix})";
                if (!IsNameTaken(candidate)) return candidate;
            }
            // Unreachable at four seats; a seat id is at least unambiguous.
            return $"{name} #{_nextSeatId}";
        }

        private bool IsNameTaken(string name) =>
            _players.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

        public bool HasConnection(int connectionId) => _seatByConnection.ContainsKey(connectionId);

        /// <summary>
        /// Releases whatever seat this connection held and applies the
        /// departure policy. Returns false if the connection wasn't seated
        /// here at all.
        /// </summary>
        public bool RemoveConnection(int connectionId)
        {
            if (!_seatByConnection.TryGetValue(connectionId, out int seatId)) return false;
            _seatByConnection.Remove(connectionId);
            Channel.Unbind(seatId);
            OnSeatVacated(seatId);
            return true;
        }

        /// <summary>
        /// The one place that decides what a player leaving means. Called
        /// after the seat has been unbound from its connection but while the
        /// seat itself still exists.
        ///
        /// <para><b>Политика с 2026-08-31: партия продолжается без ушедшего,
        /// пока за столом остаётся хотя бы двое.</b> Раньше любой уход
        /// завершал комнату — на двоих иначе и нельзя, но при трёх-четырёх
        /// игроках это наказывало всех за одного. Комната завершается только
        /// тогда, когда играть станет не с кем.</para>
        ///
        /// <para>This is also the seam for reconnect. The seat is deliberately
        /// left in <c>_players</c> rather than trimmed, so the id in
        /// GameAbortedEvent still resolves to a name on the clients — and so a
        /// future reconnect flow has a seat to hand back. Adding it means
        /// replacing the immediate AbortGame with a grace window, and
        /// rebinding this seat in the Channel when the player returns. Note
        /// that a grace window is not enough on its own: the room would also
        /// have to stop asking the missing seat for decisions while it waits,
        /// which is why this ends the room today instead of half-waiting.</para>
        /// </summary>
        private void OnSeatVacated(int seatId)
        {
            var player = _players.FirstOrDefault(p => p.Id == seatId);
            string name = player?.Name ?? $"Player {seatId}";

            if (!_gameStarted)
            {
                _players.RemoveAll(p => p.Id == seatId);
                // Ушёл из лобби — стул освободился.
                _chairBySeat.Remove(seatId);
                Debug.Log($"[Room {Code}] {name} left the lobby: {_players.Count}/{_maxPlayers}");
                BroadcastLobbyState();
                return;
            }

            // За столом останется достаточно народу — играем дальше без него.
            // Считаем по списку движка, а не по ростеру: ростер держит места
            // ушедших (задел под переподключение), движок — только тех, чей
            // ход ещё случится.
            int remaining = _session?.Context.Players.Count(p => p.Id != seatId) ?? 0;
            if (remaining >= 2)
            {
                Debug.LogWarning($"[Room] {name} (seat {seatId}) left mid-game — continuing with {remaining}.");
                DropPlayerFromGame(seatId, name);
                return;
            }

            Debug.LogWarning($"[Room] {name} (seat {seatId}) left mid-game, {remaining} left — ending the room.");
            AbortGame(seatId, $"{name} покинул игру. Партия завершена.");
        }

        /// <summary>
        /// Убирает игрока из идущей партии, не завершая её.
        ///
        /// <para>Порядок шагов существенный. Сначала закрываем его зависшие
        /// решения — иначе чужой эффект, ждущий его ответа, повиснет навсегда.
        /// Потом возвращаем карты, пока игрок ещё в списке и его карты можно
        /// найти. Только затем убираем из очереди ходов. И в самом конце,
        /// если это был его ход, снимаем ожидание карты — оно разбудит
        /// игровой цикл, и к этому моменту всё уже должно быть прибрано.</para>
        /// </summary>
        private void DropPlayerFromGame(int seatId, string name)
        {
            var ctx = _session.Context;
            var player = ctx.Players.FirstOrDefault(p => p.Id == seatId);
            if (player == null) return;

            bool wasCurrent = ctx.GameState.GetCurrentPlayer() == player;
            string reason = $"{name} покинул игру.";

            // Сначала закрыть саму возможность спросить его о чём-то ещё:
            // его карта может доигрывать эффект прямо сейчас и задать
            // следующий вопрос уже после этой строки.
            _router?.MarkPlayerDeparted(seatId);

            if (wasCurrent)
            {
                // Его ход всё равно раскручиваем — отвечать за него незачем.
                _router?.CancelForPlayer(seatId, reason);
            }
            else
            {
                // Идёт чужой ход, и эффект ждёт ответа именно от ушедшего.
                // Отменить его значило бы бросить исключение в середину
                // чужого хода: карта сыграна, очки начислены, стол остался бы
                // недоделанным. Поэтому отвечаем за него по умолчанию.
                _router?.AutoResolveForPlayer(seatId, reason);
            }

            ReturnPlayerCardsToGame(player);

            ctx.GameState.RemovePlayer(player);
            _currentTurnAbandoned = wasCurrent;

            Channel.SendToRoom(new PlayerLeftEvent { PlayerId = seatId, Reason = reason });

            if (wasCurrent)
            {
                // Разбудит игровой цикл: он поймает отмену, поймёт, что ход
                // не состоялся, и начнёт следующий — очередь уже сдвинута.
                _waitingForPlay?.TrySetCanceled();
            }
        }

        /// <summary>
        /// Разбирает стол ушедшего игрока.
        ///
        /// <para><b>Рука — обратно в колоду.</b> Партия кончается ровно тогда,
        /// когда колода иссякла, так что выбросить пять карт из игры значило
        /// бы укоротить её остальным. Колода после возврата тасуется: состав
        /// его руки видели все за столом.</para>
        ///
        /// <para><b>Карты перед ним — в сброс.</b> Они уже сыграны, и вернуть
        /// их в колоду значило бы дать разыграть того же Огра дважды.</para>
        ///
        /// <para><b>А карты на общем столе остаются лежать.</b> Владелец у
        /// них — просто запись о том, кто их выложил; сама карта после
        /// разыгровки общая, и по ней работают чужие эффекты: Дракон
        /// сбрасывает Места, Принцесса и Огр забирают Мужчин, Купец считает
        /// Купцов. Смести их вместе с личными баффами значило бы молча
        /// уменьшить общий стол, на который рассчитывали остальные. Вреда от
        /// того, что они останутся, нет: их эффекты либо мгновенные и уже
        /// отработали, либо привязаны к ходу владельца, который не наступит.</para>
        /// </summary>
        private void ReturnPlayerCardsToGame(Player player)
        {
            var ctx = _session.Context;
            var gm = _session.GameManager;

            var hand = player.Hand.ToList();
            foreach (var card in hand)
            {
                player.RemoveCardFromHand(card);
                card.Owner = null;
                card.Position = CardPosition.InDeck;
                Channel.SendToRoom(new CardReturnedToDeckEvent { CardId = card.Id });
            }
            int returned = ctx.Deck.ReturnCardsAndShuffle(hand);

            // Только личные баффы: фильтр по позиции, а не по владельцу.
            // GetCardsOnBoard(player) ловит и то, что он выложил на общий
            // стол, — а это уже не его карты (см. комментарий выше).
            var personal = ctx.GameBoard.GetCardsOnBoard(player)
                .Where(c => c.Position == CardPosition.BeforePlayer)
                .ToList();

            foreach (var card in personal)
            {
                ctx.GameBoard.RemoveCardFromBoard(card);
                // Через GameManager, а не через доску напрямую: он разошлёт
                // событие, и клиенты уберут карту сами.
                gm.PutCardToDiscardPile(card);
            }

            Debug.Log($"[Room] {player.Name}: {returned} card(s) back to the deck, " +
                      $"{personal.Count} personal card(s) to the discard pile; " +
                      $"his cards on the common table stay.");
        }

        public void BroadcastLobbyState()
        {
            if (!NetworkServer.active) return;

            var chairSeats = new int[_maxPlayers];
            var chairNames = new string[_maxPlayers];
            for (int i = 0; i < _maxPlayers; i++) chairNames[i] = string.Empty;

            foreach (var pair in _chairBySeat)
            {
                int chair = pair.Value;
                if (chair < 0 || chair >= _maxPlayers) continue;
                chairSeats[chair] = pair.Key;
                chairNames[chair] = _players.FirstOrDefault(p => p.Id == pair.Key)?.Name ?? string.Empty;
            }

            Channel.SendToRoom(new LobbyStateUpdate
            {
                PlayerCount = _players.Count,
                MinPlayers = _minPlayers,
                MaxPlayers = _maxPlayers,
                PlayerNames = _players.Select(p => p.Name).ToArray(),
                CanStart = CanStart,
                ChairSeats = chairSeats,
                ChairNames = chairNames,
            });
        }

        /// <summary>
        /// Занять место за столом. Свободное — занимает, своё же — оставляет
        /// как есть, чужое — отказ.
        /// </summary>
        public void HandleClaimChair(NetworkConnectionToClient conn, ClaimChairIntent msg)
        {
            if (_gameStarted)
            {
                Debug.LogWarning($"[Room {Code}] ClaimChairIntent after the game started; ignored.");
                return;
            }
            if (msg.Chair < 0 || msg.Chair >= _maxPlayers)
            {
                Debug.LogWarning($"[Room {Code}] ClaimChairIntent for chair {msg.Chair} out of range.");
                return;
            }
            if (!_seatByConnection.TryGetValue(conn.connectionId, out int seatId))
            {
                Debug.LogWarning($"[Room {Code}] ClaimChairIntent from a connection with no seat.");
                return;
            }

            // Занято другим — молча отказываем: гонку за один стул выигрывает
            // тот, чей интент пришёл первым, и объяснять это игроку нечем,
            // кроме того же обновления лобби, которое и так придёт.
            foreach (var pair in _chairBySeat)
            {
                if (pair.Value == msg.Chair && pair.Key != seatId)
                {
                    Debug.Log($"[Room {Code}] Chair {msg.Chair} already taken by seat {pair.Key}.");
                    return;
                }
            }

            _chairBySeat[seatId] = msg.Chair;
            Debug.Log($"[Room {Code}] Seat {seatId} took chair {msg.Chair}.");
            BroadcastLobbyState();
        }

        /// <summary>
        /// Ставит игроков в том порядке, в котором они расселись, — и это
        /// единственное, что нужно движку: очерёдность ходов там и есть
        /// порядок списка (<c>GameState.CurrentPlayerIndex</c> бегает по
        /// нему), а <c>Player.Id</c> при этом не меняется ни у кого.
        ///
        /// <para>Кто не выбрал места, тех дописываем в конец в порядке
        /// прихода — так один молчун не держит комнату. Именно поэтому
        /// сортировка стабильная (<c>OrderBy</c>, а не <c>List.Sort</c>):
        /// у всех невыбравших ключ одинаковый, и нестабильная сортировка
        /// перемешала бы их между собой без всякой причины.</para>
        /// </summary>
        private void ApplyChosenTurnOrder()
        {
            if (_chairBySeat.Count == 0) return;

            var ordered = _players
                .OrderBy(p => _chairBySeat.TryGetValue(p.Id, out int chair) ? chair : int.MaxValue)
                .ToList();

            _players.Clear();
            _players.AddRange(ordered);

            Debug.Log($"[Room {Code}] Turn order: {string.Join(", ", _players.Select(p => p.Name))}.");
        }

        // ---- Composition root for this room's game ----

        /// <summary>
        /// Entry point for the owner's StartGameIntent. The check is against
        /// the owner's *seat*, so a stray intent from another player in the
        /// room — or from a connection that has since been reseated — is
        /// refused rather than trusted.
        /// </summary>
        public void HandleStartGame(NetworkConnectionToClient conn)
        {
            if (!Channel.IsSeatedAt(OwnerSeatId, conn))
            {
                Debug.LogWarning($"[Room {Code}] StartGameIntent from a non-owner; ignored.");
                return;
            }
            StartGame();
        }

        public void StartGame()
        {
            if (!CanStart)
            {
                Debug.LogWarning($"[Room] StartGame ignored: started={_gameStarted}, players={_players.Count}/min={_minPlayers}.");
                return;
            }
            // Порядок ходов фиксируется здесь, последним действием лобби и до
            // того, как список увидит движок.
            ApplyChosenTurnOrder();

            _gameStarted = true;
            Debug.Log($"[Room] Starting game with {_players.Count} players.");

            _router = new NetworkDecisionRouter(Channel);

            var notifier = new UnityNotifier("Server");
            var board = new GameBoard();
            var builder = new GameBuilder(notifier, board, _players);
            var gameManager = builder.Build(_router);

            // The server is the only authority on which rules are in play;
            // the ids go out in GameStartedEvent so clients rebuild the same
            // ones instead of hardcoding their own copy.
            var inGameRule = RuleCatalog.Create(_inGameRuleId);
            if (inGameRule == null)
            {
                Debug.LogError($"[Room] unknown in-game rule id {_inGameRuleId}; falling back to the default.");
                _inGameRuleId = RuleCatalog.DefaultInGameRuleId;
                inGameRule = RuleCatalog.Create(_inGameRuleId);
            }

            var finalRule = RuleCatalog.Create(_finalRuleId);
            if (finalRule == null)
            {
                Debug.LogError($"[Room] unknown final rule id {_finalRuleId}; falling back to the default.");
                _finalRuleId = RuleCatalog.DefaultFinalRuleId;
                finalRule = RuleCatalog.Create(_finalRuleId);
            }

            _session = new GameSession(gameManager, inGameRule, finalRule);
            _broadcaster = new ServerEventBroadcaster(_session, Channel);

            // Intent handlers are NOT registered here. Mirror keeps one per
            // message type process-wide, so claiming them per game is what
            // would break the moment a second room existed.
            // ServerIntentDispatcher owns them and routes by connection.

            // Host-model convenience: a host machine also runs a client, so
            // hand its UnGameManager the canonical session for host-only debug
            // tooling. Skipped on a dedicated server, where the local
            // UnGameManager belongs to nobody — and where handing it a room at
            // random (whichever started last) would be actively misleading.
            if (!GameConnectionManager.IsDedicatedServer && UnGameManager.Instance != null)
                UnGameManager.Instance.SetHostSession(_session);

            // Per-client GameStartedEvent so each recipient learns their own
            // LocalPlayerId from the same shared payload.
            var deckIds = _session.Context.Deck.GetCardIds().ToArray();
            var playerInfos = _players
                .Select(p => new PlayerInfo { Id = p.Id, Name = p.Name })
                .ToArray();
            var startPlayerId = _session.CurrentPlayer?.Id ?? _players[0].Id;

            foreach (var p in _players)
            {
                bool sent = Channel.SendToSeat(p.Id, new GameStartedEvent
                {
                    Players = playerInfos,
                    DeckOrder = deckIds,
                    StartPlayerId = startPlayerId,
                    LocalPlayerId = p.Id,
                    CurrentRuleId = _inGameRuleId,
                    CurrentFinalRuleId = _finalRuleId,
                    CardCatalogVersion = GameBuilder.CardCatalogVersion(),
                });
                if (!sent)
                    Debug.LogError($"[Room] missing connection for player {p.Id}");
            }

            // async void on a server-side entry point — exceptions land in
            // Unity's logger via the try/catch in RunGameLoopAsync.
            RunGameLoopAsync();
        }

        private async void RunGameLoopAsync()
        {
            try
            {
                var ctx = _session.Context;
                var gm = _session.GameManager;

                // Initial setup mirrors the legacy UnGameManager.StartGame
                // flow: place the night card, deal 5 to each player.
                var night = ctx.Deck.TakeCardByName("Ночь");
                if (night != null)
                    gm.PutCardInTimeOfDaySlot(night);

                foreach (var player in ctx.Players)
                {
                    for (int i = 0; i < 5; i++)
                    {
                        await Task.Delay(50);
                        ThrowIfGameOver();
                        gm.DrawCard(player);
                    }
                }

                while (!ctx.GameState.IsGameOver)
                {
                    // An abort that lands while the loop happens to be
                    // running rather than awaiting has nothing to cancel,
                    // so the loop has to check for itself — otherwise it
                    // sails on and parks on a fresh _waitingForPlay that
                    // nobody will ever answer.
                    ThrowIfGameOver();

                    var current = ctx.GameState.GetCurrentPlayer();
                    Channel.SendToRoom(new TurnAdvancedEvent
                    {
                        CurrentPlayerId = current.Id,
                        TurnCount = ctx.GameState.TurnCount,
                        IsNight = ctx.GameState.IsNight,
                    });

                    gm.DrawCard(current);

                    if (current.Hand.Count == 0)
                    {
                        gm.EndGame();
                        break;
                    }

                    try
                    {
                        _waitingForPlay = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                        int chosenCardId = await _waitingForPlay.Task;
                        var card = current.Hand.FirstOrDefault(c => c.Id == chosenCardId);
                        if (card == null)
                        {
                            Debug.LogWarning($"[Room] PlayCardIntent for unknown card {chosenCardId}; loop continues.");
                            continue;
                        }
                        await gm.PlayCard(card);
                        ThrowIfGameOver();

                        await gm.ActivateAllPlayerPermanentCardEffects(current);
                        ThrowIfGameOver();
                    }
                    catch (OperationCanceledException) when (!_gameOver)
                    {
                        // Ход не состоялся: игрок ушёл посреди него, и его
                        // ожидание карты (или решение) сняли. Фильтр !_gameOver
                        // и разводит два случая, выглядящих одинаково: при
                        // настоящем прерывании партии _gameOver уже поднят, и
                        // отмена летит дальше, наружу, где цикл и кончается.
                        Debug.Log($"[Room] turn of {current.Name} abandoned; continuing.");
                        _currentTurnAbandoned = false;
                        continue;
                    }

                    // Уход игрока уже сдвинул очередь за нас (см.
                    // GameState.RemovePlayer) — второй сдвиг перескочил бы
                    // через того, кто встал на его место.
                    if (_currentTurnAbandoned)
                    {
                        _currentTurnAbandoned = false;
                        continue;
                    }

                    ctx.GameState.NextTurn();
                }

                if (_gameOver) return;
                _gameOver = true;

                int winnerId = ctx.Players.OrderByDescending(p => p.Score).First().Id;
                Channel.SendToRoom(new GameEndedEvent
                {
                    WinnerId = winnerId,
                });
                Teardown();
            }
            catch (OperationCanceledException)
            {
                // Expected: AbortGame cancelled a parked decision or the
                // wait for a card, unwinding whatever effect was suspended.
                // AbortGame has already told the clients why.
                Debug.Log("[Room] turn loop stopped by abort.");
            }
            catch (Exception e)
            {
                // One room's engine fault must not take down the others, so
                // this catch is the boundary — nothing propagates past it.
                Debug.LogError($"[Room] turn loop: {e}");
                AbortGame(0, "Ошибка на сервере. Игра прервана.");
            }
        }

        /// <summary>
        /// Bails out of the turn loop if the room ended while we were away.
        /// Throwing (rather than returning a bool the caller might forget to
        /// check) puts every abort on the single OperationCanceledException
        /// path, whether the loop was suspended on a decision or just
        /// between statements.
        /// </summary>
        private void ThrowIfGameOver()
        {
            if (_gameOver)
                throw new OperationCanceledException("Game ended while the turn loop was running.");
        }

        // ---- Teardown ----

        /// <summary>
        /// Ends the game early and tells everyone why. Safe to call more than
        /// once and from any of the paths that can discover the room is
        /// doomed — a player leaving, an engine fault, the server shutting
        /// down.
        ///
        /// Cancelling the parked decisions is what actually unwedges the
        /// room: the suspended effect resumes by throwing, the turn loop
        /// unwinds, and nothing is left holding the session alive.
        /// </summary>
        public void AbortGame(int leftPlayerId, string reason)
        {
            // Nothing to abort before the game exists, and a game that
            // reached its natural end is over rather than aborted — don't
            // overwrite the winner screen with a teardown notice.
            if (!_gameStarted || _gameOver) return;
            _gameOver = true;
            _aborted = true;

            Debug.LogWarning($"[Room] Aborting game: {reason} (pending decisions: {PendingDecisionCount}).");

            if (NetworkServer.active)
            {
                Channel.SendToRoom(new GameAbortedEvent
                {
                    LeftPlayerId = leftPlayerId,
                    Reason = reason,
                });
            }

            // Order matters: release the turn loop's own wait first, then
            // the router's, so whichever the loop is sitting on throws.
            _waitingForPlay?.TrySetCanceled();
            _router?.CancelAll(reason);

            Teardown();
        }

        /// <summary>
        /// Releases this game's session and its parked decisions.
        ///
        /// Note what it does not do: unregister Mirror handlers. Those are
        /// process-wide and shared by every room, so a finished room pulling
        /// them down would silence all the others. Retiring a room is purely
        /// an index operation on the RoomRegistry, which the owner does when
        /// the last connection actually leaves.
        /// </summary>
        private void Teardown()
        {
            _router?.Dispose();
        }

        // ---- Intent handlers ----
        // Called by ServerIntentDispatcher once it has resolved that the
        // sender belongs to this room. Authorization (is it really this
        // player's turn) stays here, where the session is.

        public void HandlePlayCard(NetworkConnectionToClient conn, PlayCardIntent msg)
        {
            if (_gameOver) return;
            var current = _session?.CurrentPlayer;
            if (current == null) return;
            if (!Channel.IsSeatedAt(current.Id, conn))
            {
                Debug.LogWarning("[Room] PlayCardIntent from wrong connection.");
                return;
            }
            // TrySetResult rather than SetResult: an abort can cancel this
            // TCS between the IsCompleted check and the call.
            _waitingForPlay?.TrySetResult(msg.CardId);
        }

        public async void HandleUseRuleEffect(NetworkConnectionToClient conn, UseRuleEffectIntent msg)
        {
            if (_gameOver) return;
            var current = _session?.CurrentPlayer;
            if (current == null) return;
            if (!Channel.IsSeatedAt(current.Id, conn))
                return;

            // Правило применяется только ДО того, как разыграна карта хода.
            // Клиент это и так соблюдает (кнопка гаснет), но правило игры не
            // должно держаться на честности клиента: единственный признак
            // «карта ещё не сыграна» на сервере — что цикл всё ещё ждёт её.
            if (_waitingForPlay == null || _waitingForPlay.Task.IsCompleted)
            {
                Debug.LogWarning("[Room] UseRuleEffectIntent after the card was played; ignored.");
                ReportRuleOutcome(current.Id, msg.RuleEffectId, applied: false);
                return;
            }

            var effect = _session.CurrentRuleInGame.Effects.FirstOrDefault(e => e.Id == msg.RuleEffectId);
            if (effect == null)
            {
                ReportRuleOutcome(current.Id, msg.RuleEffectId, applied: false);
                return;
            }
            if (!effect.IsEffectAvailable(_session.Context))
            {
                Debug.LogWarning($"[Room] UseRuleEffectIntent for unavailable effect {msg.RuleEffectId}.");
                ReportRuleOutcome(current.Id, msg.RuleEffectId, applied: false);
                return;
            }

            bool applied = false;
            try
            {
                applied = await effect.ApplyEffect(_session.Context);
            }
            catch (OperationCanceledException e)
            {
                // Три нормальных исхода с одинаковым концом: игрок передумал
                // и отказался от выбора (DecisionDeclinedException), игрок
                // вышел, пока стол ждал его ответа, или комнату прервали.
                // Во всех случаях правило просто не состоялось, и откатывать
                // нечего: единственное правило с вопросом (A1-2) спрашивает
                // ДО того, как забрать меч, а остальные вопросов не задают.
                Debug.Log($"[Room] Rule effect {msg.RuleEffectId} did not complete: {e.Message}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Room] UseRuleEffectIntent application failed: {e}");
            }

            ReportRuleOutcome(current.Id, msg.RuleEffectId, applied);
        }

        /// <summary>
        /// Говорит игроку, состоялось ли его правило.
        ///
        /// <para>Ответ уходит из КАЖДОЙ ветки — включая отказы по проверкам.
        /// Клиент по этому событию возвращает игроку право на правило, если
        /// оно не сработало, и молчание здесь означало бы для него «поезд
        /// ушёл»: ровно та жалоба, ради которой событие и появилось.</para>
        ///
        /// <para>Только этому месту, а не всей комнате: остальным чужая
        /// неудавшаяся попытка ни о чём не говорит.</para>
        /// </summary>
        private void ReportRuleOutcome(int seatId, int ruleEffectId, bool applied)
        {
            if (_gameOver || !NetworkServer.active) return;

            Channel.SendToSeat(seatId, new RuleEffectResolvedEvent
            {
                RuleEffectId = ruleEffectId,
                Applied = applied,
            });
        }
    }
}
