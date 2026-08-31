using Assets.Libreries.ScaryTales;
using Assets.Libreries.ScaryTales.Abstractions;
using Assets.Libreries.ScaryTales.Rules;
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

    private bool _canChooseRule = false;

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
        _canChooseRule = (CurrentPlayer == LocalPlayer);
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
        _canChooseRule = false;
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
                StartCoroutine(PromptCardPick(evt.RequestId, evt.CandidateIds));
                break;
            case DecisionKind.PickItem:
                StartCoroutine(PromptItemPick(evt.RequestId, evt.CandidateIds));
                break;
            case DecisionKind.PickRuleEffect:
                StartCoroutine(PromptRuleEffectPick(evt.RequestId, evt.CandidateIds));
                break;
            case DecisionKind.Confirm:
                // No yes/no UI yet; default to "yes" to match the legacy
                // path's NotImplementedException behavior (which never
                // actually executed in the existing happy path).
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

    private IEnumerator PromptCardPick(int requestId, int[] candidateIds)
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

        while (!clicked) yield return null;

        foreach (var v in views)
        {
            v.OnCardClicked -= handler;
            v.SetHighlight(false);
        }

        if (chosen != null)
            NetworkClient.Send(new ResolveCardPickIntent { RequestId = requestId, CardId = chosen.Id });
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
        RuleContainer.Instance.Show(effects, false);

        while (!resolved) yield return null;

        RuleContainer.Instance.OnRuleSelected = null;

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

    public void ShowGameRules(bool openedByPlayer)
    {
        if (CurrentPlayer == LocalPlayer && _canChooseRule)
        {
            StartCoroutine(PlayerInitiateRule());
        }
        else
        {
            RuleContainer.Instance.Show(RuleEffects(), openedByPlayer);
        }
    }

    private IEnumerator PlayerInitiateRule()
    {
        IRuleEffect chosen = null;
        bool resolved = false;
        RuleContainer.Instance.OnRuleSelected = (e) =>
        {
            chosen = e;
            resolved = true;
        };
        RuleContainer.Instance.Show(RuleEffects(), false);

        while (!resolved) yield return null;

        RuleContainer.Instance.OnRuleSelected = null;

        if (chosen != null)
        {
            _canChooseRule = false;
            NetworkClient.Send(new UseRuleEffectIntent { RuleEffectId = chosen.Id });
        }
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
        _canChooseRule = false;

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
        _canChooseRule = false;

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
        _canChooseRule = false;

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
