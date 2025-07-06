using Assets.Libreries.ScaryTales;
using Assets.Libreries.ScaryTales.Abstractions;
using Assets.Libreries.ScaryTales.Rules.Templates.A;
using Assets.Scripts.Menus;
using Assets.Scripts.Network;
using Assets.Scripts.Utilities;
using ScaryTales;
using ScaryTales.Abstractions;
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

    private Rule _currentRule;
    public Rule CurrentRule => _currentRule;

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
        _currentRule = new A1();
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
        await StartGame(); // просто вызывает StartGame, когда пришла команда от сервера
    }

    private void PrepareFirstNight()
    {
        Card night = _context.Deck.TakeCardByName("Ночь")!;
        _cardViewService.CreateCardView(night, _boardUI.TimeOfDaySlot);
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

        await AnimationManager.Instance.WaitForAllAnimations();
        _textUIManager.UpdateCurrentPlayerText();
        _gameManager.DrawCard(CurrentPlayer);
        await AnimationManager.Instance.WaitForAllAnimations();

        if (CurrentPlayer.ItemsBagCount > 0)
        {
            await CoroutineUtils.WaitForCoroutine(this, PlayerUseRules(CurrentPlayer));
        }

        EnablePlayerDrag(CurrentPlayer);
        
        if (CurrentPlayer.Hand.Count == 0)
            _gameManager.EndGame();
        else
        {
            await CoroutineUtils.WaitForCoroutine(this, ProcessPlayerActions(CurrentPlayer));
        }
    }
    public void ShowGameRules(bool openedByPlayer)
    {
        // Получаем контейнер для отображения предметов
        var ruleContainer = RuleContainer.Instance.contentPanel;

        RuleContainer.Instance.Show(_currentRule.Effects, openedByPlayer);
    }
    public async Task ApplyTheRule()
    {
        
    }
    public async void PlayCard(Card card)
    {
        //Создаем Task для ожидания завершения PlayCard
        await _gameManager.PlayCard(card);
        //Ожидаем завершения Task в корутине
        await EndTurn();

    }
    private async Task EndTurn()
    {
        await _context.GameManager.ActivateAllPlayerPermanentCardEffects(CurrentPlayer);
        Debug.Log($"ENDTURHN DragAndDrop is {DragAndDrop.SelectCard}");
        _context.GameState.NextTurn();
        HandlePlayerTurn();
    }

    private async Task EndChoosingItems()
    {

    }
    private IEnumerator PlayerUseRules(Player player)
    {
        if (!Application.isPlaying)
            yield break;

        // Ждём, пока игрок выберет или пропустит правило
        var selectTask = player.SelectRuleEffect(CurrentRule.Effects); // <- это async Task<IRuleEffect>
        yield return selectTask.AsIEnumerator(); // ждём завершения task внутри корутины

        IRuleEffect chosen = selectTask.Result;

        if (chosen == null)
        {
            Debug.Log("Игрок пропустил выбор правила.");
            yield break;
        }
        else
        {
            Debug.Log($"Игрок выбрал правило {chosen.Id}.");
            GameNetworkController.Instance.CmdSelectRuleEffect(chosen.Id);
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