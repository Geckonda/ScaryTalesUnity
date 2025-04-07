using Assets.Scripts.Views;
using ScaryTales;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BoardUI : MonoBehaviour
{
    private CardViewService _cardViewService;

    public Transform GameBoardPanel;
    public Transform TimeOfDaySlot;
    public Transform DiscardPile;

    private void Awake()
    {
        var context = UnGameManager.Instance.GameManager._context;
        context.GameManager.OnCardMovedToBoard += HandleCardMovedToBoard;
        context.GameManager.OnCardMovedToTimeOfDaySlot += HandleCardMovedToTimeOfDaySlot;
        context.GameManager.OnCardMovedToDiscardPile += HandleCardMovedToDiscardPile;

        _cardViewService = CardViewService.Instance;
    }

    private void HandleCardMovedToBoard(Card card)
    {
        var cardView = _cardViewService.GetCardView(card);
        cardView.transform.SetParent(GameBoardPanel);
        Debug.Log($"Карта {card.Name} перемещена на стол");
    }

    private void HandleCardMovedToTimeOfDaySlot(Card card)
    {
        Debug.Log($"Карта {card.Name} перемещена в слот времени суток");

        // Удаляем старую карту из слота (если есть)
        foreach (Transform child in TimeOfDaySlot)
        {
            child.SetParent(DiscardPile);
        }
        card.Owner = null;
        // Получаем CardView для текущей карты
        CardView cardView = _cardViewService.GetCardView(card);
        if (cardView != null)
        {
            // Перемещаем CardView в слот времени суток
            cardView.transform.SetParent(TimeOfDaySlot);
        }
        else
        {
            Debug.LogError($"CardView для карты {card.Name} не найден!");
        }
    }
    private void HandleCardMovedToDiscardPile(Card card)
    {
        Debug.Log($"Карта {card.Name} перемещена в сброс");

        CardView cardView = _cardViewService.GetCardView(card);
        if (cardView != null)
        {
            //cardView.transform.SetParent(DiscardPile); // Перемещаем CardView в сброс
            //cardView.transform.SetAsLastSibling(); // Убедимся, что карта отображается поверх остальных
            DiscardPileView.Instance.SetSuit();
            Destroy(cardView.gameObject);
        }
        else
        {
            Debug.LogError($"CardView для карты {card.Name} не найден!");
        }
    }
}

