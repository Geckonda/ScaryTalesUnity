using ScaryTales;
using UnityEngine;
using UnityEngine.EventSystems;
using System;
using ScaryTales.Abstractions;

public class DragAndDrop : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    private Vector3 startPosition;
    private Transform parentToReturnTo;
    private Transform gameBoard;
    private IGameManager gameManager;
    private Card card;

    /// <summary>
    /// �������� �� ����������� ������ �����
    /// </summary>
    public static bool SelectCard {  get; set; }
    public Card Card => card;

    public event Action<Card> OnCardSelected;

    public void Initialize(IGameManager manager, Card cardData, Transform board)
    {
        gameManager = manager;
        card = cardData;
        gameBoard = board;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        // ���������, ��� ����� ����������� �������� ������ � ����� ����� ��������
        if (card.Owner != UnGameManager.Instance.CurrentPlayer || !SelectCard)
        {
            return; // ����������, ���� ��� �� ��� �������� ������
        }

        startPosition = transform.position;
        parentToReturnTo = transform.parent;
        transform.SetParent(transform.root);
    }

    public void OnDrag(PointerEventData eventData)
    {
        // ���������, ��� ����� ����������� �������� ������ � ����� ����� ��������
        if (card.Owner != UnGameManager.Instance.CurrentPlayer || !SelectCard)
        {
            return; // ����������, ���� ��� �� ��� �������� ������
        }

        transform.position = eventData.position;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        // ���������, ��� ����� ����������� �������� ������ � ����� ����� ��������
        if (card.Owner != UnGameManager.Instance.CurrentPlayer || !SelectCard)
        {
            return; // ����������, ���� ��� �� ��� �������� ������
        }

        // ���� ����� ���������� �� ����, ����������� �
        if (RectTransformUtility.RectangleContainsScreenPoint(
            gameBoard.GetComponent<RectTransform>(), eventData.position))
        {
            OnCardSelected?.Invoke(card);
            transform.SetParent(gameBoard);
        }
        else
        {
            transform.SetParent(parentToReturnTo);
            transform.position = startPosition;
        }
    }
}