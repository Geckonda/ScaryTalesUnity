using Assets.Libreries.ScaryTales;
using Assets.Libreries.ScaryTales.Abstractions;
using Assets.Libreries.ScaryTales.Rules.Templates.A;
using Assets.Libreries.ScaryTales.Rules.Templates.B;
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

    // Hardcoded rules — every machine builds the same defaults so the
    // active client knows their rule options without polling the server.
    // Phase 4 lobby will let the host pick.
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
        _cardViewService = CardViewService.Instance;

        _currentRuleInGame = new A1();
        _currentFinalRule = new B2();

        // Construct the client mirror early so its NetworkClient handlers
        // are registered before GameStartedEvent arrives.
        ClientView = new ClientGameView();
        ClientView.OnGameStarted += HandleGameStarted;
        ClientView.OnTurnAdvanced += HandleTurnAdvanced;
        ClientView.OnDecisionRequested += HandleDecisionRequested;
        ClientView.OnGameEnded += HandleGameEnded;
    }

    /// <summary>
    /// Called by GameNetworkController on the host machine after the
    /// canonical session is built. Non-host clients leave HostSession null.
    /// </summary>
    public void SetHostSession(GameSession session)
    {
        HostSession = session;
    }

    // ---- Lifecycle handlers driven by ClientGameView ----

    private void HandleGameStarted()
    {
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
        _textUIManager.UpdateCurrentPlayerText();

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
        if (LocalPlayer == null || evt.PlayerId != LocalPlayer.Id)
            return;

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
        var effects = candidateIds
            .Select(id => _currentRuleInGame.Effects.FirstOrDefault(e => e.Id == id))
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

    public void ShowGameRules(bool openedByPlayer)
    {
        if (CurrentPlayer == LocalPlayer && _canChooseRule)
        {
            StartCoroutine(PlayerInitiateRule());
        }
        else
        {
            RuleContainer.Instance.Show(_currentRuleInGame.Effects, openedByPlayer);
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
        RuleContainer.Instance.Show(_currentRuleInGame.Effects, false);

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

    [SerializeField] private float _returnToMenuDelay = 5f;

    private void HandleGameEnded(int winnerId)
    {
        var winner = ClientView.FindPlayer(winnerId);
        ResultContainer.Instance.ShowWinner(winner?.Name ?? "?");
        // After the result has been visible for a moment, tear down the
        // network and reload the scene so everyone lands back on the
        // MenuCanvas. Invoke (rather than Task.Delay) lets Unity manage
        // the timing on this MonoBehaviour and survives until SceneManager
        // unloads us.
        Invoke(nameof(ReturnToMenuFromGameEnd), _returnToMenuDelay);
    }

    private void ReturnToMenuFromGameEnd()
    {
        Assets.Scripts.Network.GameConnectionManager.ReturnToMenu();
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
