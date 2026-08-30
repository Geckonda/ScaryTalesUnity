using Assets.Scripts.Network;
using Assets.Scripts.UIEntities;
using DG.Tweening;
using ScaryTales;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

public class PlayerHandUI : MonoBehaviour
{
    private ClientGameView _view;
    private SeatLayout _seatLayout;

    private CardViewService _cardViewService;

    public Dictionary<Player, Transform> _playerHandPanels;

    [Tooltip("Сколько летит карта в руку, секунд. Прилёт в руку не блокирует очередь событий, поэтому при раздаче карты летят внахлёст и значение НЕ умножается на их число.")]
    [SerializeField] private float _dealDuration = 1f;

    [Tooltip("Пауза между вылетами соседних карт при раздаче, секунд. Ноль — все вылетают разом; 0.15 даёт каскад, при котором карты идут одна за другой, оставаясь в полёте одновременно.")]
    [SerializeField] private float _dealStagger = 0.15f;

    void Awake()
    {
        _cardViewService = CardViewService.Instance;
    }

    /// <summary>
    /// Wires this hand UI to the client mirror and the seat layout.
    /// Called by UnGameManager when GameStartedEvent arrives.
    /// </summary>
    public void Initialize(ClientGameView view, SeatLayout seatLayout)
    {
        _view = view;
        _seatLayout = seatLayout;

        if (_view.LocalPlayer == null)
        {
            Debug.LogError("[PlayerHandUI] LocalPlayer not ready.");
            return;
        }

        _view.OnCardAddedToHand += HandleCardAddedToHand;
        _view.OnCardAddedToHandFromDiscardPile += HandleCardAddedToHandFromDiscardPile;

        _playerHandPanels = new Dictionary<Player, Transform>();
        var localSeat = _seatLayout?.LocalSeat;
        if (localSeat?.HandPanel != null)
            _playerHandPanels[_view.LocalPlayer] = localSeat.HandPanel;

        for (int i = 0; i < _view.Opponents.Count; i++)
        {
            var seat = _seatLayout?.GetOpponentSeat(i);
            if (seat?.HandPanel != null)
                _playerHandPanels[_view.Opponents[i]] = seat.HandPanel;
        }
    }

    private async void HandleCardAddedToHand(Card card, Player player)
    {
        var unityManager = UnGameManager.Instance;
        if (unityManager == null) return;

        if (!_playerHandPanels.TryGetValue(player, out var hand)) return;

        var deck = unityManager.Deck;
        var cardView = _cardViewService.GetCardView(card) ?? _cardViewService.CreateCardView(card, deck);
        if (card.Owner != null && card.Owner == _view.LocalPlayer)
            cardView.FaceUp();
        else
            cardView.FaceDown();

        var animationTask = AnimateCardToHand(cardView, hand);
        // blocksEventQueue: false — карты в руку ничему не предшествуют и
        // ничему не мешают. Если бы очередь ждала каждую, раздача из пяти
        // карт превратилась бы в пять последовательных полётов вместо одного
        // общего вылета, каким она была раньше.
        AnimationManager.Instance.Register(animationTask, blocksEventQueue: false, staggerSeconds: _dealStagger);
        await animationTask;
    }

    private async void HandleCardAddedToHandFromDiscardPile(Card card, Player player)
    {
        var unityManager = UnGameManager.Instance;
        if (unityManager == null) return;

        if (!_playerHandPanels.TryGetValue(player, out var hand)) return;

        var discardPile = unityManager._boardUI.DiscardPile;
        var cardView = _cardViewService.CreateCardView(card, discardPile);
        if (card.Owner != null && card.Owner == _view.LocalPlayer)
            cardView.FaceUp();
        else
            cardView.FaceDown();

        var animationTask = AnimateCardToHand(cardView, hand);
        AnimationManager.Instance.Register(animationTask, blocksEventQueue: false);
        await animationTask;
    }

    private async Task AnimateCardToHand(CardView cardView, Transform hand)
    {
        await cardView.transform.DOMove(hand.position, _dealDuration)
            .SetEase(Ease.OutQuad)
            .AsyncWaitForCompletion();

        // worldPositionStays: false — the hand panel is rotated to face the
        // table centre, so keeping the world pose would leave the card with
        // a compensating local transform that fights the layout group.
        cardView.transform.SetParent(hand, false);

        LayoutRebuilder.ForceRebuildLayoutImmediate(hand.GetComponent<RectTransform>());

        await Task.Yield();
    }

    public void ClearAllCardListeners(Player player)
    {
        if (!_playerHandPanels.TryGetValue(player, out var panel)) return;

        foreach (Transform cardTransform in panel)
        {
            var drag = cardTransform.GetComponent<DragAndDrop>();
            if (drag != null) drag.ClearAllListeners();
        }
    }
}
