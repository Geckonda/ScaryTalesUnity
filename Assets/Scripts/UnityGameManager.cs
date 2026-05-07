using Assets.Libreries.ScaryTales;
using Assets.Libreries.ScaryTales.Abstractions;
using Assets.Libreries.ScaryTales.Rules.Templates.A;
using Assets.Libreries.ScaryTales.Rules.Templates.B;
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

    public IGameContext _context;
    public GameManager _gameManager;
    public CardViewService _cardViewService;

    public BoardUI _boardUI;
    public PlayerHandUI _playerHandUI;
    public TextUIManager _textUIManager;
    public GameManager GameManager => _gameManager;
    public Transform GameBoardPanel;
    public Transform Deck;
    public Player CurrentPlayer => _context.GameState.GetCurrentPlayer();

    private Rule _currentRuleInGame;
    private Rule _currentFinalRule;
    private bool canChooseRule = false;
    public Rule CurrentRuleInGame => _currentRuleInGame;
    public Rule CurrentFinalRule=> _currentFinalRule;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
        _cardViewService = CardViewService.Instance;
        // Жесткая установка правила
        _currentRuleInGame = new A1();
        _currentFinalRule = new B2();
    }
    public Player LocalPlayer { get; private set; }
    public Player LocalOpponent { get; private set; }
    public void SetLocalPlayer(Player player) => LocalPlayer = player;
    public void SetLocalOpponent(Player player) => LocalOpponent = player;

    private async Task StartGame()
    {
        PrepareFirstNight();

        await DrawCardsToPlayersHand();


        HandlePlayerTurn();
    }

    public async void StartGameFromNetwork()
    {
        // Инициализируем UI после запуска игры
        var playerHandUI = FindObjectOfType<PlayerHandUI>();
        playerHandUI.Initialize();

        // Wire the rule-effect lookup into the router. The current rule lives
        // here, not in core, so we configure the adapter post-init.
        if (_context.Router is PlayerInputAdapterRouter adapter)
        {
            adapter.SetRuleEffectLookup(id =>
                _currentRuleInGame.Effects.FirstOrDefault(e => e.Id == id));
        }

        await StartGame(); // просто вызывает StartGame, когда пришла команда от сервера
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

            RuleContainer.Instance.Show(_currentRuleInGame.Effects, openedByPlayer);
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