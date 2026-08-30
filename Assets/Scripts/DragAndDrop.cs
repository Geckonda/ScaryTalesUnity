using Assets.Scripts.Utilities;
using ScaryTales;
using UnityEngine;
using UnityEngine.EventSystems;
using System;

public class DragAndDrop : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    private Vector3 startPosition;
    private Transform parentToReturnTo;
    private Transform gameBoard;
    private Card card;

    /// <summary>
    /// Доступна ли возможность выбора карты
    /// </summary>
    public static bool SelectCard {  get; set; }
    public Card Card => card;

    public event Action<Card> OnCardSelected;

    // Выполняем действия инициализации, которые обычно в OnBeginDrag:
    private bool dragStarted = false;

    /// <summary>
    /// Проверяем, что карта принадлежит текущему игроку и карту можно выбирать и карта в руке игрока
    /// </summary>
    /// <returns>ture если карту нельзя передвигать, иначе false</returns>
    private bool CardIsNotDragable() => UnGameManager.Instance.LocalPlayer != UnGameManager.Instance.CurrentPlayer
        || card.Owner != UnGameManager.Instance.CurrentPlayer
        || !SelectCard || card.Position != ScaryTales.Enums.CardPosition.InHand;
    public void Initialize(Card cardData, Transform board, Transform parent)
    {
        card = cardData;
        gameBoard = board;
        parentToReturnTo = parent;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (CardIsNotDragable()) return;

        OnInit();

        Debug.Log($"DaD: {this.card}");
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (CardIsNotDragable()) return;
        if (!dragStarted)
            OnInit();

        var yOffset = new Vector2(0, -125);
        transform.localRotation = Quaternion.identity;
        transform.position = eventData.position + yOffset;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (CardIsNotDragable()) return;

        dragStarted = false; // сброс состояния

        // Defensive: if gameBoard or its RectTransform was destroyed (Unity's
        // null-overload returns true for a destroyed reference), fall through
        // to the return-to-hand branch instead of throwing — otherwise the
        // card stays glued to the cursor's release position.
        var rect = (gameBoard != null) ? gameBoard.GetComponent<RectTransform>() : null;
        bool dropOnBoard = rect != null
            && RectTransformUtility.RectangleContainsScreenPoint(rect, eventData.position);

        if (dropOnBoard)
        {
            OnCardSelected?.Invoke(card);
            // Cards dealt during the initial deal are created before
            // WaitForLocalCardPlay sets the selection handler, so the
            // factory's per-instance subscription is skipped for them and
            // their OnCardSelected event has no listeners. Fire the live
            // static handler directly so those cards can still be played.
            // Idempotent for cards that already had the per-instance
            // subscription wired (the handler just sets two flags).
            CardSelectionService.CurrentSelectionHandler?.Invoke(card);
            transform.SetParent(gameBoard);
        }
        else
        {
            if (parentToReturnTo != null)
                transform.SetParent(parentToReturnTo);
            transform.position = startPosition;
        }
    }
    private void OnInit()
    {
        startPosition = transform.position;
        transform.SetParent(transform.root);
    }
    public void ClearAllListeners()
    {
        OnCardSelected = null;
    }

}