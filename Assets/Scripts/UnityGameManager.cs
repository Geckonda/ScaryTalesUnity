using ScaryTales;
using ScaryTales.Abstractions;
using ScaryTales.Interaction_Entities.EnvUnity;
using System.Collections;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using System.Threading.Tasks;

public class UnGameManager : MonoBehaviour
{
    public static UnGameManager Instance { get; private set; }

    private IGameContext _context;
    private GameManager _gameManager;
    private CardViewService _cardViewService;

    public BoardUI _boardUI;
    public PlayerHandUI _playerHandUI;
    public TextUIManager _textUIManager;
    public GameManager GameManager => _gameManager;
    public Transform GameBoardPanel;
    public Transform Deck;
    public Player CurrentPlayer => _context.GameState.GetCurrentPlayer();

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
        var playerInput = new GameObject("PlayerInputObject").AddComponent<UnityPlayerInput>();

        // ������������� ����
        var builder = new GameBuilder(playerInput,
            new UnityNotifier(),
            new GameBoard());
        _gameManager = builder.Build();
        _context = _gameManager._context;
        _cardViewService = CardViewService.Instance;
    }

    async void Start()
    {
        //await StartGame();
    }
    private async Task StartGame()
    {
        PrepareFirstNight();

        await DrawCardsToPlayersHand();


        HandlePlayerTurn();
    }

    public async void StartGameFromNetwork()
    {
        await StartGame(); // ������ �������� StartGame, ����� ������ ������� �� �������
    }

    private void PrepareFirstNight()
    {
        Card night = _context.Deck.TakeCardByName("����")!;
        _cardViewService.CreateCardView(night, _boardUI.TimeOfDaySlot);
        _context.GameManager.PutCardInTimeOfDaySlot(night);
    }
    public async Task DrawCardsToPlayersHand()
    {
        //_gameManager.DrawCardsToPlayersHand();
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

    private async void HandlePlayerTurn()
    {
        Debug.Log("��� ��������� ���� ��������...");
        await AnimationManager.Instance.WaitForAllAnimations();
        Debug.Log("�������� ���������.");
        _textUIManager.UpdateCurrentPlayerText();
        _gameManager.DrawCard(CurrentPlayer);
        await AnimationManager.Instance.WaitForAllAnimations();
        if (CurrentPlayer.Hand.Count == 0)
            _gameManager.EndGame();
        else
        {
            StartCoroutine(ProcessPlayerActions(CurrentPlayer));
        }
    }

    private async Task EndTurn()
    {
        await _context.GameManager.ActivateAllPlayerPermanentCardEffects(CurrentPlayer);
        _context.GameState.NextTurn();
        HandlePlayerTurn();
    }

    private IEnumerator ProcessPlayerActions(Player player)
    {
        // ���������, ��� ���� ��� ��������
        if (!Application.isPlaying)
            yield break;

        bool cardSelected = false;
        Card selectedCard = null;

        if (!_playerHandUI._playerHandPanels.TryGetValue(player, out Transform playerHandPanel))
        {
            Debug.LogError($"������ ���� ��� {player.Name} �� �������!");
            yield break;
        }

        Action<Card> onCardSelected = (card) =>
        {
            cardSelected = true;
            selectedCard = card;
        };

        DragAndDrop.SelectCard = true; // ��������� �������� �����
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
        DragAndDrop.SelectCard = false; // ��������� ����� �����
        foreach (Transform cardTransform in playerHandPanel)
        {
            var dragAndDrop = cardTransform.GetComponent<DragAndDrop>();
            if (dragAndDrop != null)
            {
                dragAndDrop.OnCardSelected -= onCardSelected;
            }
        }

        // ���� ����� �������, ����������� �
        if (selectedCard != null)
        {
            // ������� Task ��� �������� ���������� PlayCard
            Task playCardTask = _gameManager.PlayCard(selectedCard);
            // ������� ���������� Task � ��������
            yield return new WaitUntil(() => playCardTask.IsCompleted);
        }

        yield return EndTurn().AsIEnumerator();

    }

}