using Assets.Scripts.Views;
using DG.Tweening;
using ScaryTales;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

// Избавься от дубликатов
public class BoardUI : MonoBehaviour
{
    private CardViewService _cardViewService;

    public Transform GameBoardPanel;
    public Transform TimeOfDaySlot;
    public Transform DiscardPile;

    private void Start()
    {
        StartCoroutine(WaitForContextAndInit());
    }

    private IEnumerator WaitForContextAndInit()
    {
        // Ждём, пока инициализируется контекст
        while (UnGameManager.Instance.GameManager == null)
        {
            yield return null;
        }
        var context = UnGameManager.Instance.GameManager._context;
        context.GameManager.OnCardMovedToBoard += HandleCardMovedToBoard;
        context.GameManager.OnCardMovedToTimeOfDaySlot += HandleCardMovedToTimeOfDaySlot;
        context.GameManager.OnCardMovedToDiscardPile += HandleCardMovedToDiscardPile;

        _cardViewService = CardViewService.Instance;
    }
    private async void HandleCardMovedToBoard(Card card)
    {
        var unityManager = UnGameManager.Instance;
        var deck = unityManager.Deck;
        var cardView = _cardViewService.GetCardView(card);
        if (cardView == null)
        {
            cardView = _cardViewService.CreateCardView(card, deck);
        }
        else
        {
            cardView = _cardViewService.GetCardView(card);
        }
        cardView.FaceUp();
        var animationTask = AnumateCardTransformToPositionInLayout(cardView, GameBoardPanel);
        AnimationManager.Instance.Register(animationTask);
        await animationTask;
        Debug.Log($"Карта {card.Name} перемещена на стол");
    }

    private async void HandleCardMovedToTimeOfDaySlot(Card card)
    {
        Debug.Log($"Карта {card.Name} перемещена в слот времени суток");

        // Удаляем старую карту из слота (если есть)
        foreach (Transform child in TimeOfDaySlot)
        {
            child.SetParent(DiscardPile);
        }
        // Получаем CardView для текущей карты
        CardView cardView = _cardViewService.GetCardView(card);
        var unityManager = UnGameManager.Instance;
        var deck = unityManager.Deck;
        if (cardView == null)
        {
            cardView = _cardViewService.CreateCardView(card, deck);
        }
        else
        {
            cardView = _cardViewService.GetCardView(card);
        }
        cardView.FaceUp();
        // Перемещаем CardView в слот времени суток
        await AnimateCardTransformToPosition(cardView, TimeOfDaySlot);
        cardView.transform.SetParent(TimeOfDaySlot);
        card.Owner = null;
    }
    private async void HandleCardMovedToDiscardPile(Card card)
    {
        Debug.Log($"Карта {card.Name} перемещена в сброс");

        CardView cardView = _cardViewService.GetCardView(card);
        if (cardView != null)
        {
            //cardView.transform.SetParent(DiscardPile); // Перемещаем CardView в сброс
            //cardView.transform.SetAsLastSibling(); // Убедимся, что карта отображается поверх остальных
            var animationTask = AnimateCardTransformToPosition(cardView, DiscardPile);
            AnimationManager.Instance.Register(animationTask);
            await animationTask;
            DiscardPileView.Instance.SetSuit();
            Destroy(cardView.gameObject);
        }
        else
        {
            Debug.LogError($"CardView для карты {card.Name} не найден!");
        }
    }
    public async Task AnimateCardTransformToPosition(CardView card, Transform to)
    {

        // Анимация перемещения карты в позицию руки
        await card.transform.DOMove(to.position, 1f) // Длительность анимации: 1 секунда
            .SetEase(Ease.OutQuad) // Плавное замедление
            .AsyncWaitForCompletion(); // Ожидаем завершения анимации
    }

    public async Task AnumateCardTransformToPositionInLayout(CardView card, Transform to)
    {
        // Анимация перемещения карты в позицию руки
        await card.transform.DOMove(to.position, 1f) // Длительность анимации: 1 секунда
            .SetEase(Ease.OutQuad) // Плавное замедление
            .AsyncWaitForCompletion(); // Ожидаем завершения анимации
        // Делаем карту дочерним объектом руки
        card.transform.SetParent(to);

        // Принудительное обновление расположения GridLayout
        LayoutRebuilder.ForceRebuildLayoutImmediate(to.GetComponent<RectTransform>());

        // Ждём, пока GridLayoutGroup обновит позиции
        await Task.Yield();
    }
}

