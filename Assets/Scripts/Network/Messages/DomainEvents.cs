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

        /// <summary>
        /// Отпечаток каталога карт на сервере (<c>GameBuilder.CardCatalogVersion</c>).
        /// Клиент сверяет со своим: id карт назначаются позиционно по списку
        /// шаблонов, так что разные сборки понимают одни и те же номера
        /// по-разному — и партия молча начинает показывать не те карты.
        /// </summary>
        public int CardCatalogVersion;
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
    /// Игрок смотрит верх колоды: над ней приподнимается столько-то рубашек.
    /// Чистый показ — в состоянии партии не меняется ничего, карты остаются в
    /// колоде, пока игрок не ответил.
    ///
    /// <para>Номеров карт здесь нет намеренно: показываются рубашки, и какие
    /// за ними карты — знать не нужно никому, включая клиент смотрящего (у
    /// того есть окно выбора со своими картами).</para>
    /// </summary>
    public struct CardsPeekedEvent : NetworkMessage
    {
        public int PlayerId;
        public int Count;
    }

    /// <summary>
    /// Карта убрана из партии совсем: ни в колоде, ни в руке, ни на столе, ни
    /// в сбросе. Вернуть её нечем — тем и отличается от сброса.
    ///
    /// <para>Показывать тут нечего (карта нигде и не появлялась), но зеркало
    /// клиента обязано об этом узнать: иначе она навсегда останется числиться
    /// в его порядке колоды. Зеркало, которое врёт, уже стоило этому проекту
    /// одного расследования.</para>
    /// </summary>
    public struct CardRemovedFromGameEvent : NetworkMessage
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

        /// <summary>
        /// Можно ли отказаться от выбора. Решает сервер, клиент только
        /// показывает: у эффекта разыгранной карты выбор обязателен, а вот
        /// правило A1-2 спрашивает до того, как что-либо потратить, и там
        /// отказ безобиден.
        /// </summary>
        public bool CanCancel;
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

        /// <summary>
        /// Выбор карты, которой НЕТ на столе, — сегодня это верх колоды
        /// (Тайная помощница).
        ///
        /// <para>Отдельный вид, а не флаг у <see cref="PickCard"/>, потому что
        /// клиент показывает это принципиально иначе: обычный выбор рисуется
        /// подсветкой карт на столе, а у карты из колоды представления нет
        /// вовсе. Отдай мы её обычному пути — кандидаты молча выпали бы из
        /// списка, ожидание клиента повисло бы в цикле, а комната осталась бы
        /// стоять на неотвеченном вопросе.</para>
        /// </summary>
        PickCardFromDeck = 4,
    }

    /// <summary>
    /// Game-over signal.
    ///
    /// <para><c>WinnerIds</c> — <b>все</b> игроки с наибольшим счётом, а не
    /// один. Ничья в этой игре обычное дело: очки набираются десятками, а
    /// тайбрейкера в правилах нет. Раньше здесь ехал единственный
    /// <c>WinnerId</c>, и при равенстве побеждал тот, кто раньше сел за стол,
    /// — правило, которого нет ни в правилах игры, ни на экране.</para>
    ///
    /// <para>Порядок в массиве — по местам за столом, чтобы у всех клиентов
    /// список читался одинаково.</para>
    /// </summary>
    public struct GameEndedEvent : NetworkMessage
    {
        public int[] WinnerIds;
        // Итоговые очки здесь когда-то ехали параллельным массивом к
        // GameStartedEvent.Players и не читались ни одним клиентом: счёт и так
        // живёт на местах и обновляется PointsAwardedEvent. Убрано 2026-08-31.
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

    /// <summary>
    /// Игрок вышел, но партия продолжается без него — за столом осталось
    /// достаточно народу.
    ///
    /// <para>Отличается от <see cref="GameAbortedEvent"/> ровно тем, что это
    /// не конец: клиент убирает игрока из своего зеркала и порядка ходов и
    /// играет дальше. Комната завершается только тогда, когда остаться
    /// вдвоём уже нельзя.</para>
    /// </summary>
    public struct PlayerLeftEvent : NetworkMessage
    {
        public int PlayerId;
        public string Reason; // display text, already localized by the server
    }

    /// <summary>
    /// Карта вернулась из руки в колоду. Сегодня единственная причина —
    /// уход игрока: его рука не должна пропасть из игры, потому что партия
    /// кончается ровно тогда, когда иссякла колода.
    ///
    /// <para>Своё событие, а не <see cref="CardMovedToDiscardPileEvent"/>,
    /// потому что сброс — это игровая зона, из которой карты возвращаются
    /// эффектами: соврав про неё, мы бы развели зеркало клиента с сервером.</para>
    /// </summary>
    public struct CardReturnedToDeckEvent : NetworkMessage
    {
        public int CardId;
    }

    /// <summary>
    /// Чем кончилась попытка применить правило. Уходит только тому, кто
    /// пытался.
    ///
    /// <para><c>Applied == false</c> означает, что правило не сработало:
    /// игрок отказался от выбора цели, условия не сошлись, интент опоздал.
    /// Клиент по этому признаку возвращает игроку право на правило — картой
    /// он ещё не ходил, значит терять право не за что.</para>
    ///
    /// <para>Отвечает именно сервер, потому что отказ — лишь одна из причин
    /// неудачи, и только он знает про все остальные.</para>
    /// </summary>
    /// <summary>
    /// В колоде осталось столько-то карт.
    ///
    /// <para>Клиент не может посчитать это сам: не всё, что покидает колоду,
    /// сопровождается событием — карта Ночи уходит в слот времени суток
    /// напрямую, а Волшебник раскрывает карту, минуя руку. Поэтому число
    /// шлёт тот, кто им владеет.</para>
    /// </summary>
    public struct DeckCountChangedEvent : NetworkMessage
    {
        public int Remaining;
    }

    public struct RuleEffectResolvedEvent : NetworkMessage
    {
        public int RuleEffectId;
        public bool Applied;
    }

    /// <summary>
    /// Все игроки показывают карты на руках (Цыганка).
    ///
    /// <para><b>Карты в событии не едут, и это не экономия.</b> Клиент и так
    /// знает каждую карту в каждой руке: взятие рассылается всей комнате
    /// (<see cref="CardDrawnEvent"/> через <c>SendToRoom</c>), а порядок колоды
    /// целиком приезжает в <see cref="GameStartedEvent"/>. Чужие руки рисуются
    /// рубашкой не по незнанию, а по решению показа. Событие говорит ровно
    /// одно: пора эту рубашку на время убрать.</para>
    ///
    /// <para>Длительности здесь тоже нет: темп показа — дело клиента, а не
    /// игрового цикла на сервере (см. REFACTOR §6-7).</para>
    /// </summary>
    public struct HandsRevealedEvent : NetworkMessage
    {
        public int ByPlayerId;
        /// <summary>Открыть руки или вернуть их как было.</summary>
        public bool Revealed;
    }

    /// <summary>
    /// Одна карта выступает вперёд из раскрытой руки — или возвращается.
    ///
    /// <para>Шаг сцены Цыганки: каждое место выезжает, приносит очко и
    /// уезжает; каждая цыганка выезжает, приносит карту и уезжает. Выезд и
    /// заезд — два отдельных события, потому что между ними сервер шлёт
    /// начисление очка или взятие карты, и очередь событий у клиента
    /// расставляет их именно в этом порядке.</para>
    /// </summary>
    public struct CardShowcasedEvent : NetworkMessage
    {
        public int CardId;
        public bool StepOut;
    }

    /// <summary>
    /// Игровой цикл ждёт от игрока карту. Приходит перед КАЖДЫМ ожиданием —
    /// и перед обязательной картой хода, и перед каждой дополнительной.
    ///
    /// <para><b>Почему отдельное событие, а не <see cref="TurnAdvancedEvent"/>.</b>
    /// Смена хода и «жду карту» перестали совпадать: в одном ходу карт может
    /// быть несколько. Раньше клиент взводил ожидание по смене хода и другого
    /// повода не знал — теперь сервер говорит прямо, а смена хода занимается
    /// только своим делом (подсветка, сброс флажков).</para>
    /// </summary>
    public struct AwaitingCardPlayEvent : NetworkMessage
    {
        public int PlayerId;

        /// <summary>
        /// Можно ли не играть. У обязательной карты хода — нет; у
        /// дополнительной — да, её дают со словом «можно».
        /// </summary>
        public bool CanDecline;
    }


    /// <summary>
    /// Начался разбор правила конца партии. Идёт перед первым
    /// <see cref="FinalScoringEvent"/> и нужен показу: под него клиент
    /// показывает баннер с правилом, чтобы дальнейшая раздача очков не
    /// выглядела беспричинной.
    ///
    /// <para>Едет номер правила, а не его текст: правила клиент и так строит
    /// сам из <c>RuleCatalog</c> по номеру из <see cref="GameStartedEvent"/>.</para>
    /// </summary>
    public struct FinalScoringStartedEvent : NetworkMessage
    {
        public int RuleId;
    }

    /// <summary>
    /// Правило конца партии посчитано одному игроку.
    ///
    /// <para><b>Уходит ДО начисления очков.</b> В очереди клиента получается
    /// «разбор игрока — его очки — разбор следующего», и насос выпускает очки
    /// только после того, как отыграла анимация разбора: счёт на табличке
    /// подскакивает ровно тогда, когда в неё влетают предметы. Обратный
    /// порядок обессмыслил бы всю сцену.</para>
    ///
    /// <para><see cref="CountedItemTypes"/> — <c>ItemType</c> <b>с повторами</b>
    /// ([Coin, Coin, Coin, MagicStick]). Типы, а не идентификаторы предметов:
    /// id на сервере и на клиенте расходятся после первого же снятия предмета
    /// правилом (см. <c>ClientGameView.HandleItemRemoved</c>), а монеты
    /// взаимозаменяемы — «подсветить три монеты из пяти» и есть то, что нужно.</para>
    ///
    /// <para>Событие приходит <b>каждому</b> игроку, включая тех, кому правило
    /// ничего не дало: очередь должна обойти стол целиком, иначе непонятно,
    /// разобрали игрока или пропустили. У них <see cref="Points"/> равен нулю,
    /// и своего <c>PointsAwardedEvent</c> следом не будет — начисление нуля на
    /// сервере молчит.</para>
    /// </summary>
    public struct FinalScoringEvent : NetworkMessage
    {
        public int PlayerId;
        public int RuleEffectId;
        public int Points;
        public int[] CountedItemTypes;
    }
}
