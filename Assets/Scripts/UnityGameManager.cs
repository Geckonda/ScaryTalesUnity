using Assets.Libreries.ScaryTales;
using Assets.Libreries.ScaryTales.Abstractions;
using Assets.Libreries.ScaryTales.Rules.Templates.A;
using Assets.Libreries.ScaryTales.Rules.Templates.B;
using Assets.Scripts;
using Assets.Scripts.Menus;
using Assets.Scripts.Network;
using Assets.Scripts.Services;
using Assets.Scripts.Utilities;
using Assets.Scripts.Views;
using ScaryTales;
using ScaryTales.Abstractions;
using ScaryTales.Decisions;
using ScaryTales.Interaction_Entities.EnvUnity;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;

public class UnGameManager : MonoBehaviour
{
    public static UnGameManager Instance { get; private set; }

    public GameSession Session { get; private set; }

    public CardViewService _cardViewService;
    public BoardUI _boardUI;
    public PlayerHandUI _playerHandUI;
    public TextUIManager _textUIManager;
    public Transform GameBoardPanel;
    public Transform Deck;

    // Forwarders to session state. Kept named the way legacy callers expect
    // so they keep compiling; Phase 2.2 will migrate callers to use Session
    // directly and these forwarders can go away.
    public IGameContext _context => Session?.Context;
    public GameManager _gameManager => Session?.GameManager;
    public GameManager GameManager => Session?.GameManager;
    public Player CurrentPlayer => Session?.CurrentPlayer;
    public Rule CurrentRuleInGame => Session?.CurrentRuleInGame;
    public Rule CurrentFinalRule => Session?.CurrentFinalRule;
    public Player LocalPlayer => Session?.LocalPlayer;
    public Player LocalOpponent => Session?.LocalOpponent;

    private bool canChooseRule
    {
        get => Session?.CanChooseRule ?? false;
        set { if (Session != null) Session.CanChooseRule = value; }
    }

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
    }

    /// <summary>
    /// Composition-root entry point: builds a GameSession, wires every UI
    /// component to it, and kicks off the game. Called by
    /// GameNetworkController.TargetSetPlayer once the network has handed us
    /// the GameManager + players.
    /// </summary>
    public async void StartNewSession(GameManager gameManager, Player localPlayer, Player localOpponent)
    {
        // Hardcoded rules for now; Phase 4 lobby will let the host pick.
        Session = new GameSession(gameManager, new A1(), new B2(), localPlayer, localOpponent);

        _boardUI.Initialize(Session);
        _playerHandUI.Initialize(Session);
        _textUIManager.Initialize(Session);

        await StartGame();
    }

    private async Task StartGame()
    {
        PrepareFirstNight();

        await DrawCardsToPlayersHand();


        HandlePlayerTurn();
    }

    private void PrepareFirstNight()
    {
        Card night = _context.Deck.TakeCardByName("Ночь")!;
        var card = _cardViewService.CreateCardView(night, _boardUI.TimeOfDaySlot);
        card.FaceUp();
        _context.GameManager.PutCardInTimeOfDaySlot(night);
    }
    public async Task DrawCardsToPlayersHand()
    {
        var players = _context.Players;
        var deck = _context.Deck;
        foreach (var player in players)
        {
            for (int i = 0; i < 5; i++)
            {
                await Task.Delay(200);
                _gameManager.DrawCard(player);
            }
        }
    }

    public void ShowLocalPlayerItemBag()
    {
        // Получаем контейнер для отображения предметов
        var itemContainer = ItemContainer.Instance.contentPanel;

        var items = LocalPlayer.ShowItemsFromItemBag();
        ItemContainer.Instance.Show(items);
    }

    private async void HandlePlayerTurn()
    {
        DragAndDrop.SelectCard = false;
        canChooseRule = true; // Разрешаем выбор правила

        await AnimationManager.Instance.WaitForAllAnimations();
        _textUIManager.UpdateCurrentPlayerText();
        _gameManager.DrawCard(CurrentPlayer);
        await AnimationManager.Instance.WaitForAllAnimations();

        EnablePlayerDrag(CurrentPlayer);

        if (CurrentPlayer.Hand.Count == 0)
        {
            _gameManager.EndGame();
            EndGame();
        }
        else
        {
            await CoroutineUtils.WaitForCoroutine(this, ProcessPlayerActions(CurrentPlayer));
        }
    }
    public async void EndGame()
    {
        _gameManager.PrintMessage("Конец игры");
        await FinalRule();
        string winner =  LocalPlayer.Score > LocalOpponent.Score
            ? LocalPlayer.Name : LocalOpponent.Name;
        ResultContainer.Instance.ShowWinner(winner);
    }


    // Отрефактоирть этот етод, перенести логику в отдельный класс
    public async Task FinalRule()
    {
        // Получаем контейнер
        var ruleEffectContainer = RuleContainer.Instance.contentPanel;
        List<RuleEffectView> _viewsToSelect = new();

        // Создаем и настраиваем представления эффектов
        foreach (var effect in CurrentFinalRule.Effects)
        {
            var effectView = RuleEffectService.Instance.CreateRuleEffectView(effect, ruleEffectContainer);
            if (effectView != null)
            {
                _viewsToSelect.Add(effectView);
            }
        }
        // Показываем UI
        RuleContainer.Instance.Show(_viewsToSelect, false);
        CurrentFinalRule.Effects.ForEach(x => x.ApplyEffect(_context));
        await Task.Delay(10000);
        RuleContainer.Instance.Hide();
    }
    public async void ShowGameRules(bool openedByPlayer)
    {
        // Получаем контейнер для отображения предметов
        if(CurrentPlayer == LocalPlayer && canChooseRule)
        {
            await CoroutineUtils.WaitForCoroutine(this, PlayerUseRules(CurrentPlayer));

        }
        else
        {
            var ruleContainer = RuleContainer.Instance.contentPanel;

            RuleContainer.Instance.Show(CurrentRuleInGame.Effects, openedByPlayer);
        }
    }
    public async void PlayCard(Card card)
    {
        //Создаем Task для ожидания завершения PlayCard
        await _gameManager.PlayCard(card);

        // Задержка - можно в будущем прикрутить анмиацию
        await Task.Delay(1000);
        //Ожидаем завершения Task в корутине
        await EndTurn();

    }
    public async void ActivateRuleEffect(IRuleEffect effect)
    {
        await _gameManager.ActivateRuleEffect(effect);
    }
    private async Task EndTurn()
    {
        await _context.GameManager.ActivateAllPlayerPermanentCardEffects(CurrentPlayer);
        Debug.Log($"ENDTURHN DragAndDrop is {DragAndDrop.SelectCard}");
        _context.GameState.NextTurn();
        HandlePlayerTurn();
    }
    private IEnumerator PlayerUseRules(Player player)
    {
        if (!Application.isPlaying)
            yield break;

        // Ждём, пока игрок выберет или пропустит правило
        var pickTask = _context.Router.PickRuleEffect(
            player.Id,
            new PickRuleEffectRequest(CurrentRuleInGame.Effects.Select(e => e.Id)));
        yield return pickTask.AsIEnumerator();

        var pick = pickTask.Result;

        if (pick.RuleEffectId == null)
        {
            Debug.Log("Игрок пропустил выбор правила.");
            yield break;
        }
        else
        {
            Debug.Log($"Игрок выбрал правило {pick.RuleEffectId}.");
            canChooseRule = false;
            GameNetworkController.Instance.CmdOnRuleChosen(pick.RuleEffectId.Value);
            yield break;
        }
    }

    private IEnumerator ProcessPlayerActions(Player player)
    {
        // Проверяем, что игра ещё запущена
        if (!Application.isPlaying)
            yield break;

        // Только локальный игрок должен выполнять выбор карты
        if (_gameManager.LocalPlayer != player)
        {
            yield break;
        }
        bool cardSelected = false;
        Card selectedCard = null;

        Debug.Log($"Player and Panel: {_playerHandUI._playerHandPanels.ContainsKey(player)}");

        if (!_playerHandUI._playerHandPanels.TryGetValue(player, out Transform playerHandPanel))
        {
            Debug.LogError($"Панель руки для {player.Name} не найдена!");
            yield break;
        }

        Action<Card> onCardSelected = (card) =>
        {
            cardSelected = true;
            selectedCard = card;
        };
        CardSelectionService.CurrentSelectionHandler = onCardSelected;


        DragAndDrop.SelectCard = true; // Разрешаем выбирать карту
        foreach (Transform cardTransform in playerHandPanel)
        {
            var dragAndDrop = cardTransform.GetComponent<DragAndDrop>();
            if (dragAndDrop != null)
            {
                dragAndDrop.OnCardSelected += onCardSelected;
            }
        }

        while (!cardSelected)
        {
            yield return null;
        }
        DragAndDrop.SelectCard = false; // Запрещаем выбор карты
        canChooseRule = false; // Запрещаем выбор правила
        CardSelectionService.CurrentSelectionHandler = null;

        foreach (Transform cardTransform in playerHandPanel)
        {
            var dragAndDrop = cardTransform.GetComponent<DragAndDrop>();
            if (dragAndDrop != null)
            {
                dragAndDrop.OnCardSelected -= onCardSelected;
            }
        }

        // Если карта выбрана, разыгрываем её
        if (selectedCard != null)
        {
            GameNetworkController.Instance.CmdPlayCard(selectedCard.Id);

            yield break;
        }
        DisablePlayerDrag(CurrentPlayer);
        yield break;

    }
    public void EnablePlayerDrag(Player player)
    {
        if (!_playerHandUI._playerHandPanels.TryGetValue(player, out var handPanel))
            return;

        foreach (Transform child in handPanel)
        {
            var cardView = child.GetComponent<CardView>();
            if (cardView != null)
            {
                _cardViewService.CardViewFactory.EnableDrag(cardView);
            }
        }
    }

    public void DisablePlayerDrag(Player player)
    {
        if (!_playerHandUI._playerHandPanels.TryGetValue(player, out var handPanel))
            return;

        foreach (Transform child in handPanel)
        {
            var cardView = child.GetComponent<CardView>();
            if (cardView != null)
            {
                _cardViewService.CardViewFactory.DisableDrag(cardView);
            }
        }
    }

}
