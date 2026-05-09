using Assets.Scripts.Network;
using Assets.Scripts.UIEntities;
using Assets.Scripts.Views;
using DG.Tweening;
using ScaryTales;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

public class BoardUI : MonoBehaviour
{
    private CardViewService _cardViewService;
    private ClientGameView _view;
    private SeatLayout _seatLayout;
    // Each player's BeforePlayer destination, looked up at runtime per
    // CardMovedToBeforePlayer event.
    private Dictionary<Player, RectTransform> _beforePlayerTables = new();

    public Transform GameBoardPanel;
    public Transform TimeOfDaySlot;
    public Transform DiscardPile;
    public GameObject UIBlockerOverlay;

    private int _animationDelay = 2000;
    private void Start()
    {
        if (UIBlockerOverlay != null) UIBlockerOverlay.SetActive(false);
    }

    /// <summary>
    /// Wires this BoardUI to the client mirror and the seat layout.
    /// Each seat owns its own BeforePlayerTable (replacing the old
    /// LocalPlayerTable / OpponentTable pair).
    /// </summary>
    public void Initialize(ClientGameView view, SeatLayout seatLayout)
    {
        _view = view;
        _seatLayout = seatLayout;

        view.OnCardMovedToBoard += HandleCardMovedToBoard;
        view.OnCardMovedToBeforePlayer += HandleCardMovedToBeforePlayer;
        view.OnCardMovedToTimeOfDaySlot += HandleCardMovedToTimeOfDaySlot;
        view.OnCardMovedToDiscardPile += HandleCardMovedToDiscardPile;

        _cardViewService = CardViewService.Instance;

        _beforePlayerTables.Clear();
        var localSeat = _seatLayout?.LocalSeat;
        if (localSeat?.BeforePlayerTable != null)
            _beforePlayerTables[_view.LocalPlayer] = localSeat.BeforePlayerTable;
        for (int i = 0; i < _view.Opponents.Count; i++)
        {
            var seat = _seatLayout?.GetOpponentSeat(i);
            if (seat?.BeforePlayerTable != null)
                _beforePlayerTables[_view.Opponents[i]] = seat.BeforePlayerTable;
        }
    }

    private async void HandleCardMovedToBoard(Card card)
    {
        var unityManager = UnGameManager.Instance;
        var deck = unityManager.Deck;
        var cardView = _cardViewService.GetCardView(card)
            ?? _cardViewService.CreateCardView(card, deck);
        cardView.FaceUp();
        var animationTask = AnimateCardTransformToPositionInLayout(cardView, GameBoardPanel);
        AnimationManager.Instance.Register(animationTask);
        await animationTask;
    }

    private async void HandleCardMovedToBeforePlayer(Card card)
    {
        var unityManager = UnGameManager.Instance;
        var deck = unityManager.Deck;
        var cardView = _cardViewService.GetCardView(card)
            ?? _cardViewService.CreateCardView(card, deck);
        cardView.FaceUp();

        Transform panel = null;
        if (card.Owner != null && _beforePlayerTables.TryGetValue(card.Owner, out var tbl))
            panel = tbl;
        // Fallback: send to general board if we couldn't resolve a seat.
        if (panel == null) panel = GameBoardPanel;

        var animationTask = AnimateCardTransformToPositionInLayout(cardView, panel);
        AnimationManager.Instance.Register(animationTask);
        await animationTask;
    }

    private async void HandleCardMovedToTimeOfDaySlot(Card card)
    {
        var unityManager = UnGameManager.Instance;
        var deck = unityManager.Deck;
        CardView cardView = _cardViewService.GetCardView(card)
            ?? _cardViewService.CreateCardView(card, deck);
        cardView.FaceUp();
        await AnimateCardTransformToPosition(cardView, TimeOfDaySlot);
        cardView.transform.SetParent(TimeOfDaySlot);
        card.Owner = null;
        cardView.transform.localScale = Vector3.one;
    }

    private async void HandleCardMovedToDiscardPile(Card card)
    {
        CardView cardView = _cardViewService.GetCardView(card);
        if (cardView != null)
        {
            var animationTask = AnimateCardTransformToPosition(cardView, DiscardPile);
            AnimationManager.Instance.Register(animationTask);
            await animationTask;
            DiscardPileView.Instance.SetSuit();
            Destroy(cardView.gameObject);
        }
        else
        {
            Debug.LogError($"CardView for {card.Name} not found.");
        }
    }

    public async Task AnimateCardTransformToPosition(CardView card, Transform to)
    {
        await Task.Delay(_animationDelay);
        await card.transform.DOMove(to.position, 1f)
            .SetEase(Ease.OutQuad)
            .AsyncWaitForCompletion();
    }

    public async Task AnimateCardTransformToPositionInLayout(CardView card, Transform to)
    {
        await card.transform.DOMove(to.position, 1f)
            .SetEase(Ease.OutQuad)
            .AsyncWaitForCompletion();
        card.transform.SetParent(to);
        LayoutRebuilder.ForceRebuildLayoutImmediate(to.GetComponent<RectTransform>());
        await Task.Yield();
    }
}
