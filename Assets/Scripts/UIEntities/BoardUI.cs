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
        Debug.Log($"����� {card.Name} ���������� �� ����");
    }

    private void HandleCardMovedToTimeOfDaySlot(Card card)
    {
        Debug.Log($"����� {card.Name} ���������� � ���� ������� �����");

        // ������� ������ ����� �� ����� (���� ����)
        foreach (Transform child in TimeOfDaySlot)
        {
            child.SetParent(DiscardPile);
        }
        card.Owner = null;
        // �������� CardView ��� ������� �����
        CardView cardView = _cardViewService.GetCardView(card);
        if (cardView != null)
        {
            // ���������� CardView � ���� ������� �����
            cardView.transform.SetParent(TimeOfDaySlot);
        }
        else
        {
            Debug.LogError($"CardView ��� ����� {card.Name} �� ������!");
        }
    }
    private void HandleCardMovedToDiscardPile(Card card)
    {
        Debug.Log($"����� {card.Name} ���������� � �����");

        CardView cardView = _cardViewService.GetCardView(card);
        if (cardView != null)
        {
            //cardView.transform.SetParent(DiscardPile); // ���������� CardView � �����
            //cardView.transform.SetAsLastSibling(); // ��������, ��� ����� ������������ ������ ���������
            DiscardPileView.Instance.SetSuit();
            Destroy(cardView.gameObject);
        }
        else
        {
            Debug.LogError($"CardView ��� ����� {card.Name} �� ������!");
        }
    }
}

