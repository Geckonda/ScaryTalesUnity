using Assets.Libraries.ScaryTales;
using Assets.Libraries.ScaryTales.Abstractions;
using Assets.Libraries.ScaryTales.Rules;
using Assets.Scripts;
using Assets.Scripts.Menus;
using Assets.Scripts.Network;
using Assets.Scripts.Network.Messages;
using Assets.Scripts.Services;
using Assets.Scripts.UIEntities;
using Assets.Scripts.Utilities;
using Assets.Scripts.Views;
using Mirror;
using ScaryTales;
using ScaryTales.Abstractions;
using ScaryTales.Enums;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Phase 3 client-side host. Owns the ClientGameView and orchestrates
/// the active player's UI: drag-drop, rule prompts, server-initiated
/// decision prompts. On the host machine, also holds a reference to the
/// canonical GameSession so host-only debug tooling can reach into it.
///
/// The engine does NOT run on this MonoBehaviour anymore. State arrives
/// as DomainEvents that ClientGameView translates into in-process events
/// the UI subscribes to.
/// </summary>
public class UnGameManager : MonoBehaviour
{
    public static UnGameManager Instance { get; private set; }

    public ClientGameView ClientView { get; private set; }
    public GameSession HostSession { get; private set; } // non-null only on the host

    public CardViewService _cardViewService;
    public BoardUI _boardUI;
    public PlayerHandUI _playerHandUI;
    public TextUIManager _textUIManager;
    public SeatLayout _seatLayout;
    public Transform GameBoardPanel;
    public Transform Deck;

    // Rules are chosen by the server and arrive as ids in GameStartedEvent;
    // we rebuild them from RuleCatalog. Null until the game starts — every
    // reader must cope with that, because the rules panel is reachable from
    // a button that exists before the first game.
    private Rule _currentRuleInGame;
    private Rule _currentFinalRule;
    public Rule CurrentRuleInGame => _currentRuleInGame;
    public Rule CurrentFinalRule => _currentFinalRule;

    // Convenience accessors that always work on the client side.
    public Player LocalPlayer => ClientView?.LocalPlayer;
    // Legacy: returns the *first* opponent. With 3-4 players the rest are
    // accessed via ClientView.Opponents directly.
    public Player LocalOpponent => ClientView?.Opponents.FirstOrDefault();
    public Player CurrentPlayer => ClientView?.CurrentPlayer;

    // Forwarders that only work on the host (server-side state). Returning
    // null on non-host clients is intentional; nothing on the client path
    // should be reading these.
    public IGameContext _context => HostSession?.Context;
    public GameManager _gameManager => HostSession?.GameManager;
    public GameManager GameManager => HostSession?.GameManager;

    // Право применить правило разложено на два независимых факта, потому что
    // гаснут они от разных событий и восстанавливаются тоже по-разному.
    //
    // Раньше это был один флаг _canChooseRule, и он гас в момент ОТПРАВКИ
    // интента. Если игрок потом отказывался от выбора цели, правило не
    // применялось, а право было уже потрачено — «поезд ушёл», хотя картой
    // игрок ещё не ходил.
    private bool _myTurnCardPending;   // мой ход, и карта ещё не разыграна
    private bool _ruleUsedThisTurn;    // правило в этом ходу уже сработало

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }
        // Everything below is per-scene state living in a static, and a game
        // ends by reloading the scene — so without this it is the *previous*
        // game's state that the next one starts from.
        //
        // The three view services are plain C# classes, so Unity's fake-null
        // never applies to them: their cached factories still point at the
        // destroyed scene's Transforms, and a card created against a destroyed
        // parent gets no parent at all — which is how cards ended up loose in
        // the scene instead of in a hand.
        CardViewService.Reset();
        ItemViewService.Reset();
        RuleEffectService.Reset();
        // Left true by a game that ended while a card was selectable, which
        // made every card in the next game draggable from the start.
        DragAndDrop.SelectCard = false;
        // Would otherwise still point into a coroutine of the finished game.
        CardSelectionService.CurrentSelectionHandler = null;

        _cardViewService = CardViewService.Instance;

        // Construct the client mirror early so its NetworkClient handlers
        // are registered before GameStartedEvent arrives.
        ClientView = new ClientGameView();
        ClientView.OnGameStarted += HandleGameStarted;
        ClientView.OnTurnAdvanced += HandleTurnAdvanced;
        ClientView.OnDecisionRequested += HandleDecisionRequested;
        ClientView.OnDecisionResolved += HandleDecisionResolved;
        ClientView.OnGameEnded += HandleGameEnded;
        ClientView.OnGameAborted += HandleGameAborted;
        ClientView.OnPlayerLeft += HandlePlayerLeft;
        ClientView.OnRuleEffectResolved += HandleRuleEffectResolved;

        StartCoroutine(PumpClientEvents());
    }

    /// <summary>
    /// Прокачивает очередь событий клиента: следующее событие применяется
    /// только когда доиграли анимации предыдущего.
    ///
    /// <para>Это то самое «клиент буферизует события в очередь анимаций»,
    /// которое план Фазы 3 назвал предпочтительным вариантом и которое так и
    /// не было сделано. Без него сервер шлёт события на полной скорости:
    /// карты раздавались поверх ещё летящей карты дня/ночи, а запрос выбора
    /// приходил, пока стол ещё двигался.</para>
    ///
    /// <para>Живёт весь срок жизни объекта, включая меню: события начинают
    /// приходить раньше, чем игра стартует (GameStartedEvent идёт через ту же
    /// очередь), так что насос должен работать уже тогда.</para>
    /// </summary>
    [Tooltip("Через сколько секунд ожидания анимации писать в лог, что очередь событий встала. Только диагностика.")]
    [SerializeField] private float _eventStallWarningSeconds = 15f;

    private IEnumerator PumpClientEvents()
    {
        float waitingSince = -1f;

        while (true)
        {
            var animations = AnimationManager.Instance;
            bool blocked = animations != null && animations.IsBusy;

            if (ClientView.HasPendingEvents && !blocked)
            {
                // Ровно одно за раз. Обработчики UI запускают свои анимации
                // синхронно (до первого await) и тут же регистрируют их, так
                // что уже на следующем кадре IsBusy скажет правду.
                ClientView.ApplyNextEvent();
                waitingSince = -1f;
            }
            else if (ClientView.HasPendingEvents)
            {
                // Анимация, которая никогда не завершится, останавливает
                // очередь навсегда — игра просто замирает, и понять почему
                // неоткуда. Пусть хотя бы скажет.
                if (waitingSince < 0f)
                {
                    waitingSince = Time.unscaledTime;
                }
                else if (Time.unscaledTime - waitingSince > _eventStallWarningSeconds)
                {
                    Debug.LogWarning(
                        $"[UnGameManager] Очередь событий стоит {_eventStallWarningSeconds:0} с: " +
                        $"в ожидании {ClientView.PendingEventCount}, анимаций в полёте {animations?.ActiveCount ?? 0}.");
                    waitingSince = Time.unscaledTime;
                }
            }
            else
            {
                waitingSince = -1f;
            }

            yield return null;
        }
    }

    /// <summary>
    /// Called by the Room on the host machine after the
    /// canonical session is built. Non-host clients leave HostSession null.
    /// </summary>
    public void SetHostSession(GameSession session)
    {
        HostSession = session;
    }

    // ---- Lifecycle handlers driven by ClientGameView ----

    // Идёт ли партия на этом клиенте. Нужен ровно одному вопросу: если связь
    // оборвалась, есть ли что показывать игроку, или он ещё в лобби и
    // показывать нечего. См. HandleConnectionLost.
    private bool _inGame;

    private void HandleGameStarted()
    {
        _inGame = true;

        // Rebuild the server's rule choice from its ids. A null here means
        // this build doesn't know a rule the server used — a version
        // mismatch, worth shouting about rather than silently substituting.
        _currentRuleInGame = RuleCatalog.Create(ClientView.CurrentRuleId);
        _currentFinalRule = RuleCatalog.Create(ClientView.CurrentFinalRuleId);
        if (_currentRuleInGame == null)
            Debug.LogError($"[UnGameManager] server sent unknown in-game rule id {ClientView.CurrentRuleId}; rule UI will be empty.");
        if (_currentFinalRule == null)
            Debug.LogError($"[UnGameManager] server sent unknown final rule id {ClientView.CurrentFinalRuleId}.");

        // Position seats around the table based on the actual roster size,
        // then hand each UI component a reference to the seat layout so it
        // can read seat slots instead of caring about coordinates.
        if (_seatLayout != null)
            _seatLayout.Apply(ClientView.Players.Count);

        _boardUI.Initialize(ClientView, _seatLayout);
        _playerHandUI.Initialize(ClientView, _seatLayout);
        _textUIManager.Initialize(ClientView, _seatLayout);
    }

    private void HandleTurnAdvanced(int currentPlayerId)
    {
        DragAndDrop.SelectCard = false;
        _myTurnCardPending = (CurrentPlayer == LocalPlayer);
        _ruleUsedThisTurn = false;
        _textUIManager.RefreshTurnHighlight();

        if (CurrentPlayer == LocalPlayer)
        {
            EnablePlayerDrag(CurrentPlayer);
            StartCoroutine(WaitForLocalCardPlay(CurrentPlayer));
        }
    }

    private IEnumerator WaitForLocalCardPlay(Player player)
    {
        if (!Application.isPlaying) yield break;

        if (_playerHandUI._playerHandPanels == null
            || !_playerHandUI._playerHandPanels.TryGetValue(player, out var playerHandPanel))
        {
            Debug.LogError($"[UnGameManager] Hand panel for {player.Name} not found.");
            yield break;
        }

        bool cardSelected = false;
        Card selectedCard = null;
        Action<Card> onCardSelected = (card) =>
        {
            cardSelected = true;
            selectedCard = card;
        };
        CardSelectionService.CurrentSelectionHandler = onCardSelected;
        DragAndDrop.SelectCard = true;
        foreach (Transform t in playerHandPanel)
        {
            var dnd = t.GetComponent<DragAndDrop>();
            if (dnd != null) dnd.OnCardSelected += onCardSelected;
        }

        while (!cardSelected) yield return null;

        DragAndDrop.SelectCard = false;
        // Карта разыграна — правила на этот ход кончились.
        _myTurnCardPending = false;
        CardSelectionService.CurrentSelectionHandler = null;
        foreach (Transform t in playerHandPanel)
        {
            var dnd = t.GetComponent<DragAndDrop>();
            if (dnd != null) dnd.OnCardSelected -= onCardSelected;
        }

        if (selectedCard != null)
        {
            NetworkClient.Send(new PlayCardIntent { CardId = selectedCard.Id });
        }
        else
        {
            DisablePlayerDrag(CurrentPlayer);
        }
    }

    // ---- Server-initiated decision prompts ----

    private void HandleDecisionRequested(DecisionRequestedEvent evt)
    {
        bool isMine = LocalPlayer != null && evt.PlayerId == LocalPlayer.Id;

        // Подсветить того, от кого стол ждёт решения. Раньше во время
        // чужого выбора игра замирала молча, и пауза выглядела как зависание.
        // Показываем это тем же жестом, что и «чей ход» — цветом ника на его
        // месте, а не отдельной строкой.
        if (_textUIManager != null)
            _textUIManager.SetDeciding(evt.PlayerId);

        if (!isMine) return;

        switch ((DecisionKind)evt.Kind)
        {
            case DecisionKind.PickCard:
                StartCoroutine(PromptCardPick(evt.RequestId, evt.CandidateIds, evt.CanCancel));
                break;
            case DecisionKind.PickItem:
                StartCoroutine(PromptItemPick(evt.RequestId, evt.CandidateIds));
                break;
            case DecisionKind.PickRuleEffect:
                StartCoroutine(PromptRuleEffectPick(evt.RequestId, evt.CandidateIds));
                break;
            case DecisionKind.Confirm:
                // UI для «да/нет» не существует, и ни один эффект в игре
                // Confirm не запрашивает — поэтому сюда сегодня не попадают.
                // Но если попадут, молчаливое «да» будет худшим из возможных
                // поведений: игрок согласится с тем, чего не видел. Отвечаем,
                // чтобы не подвесить комнату, и кричим в лог.
                Debug.LogError($"[UnGameManager] Пришёл запрос Confirm (id {evt.RequestId}), " +
                               "а окна подтверждения в игре нет. Отвечаю «да» вслепую — " +
                               "нужен UI, прежде чем каким-либо эффектом пользоваться этим.");
                NetworkClient.Send(new ResolveConfirmIntent
                {
                    RequestId = evt.RequestId,
                    Confirmed = true,
                });
                break;
        }
    }

    private void HandleDecisionResolved(int requestId)
    {
        if (_textUIManager != null) _textUIManager.ClearDeciding();
    }

    // Отменяемый выбор карты, который ждёт ответа прямо сейчас, или 0.
    private int _cancellableCardPick;
    // Запрос, от которого игрок отказался. Отдельное поле, а не обнуление
    // предыдущего: если когда-нибудь два отменяемых запроса наложатся,
    // ожидающая корутина должна выйти только по СВОЕМУ отказу, а не потому,
    // что «текущим» стал чужой.
    private int _declinedCardPick;

    /// <summary>
    /// Отказаться от выбора, если игрока сейчас о чём-то спрашивают и от
    /// этого можно отказаться. Зовётся из <c>PauseMenu</c> по Esc.
    ///
    /// <para>Esc, а не отдельная кнопка, потому что выбор карты рисуется не
    /// панелью, а подсветкой самих карт на столе — вешать кнопку негде, и
    /// любая новая привязка в сцене имеет свойство остаться непривязанной.
    /// «Esc — назад» игрок угадывает без подсказки.</para>
    /// </summary>
    /// <returns>true, если отказ отправлен — тогда Esc не должен открывать меню.</returns>
    public bool TryCancelPendingDecision()
    {
        if (_cancellableCardPick == 0) return false;

        NetworkClient.Send(new ResolveCardPickIntent
        {
            RequestId = _cancellableCardPick,
            HasPick = false,
        });
        _declinedCardPick = _cancellableCardPick;
        _cancellableCardPick = 0;
        return true;
    }

    private IEnumerator PromptCardPick(int requestId, int[] candidateIds, bool canCancel)
    {
        var candidates = candidateIds
            .Select(id => ClientView.FindCard(id))
            .Where(c => c != null)
            .ToList();

        var views = new List<CardView>();
        foreach (var card in candidates)
        {
            var v = _cardViewService.GetCardView(card);
            if (v != null)
            {
                v.SetHighlight(true);
                views.Add(v);
            }
        }

        Card chosen = null;
        bool clicked = false;
        Action<Card> handler = (c) => { chosen = c; clicked = true; };
        foreach (var v in views) v.OnCardClicked += handler;

        if (canCancel) _cancellableCardPick = requestId;

        // Ждём клика по карте — или отказа по Esc именно от этого запроса.
        while (!clicked && _declinedCardPick != requestId)
            yield return null;

        bool cancelled = !clicked;
        if (_cancellableCardPick == requestId) _cancellableCardPick = 0;
        if (_declinedCardPick == requestId) _declinedCardPick = 0;

        foreach (var v in views)
        {
            v.OnCardClicked -= handler;
            v.SetHighlight(false);
        }

        // При отказе интент уже ушёл из TryCancelPendingDecision.
        if (!cancelled && chosen != null)
            NetworkClient.Send(new ResolveCardPickIntent
            {
                RequestId = requestId,
                CardId = chosen.Id,
                HasPick = true,
            });
    }

    private IEnumerator PromptItemPick(int requestId, int[] candidateTypes)
    {
        var items = candidateTypes
            .Select(t => MakeItemForType((ItemType)t))
            .Where(i => i != null)
            .ToList();

        var views = new List<ItemView>();
        foreach (var item in items)
        {
            var v = ItemViewService.Instance.CreateItemView(item, ItemContainer.Instance.contentPanel);
            if (v != null) views.Add(v);
        }
        ItemContainer.Instance.Show(views, false);

        Item chosen = null;
        bool clicked = false;
        Action<Item> handler = (i) => { chosen = i; clicked = true; };
        foreach (var v in views) v.OnItemClicked += handler;

        while (!clicked) yield return null;

        foreach (var v in views) v.OnItemClicked -= handler;
        ItemContainer.Instance.Hide();

        if (chosen != null)
            NetworkClient.Send(new ResolveItemPickIntent { RequestId = requestId, ItemType = (int)chosen.Type });
    }

    private IEnumerator PromptRuleEffectPick(int requestId, int[] candidateIds)
    {
        var available = RuleEffects();
        var effects = candidateIds
            .Select(id => available.FirstOrDefault(e => e.Id == id))
            .Where(e => e != null)
            .ToList();

        IRuleEffect chosen = null;
        bool resolved = false;
        RuleContainer.Instance.OnRuleSelected = (e) => { chosen = e; resolved = true; };
        // Закрытие крестиком и есть отказ: отдельной кнопки «Пропустить»
        // больше нет. Без этой подписки закрытие подвесило бы и корутину, и
        // комнату — сервер ждёт ответа на свой запрос.
        RuleContainer.Instance.OnClosed = () => { chosen = null; resolved = true; };
        RuleContainer.Instance.Show(effects, interactive: true, IsRuleEffectAvailable);

        while (!resolved) yield return null;

        RuleContainer.Instance.OnRuleSelected = null;
        RuleContainer.Instance.OnClosed = null;

        NetworkClient.Send(new ResolveRuleEffectPickIntent
        {
            RequestId = requestId,
            HasPick = chosen != null,
            RuleEffectId = chosen?.Id ?? 0,
        });
    }

    private static Item MakeItemForType(ItemType type)
    {
        foreach (var tpl in GameBuilder.MakeItemTemplates())
            if (tpl.Type == type) return tpl.Clone();
        return null;
    }

    // ---- Player-initiated rule UI ----

    /// <summary>
    /// Effects of the rule the server picked, or an empty list before the
    /// game has started (the rules button exists on the menu screen too).
    /// Note that Rule.Effects allocates a fresh list on every call, so read
    /// it once per use rather than in a loop.
    /// </summary>
    private List<IRuleEffect> RuleEffects() =>
        _currentRuleInGame?.Effects ?? new List<IRuleEffect>();

    /// <summary>
    /// Можно ли прямо сейчас применить правило: свой ход и карта этого хода
    /// ещё не разыграна. Второе гасится в WaitForLocalCardPlay сразу после
    /// выбора карты.
    ///
    /// <para>На возможность ОТКРЫТЬ таблицу это не влияет — смотреть правила
    /// можно когда угодно.</para>
    /// </summary>
    private bool CanUseRuleNow => CurrentPlayer != null
                                  && CurrentPlayer == LocalPlayer
                                  && _myTurnCardPending
                                  && !_ruleUsedThisTurn;

    /// <summary>
    /// Кнопка «Правила» в сцене. Всегда открывает таблицу; кликабельность
    /// эффектов зависит от <see cref="CanUseRuleNow"/>.
    ///
    /// <para>Корутины здесь больше нет намеренно. Прежняя ждала выбора в
    /// <c>while (!resolved)</c>, а крестик привязан в сцене прямо к
    /// <c>RuleContainer.Hide()</c> и её не будил — корутина оставалась жить
    /// вечно, держа ссылку на свой обработчик. Игроку это и виделось как
    /// «открыл, закрыл, и правило больше не нажимается». Открытию таблицы
    /// ждать нечего: либо игрок кликнет эффект, либо закроет.</para>
    /// </summary>
    /// <param name="openedByPlayer">
    /// Не используется. Параметр остался, потому что кнопка в сцене привязана
    /// к сигнатуре с bool.
    /// </param>
    public void ShowGameRules(bool openedByPlayer)
    {
        bool interactive = CanUseRuleNow;

        RuleContainer.Instance.OnClosed = null;
        RuleContainer.Instance.OnRuleSelected = interactive ? UseRuleEffect : null;
        RuleContainer.Instance.Show(RuleEffects(), interactive, IsRuleEffectAvailable);
    }

    // Контекст только для чтения поверх зеркала — единственное, для чего он
    // нужен, это спросить у правила, выполнены ли его условия.
    private ClientRuleContext _ruleContext;

    /// <summary>
    /// Выполнены ли условия конкретного правила по данным клиента: есть ли
    /// нужный предмет, есть ли что брать из сброса, есть ли монстр на столе.
    ///
    /// <para>Считается ТЕМ ЖЕ методом, что и на сервере
    /// (<c>IRuleEffect.IsEffectAvailable</c>), просто поверх клиентского
    /// снимка мира. Дублировать условия на клиенте нельзя: копия разъедется
    /// при первой правке правил, а разъехавшаяся подсветка врёт игроку.</para>
    ///
    /// <para>Ответ — подсказка, а не разрешение: сервер проверяет условия
    /// заново. Поэтому исключение здесь не должно ломать окно — считаем, что
    /// подсвечивать нечего, и пишем в лог.</para>
    /// </summary>
    private bool IsRuleEffectAvailable(IRuleEffect effect)
    {
        if (effect == null || ClientView == null) return false;

        try
        {
            _ruleContext ??= new ClientRuleContext(ClientView);
            return effect.IsEffectAvailable(_ruleContext);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[UnGameManager] Не удалось оценить доступность правила {effect.Id}: {e.Message}");
            return false;
        }
    }

    private void UseRuleEffect(IRuleEffect effect)
    {
        if (effect == null) return;
        // Ещё раз, уже по факту клика: между открытием таблицы и нажатием
        // ход мог смениться или карта — разыграться.
        if (!CanUseRuleNow) return;

        // Гасим сразу, чтобы не отправить второй интент, пока первый в пути.
        // Если правило не состоится, сервер скажет об этом
        // RuleEffectResolvedEvent, и право вернётся.
        _ruleUsedThisTurn = true;
        NetworkClient.Send(new UseRuleEffectIntent { RuleEffectId = effect.Id });
    }

    /// <summary>
    /// Сервер сообщил, чем кончилась попытка применить правило.
    ///
    /// <para>Право на правило возвращается, если оно НЕ сработало: игрок
    /// отказался от выбора цели, условия не сошлись, интент опоздал. Ходить
    /// картой он ещё не ходил, так что и права лишаться не за что.</para>
    ///
    /// <para>Решает это именно сервер, а не клиент по факту своего отказа:
    /// отказ — лишь одна из причин, по которым правило может не состояться,
    /// и только сервер знает про все.</para>
    /// </summary>
    private void HandleRuleEffectResolved(bool applied)
    {
        if (applied) return;
        if (!_myTurnCardPending) return; // карта уже сыграна — поздно

        _ruleUsedThisTurn = false;
    }

    // ---- Misc UI ----

    public void ShowLocalPlayerItemBag()
    {
        if (LocalPlayer == null) return;
        var items = LocalPlayer.ShowItemsFromItemBag();
        ItemContainer.Instance.Show(items);
    }

    private void HandleGameEnded(int winnerId)
    {
        // Партия кончилась — больше ничего не тащим и правил не выбираем.
        // Раньше это делал за нас уход в меню через пять секунд; теперь
        // экран результата висит, пока игрок сам не решит уйти.
        _inGame = false;
        DragAndDrop.SelectCard = false;
        _myTurnCardPending = false;

        var winner = ClientView.FindPlayer(winnerId);
        ResultContainer.Instance.ShowWinner(winner?.Name ?? "?");

        // Автовыхода отсюда нет намеренно: когда уходить — решает игрок.
        // Уводят в меню кнопка «Выйти» на экране результата и меню по Esc,
        // и обе зовут один и тот же GameConnectionManager.ReturnToMenu().
    }

    /// <summary>
    /// The server ended the game early — today, because somebody left.
    /// Same result panel as a normal finish, and the same "leave when you
    /// choose to" rule, but the text says what happened instead of naming
    /// a winner.
    /// </summary>
    private void HandleGameAborted(string reason, Player leftPlayer)
    {
        // Stop any prompt coroutine that is still waiting on a click for a
        // decision the server has already given up on.
        StopAllCoroutines();
        _inGame = false;
        DragAndDrop.SelectCard = false;
        _myTurnCardPending = false;

        // Null on a teardown that races the scene reload; the log line is
        // then the only record, which is fine — we're leaving anyway.
        if (ResultContainer.Instance != null)
        {
            ResultContainer.Instance.ShowMessage(
                string.IsNullOrEmpty(reason) ? "Игра прервана." : reason);
        }
        Debug.LogWarning($"[Client] Game aborted (left: {leftPlayer?.Name ?? "n/a"}): {reason}");

        // Как и при обычном конце партии, в меню не уходим сами: игрок
        // должен успеть прочитать, почему всё оборвалось.
    }

    /// <summary>
    /// Игрок вышел, но партия продолжается — за столом осталось достаточно
    /// народу. В отличие от <see cref="HandleGameAborted"/> здесь ничего не
    /// останавливается: гасим место ушедшего и играем дальше.
    /// </summary>
    private void HandlePlayerLeft(string reason, Player player)
    {
        // Говорим об этом местом, а не строкой текста: по решению из пункта 3
        // отдельная строка-уведомление убрана, а место ушедшего гаснет и
        // подписывается «(вышел)» — это видно всю оставшуюся партию, тогда как
        // всплывающий текст успел бы смениться следующим же ходом.
        _textUIManager?.MarkPlayerLeft(player);

        Debug.Log($"[Client] {player?.Name ?? "?"} left; game continues: {reason}");
    }

    /// <summary>
    /// Связь оборвалась не по нашей воле: сервер остановлен, хост вышел,
    /// сеть отвалилась. Показывает экран результата с причиной — партия
    /// кончается для игрока так же, как от любой другой причины, и уходить
    /// он должен сам.
    ///
    /// <para>Возвращает <c>false</c>, если показывать нечего (мы ещё в лобби
    /// или меню) — тогда зовущему остаётся обычный возврат в меню.</para>
    /// </summary>
    public bool HandleConnectionLost()
    {
        var result = ResultContainer.Instance;
        if (result == null) return false;

        // Партия уже кончилась, и её итог на экране. Обрыв связи после этого
        // — ожидаемое следствие, а не новость: не затираем «Победитель: …»
        // техническим сообщением, просто остаёмся на нём.
        if (result.IsShowing) return true;

        if (!_inGame) return false;

        StopAllCoroutines();
        _inGame = false;
        DragAndDrop.SelectCard = false;
        _myTurnCardPending = false;

        result.ShowMessage("Связь с сервером потеряна. Партия завершена.");
        Debug.LogWarning("[Client] Connection lost mid-game.");
        return true;
    }

    public void EnablePlayerDrag(Player player)
    {
        if (_playerHandUI._playerHandPanels == null) return;
        if (!_playerHandUI._playerHandPanels.TryGetValue(player, out var handPanel))
            return;

        foreach (Transform child in handPanel)
        {
            var cardView = child.GetComponent<CardView>();
            if (cardView != null)
                _cardViewService.CardViewFactory.EnableDrag(cardView);
        }
    }

    public void DisablePlayerDrag(Player player)
    {
        if (_playerHandUI._playerHandPanels == null) return;
        if (!_playerHandUI._playerHandPanels.TryGetValue(player, out var handPanel))
            return;

        foreach (Transform child in handPanel)
        {
            var cardView = child.GetComponent<CardView>();
            if (cardView != null)
                _cardViewService.CardViewFactory.DisableDrag(cardView);
        }
    }
}
