using Assets.Scripts;
using ScaryTales;
using ScaryTales.Abstractions;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using DG.Tweening;
using System.Threading.Tasks;
using UnityEngine.XR;
using UnityEngine.UI;
using System.Linq;

public class PlayerHandUI : MonoBehaviour
{
    private GameSession _session;
    private IGameContext _context;

    private CardViewService _cardViewService;

    public Transform PlayerHandPanel1; // Панель для карт первого игрока
    public Transform PlayerHandPanel2; // Панель для карт второго игрока
    public Dictionary<Player, Transform> _playerHandPanels;
    void Awake()
    {
        _cardViewService = CardViewService.Instance;
    }
    /// <summary>
    /// Wires this hand UI to a session. Called by UnGameManager.StartNewSession.
    /// </summary>
    public void Initialize(GameSession session)
    {
        _session = session;
        _context = session.Context;

        if (_context == null || _session.LocalPlayer == null)
        {
            UnityEngine.Debug.LogError("[PlayerHandUI] Игра или LocalPlayer еще не готовы!");
            return;
        }

        _context.GameManager.OnCardAddedToHand += HandleCardAddedToHand;
        _context.GameManager.OnCardAddedToHandFromDiscardPile += HandleCardAddedToHandFromDiscardPile;

        var localPlayer = _session.LocalPlayer;
        var opponent = _context.Players.First(p => p != localPlayer);

        _playerHandPanels = new Dictionary<Player, Transform>
        {
            { localPlayer, PlayerHandPanel1 },
            { opponent, PlayerHandPanel2 }
        };
    }

    private async void HandleCardAddedToHand(Card card, Player player)
    {
        var unityManager = UnGameManager.Instance;
        if (unityManager == null)
            return;

        if (!_playerHandPanels.TryGetValue(player, out var hand))
            return;

        var deck = unityManager.Deck;
        var cardView = _cardViewService.GetCardView(card) ?? _cardViewService.CreateCardView(card, deck);
        if (card.Owner != null && card.Owner == _session.LocalPlayer)
            cardView.FaceUp();
        else
            cardView.FaceDown();

        // Здесь мы создаём настоящий Task, и только потом регистрируем
        var animationTask = AnimateCardToHand(cardView, hand);
        AnimationManager.Instance.Register(animationTask);
        await animationTask;
    }
    private async void HandleCardAddedToHandFromDiscardPile(Card card, Player player)
    {
        var unityManager = UnGameManager.Instance;
        if (unityManager == null)
            return;

        if (!_playerHandPanels.TryGetValue(player, out var hand))
            return;

        var discardPile = unityManager._boardUI.DiscardPile;
        var cardView = _cardViewService.CreateCardView(card, discardPile);
        if (card.Owner != null && card.Owner == _session.LocalPlayer)
            cardView.FaceUp();
        else
            cardView.FaceDown();

        // Здесь мы создаём настоящий Task, и только потом регистрируем
        var animationTask = AnimateCardToHand(cardView, hand);
        AnimationManager.Instance.Register(animationTask);
        await animationTask;
    }

    private async Task AnimateCardToHand(CardView cardView, Transform hand)
    {
        // Запускаем анимацию
        await cardView.transform.DOMove(hand.position, 1f)
            .SetEase(Ease.OutQuad)
            .AsyncWaitForCompletion();

        // Только после завершения tween'а — меняем родителя
        cardView.transform.SetParent(hand);

        // Принудительно перестраиваем layout
        LayoutRebuilder.ForceRebuildLayoutImmediate(hand.GetComponent<RectTransform>());

        // Ждём один кадр, чтобы UI точно перестроился
        await Task.Yield();
    }
    public void ClearAllCardListeners(Player player)
    {
        if (!_playerHandPanels.TryGetValue(player, out var panel))
            return;

        foreach (Transform cardTransform in panel)
        {
            var drag = cardTransform.GetComponent<DragAndDrop>();
            if (drag != null)
            {
                drag.ClearAllListeners();
            }
        }
    }

}
