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

public class PlayerHandUI : MonoBehaviour
{
    private IGameContext _context;

    private CardViewService _cardViewService;

    public Transform PlayerHandPanel1; // Панель для карт первого игрока
    public Transform PlayerHandPanel2; // Панель для карт второго игрока
    public Dictionary<Player, Transform> _playerHandPanels;
    void Awake()
    {
        _context = UnGameManager.Instance.GameManager._context;
        _context.GameManager.OnCardAddedToHand += HandleCardAddedToHand;
        _cardViewService = CardViewService.Instance;

        _playerHandPanels = new Dictionary<Player, Transform>
            {
                { _context.Players[0], PlayerHandPanel1 },
                { _context.Players[1], PlayerHandPanel2 }
            };
    }

    private async void HandleCardAddedToHand(Card card, Player player)
    {
        var unityManager = UnGameManager.Instance;
        if (unityManager == null)
            return;

        CardView cardView;
        var deck = unityManager.Deck;
        if (!_playerHandPanels.TryGetValue(player, out var hand))
            return;

        if (_cardViewService.GetCardView(card) == null)
        {
            cardView = _cardViewService.CreateCardView(card, deck);
        }
        else
        {
            cardView = _cardViewService.GetCardView(card);
        }

        cardView.SetCardViewBackground(card.Owner);
        var animationTask = AnimateCardToHand(cardView, hand);
        AnimationManager.Instance.Register(animationTask);
        await animationTask;
    }
    public async Task AnimateCardToHand(CardView card, Transform hand)
    {

        // Анимация перемещения карты в позицию руки
        await card.transform.DOMove(hand.position, 1f) // Длительность анимации: 1 секунда
            .SetEase(Ease.OutQuad) // Плавное замедление
            .AsyncWaitForCompletion(); // Ожидаем завершения анимации

        // Плавное перемещение в конечную позицию внутри GridLayoutGroup
        //await card.transform.DOLocalMove(Vector3.zero, 1f)
        //    .SetEase(Ease.OutQuad)
        //    .AsyncWaitForCompletion();


        // Делаем карту дочерним объектом руки
        card.transform.SetParent(hand);

        // Принудительное обновление расположения GridLayout
        LayoutRebuilder.ForceRebuildLayoutImmediate(hand.GetComponent<RectTransform>());

        // Ждём, пока GridLayoutGroup обновит позиции
        await Task.Yield();
    }
}
