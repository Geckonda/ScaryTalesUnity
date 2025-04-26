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
    /// ƒоступна ли возможность выбора карты
    /// </summary>
    public static bool SelectCard {  get; set; }
    public Card Card => card;

    public event Action<Card> OnCardSelected;

    /// <summary>
    /// ѕровер€ем, что карта принадлежит текущему игроку и карту можно выбирать и карта в руке игрока
    /// </summary>
    /// <returns>ture если карту нельз€ передвигать, иначе false</returns>
    private bool CardIsNotDragable() => UnGameManager.Instance.LocalPlayer != UnGameManager.Instance.CurrentPlayer
        || card.Owner != UnGameManager.Instance.CurrentPlayer
        || !SelectCard || card.Position != ScaryTales.Enums.CardPosition.InHand;
    public void Initialize(IGameManager manager, Card cardData, Transform board)
    {
        gameManager = manager;
        card = cardData;
        gameBoard = board;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (CardIsNotDragable()) return;


        startPosition = transform.position;
        parentToReturnTo = transform.parent;
        transform.SetParent(transform.root);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (CardIsNotDragable()) return;

        transform.position = eventData.position;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (CardIsNotDragable()) return;

        // ≈сли карта перемещена на стол, разыгрываем еЄ
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