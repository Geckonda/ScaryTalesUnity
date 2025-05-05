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

// �������� �� ����������
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
        // ���, ���� ���������������� ��������
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
        Debug.Log($"����� {card.Name} ���������� �� ����");
    }

    private async void HandleCardMovedToTimeOfDaySlot(Card card)
    {
        Debug.Log($"����� {card.Name} ���������� � ���� ������� �����");

        // ������� ������ ����� �� ����� (���� ����)
        foreach (Transform child in TimeOfDaySlot)
        {
            child.SetParent(DiscardPile);
        }
        // �������� CardView ��� ������� �����
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
        // ���������� CardView � ���� ������� �����
        await AnimateCardTransformToPosition(cardView, TimeOfDaySlot);
        cardView.transform.SetParent(TimeOfDaySlot);
        card.Owner = null;
    }
    private async void HandleCardMovedToDiscardPile(Card card)
    {
        Debug.Log($"����� {card.Name} ���������� � �����");

        CardView cardView = _cardViewService.GetCardView(card);
        if (cardView != null)
        {
            //cardView.transform.SetParent(DiscardPile); // ���������� CardView � �����
            //cardView.transform.SetAsLastSibling(); // ��������, ��� ����� ������������ ������ ���������
            var animationTask = AnimateCardTransformToPosition(cardView, DiscardPile);
            AnimationManager.Instance.Register(animationTask);
            await animationTask;
            DiscardPileView.Instance.SetSuit();
            Destroy(cardView.gameObject);
        }
        else
        {
            Debug.LogError($"CardView ��� ����� {card.Name} �� ������!");
        }
    }
    public async Task AnimateCardTransformToPosition(CardView card, Transform to)
    {

        // �������� ����������� ����� � ������� ����
        await card.transform.DOMove(to.position, 1f) // ������������ ��������: 1 �������
            .SetEase(Ease.OutQuad) // ������� ����������
            .AsyncWaitForCompletion(); // ������� ���������� ��������
    }

    public async Task AnumateCardTransformToPositionInLayout(CardView card, Transform to)
    {
        // �������� ����������� ����� � ������� ����
        await card.transform.DOMove(to.position, 1f) // ������������ ��������: 1 �������
            .SetEase(Ease.OutQuad) // ������� ����������
            .AsyncWaitForCompletion(); // ������� ���������� ��������
        // ������ ����� �������� �������� ����
        card.transform.SetParent(to);

        // �������������� ���������� ������������ GridLayout
        LayoutRebuilder.ForceRebuildLayoutImmediate(to.GetComponent<RectTransform>());

        // ���, ���� GridLayoutGroup ������� �������
        await Task.Yield();
    }
}

