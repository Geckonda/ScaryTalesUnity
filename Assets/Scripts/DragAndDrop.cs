using Assets.Scripts.Utilities;
using ScaryTales;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
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
        // Раз OnInit увёл карту в корень канваса — вернуть её обязаны, даже
        // если за время перетаскивания она перестала быть перетаскиваемой
        // (кончился ход, партию прервали). Прежний ранний выход по
        // CardIsNotDragable() оставлял её лежать поверх стола вне раскладки.
        if (!dragStarted) return;

        dragStarted = false; // сброс состояния

        // Defensive: if gameBoard or its RectTransform was destroyed (Unity's
        // null-overload returns true for a destroyed reference), fall through
        // to the return-to-hand branch instead of throwing — otherwise the
        // card stays glued to the cursor's release position.
        var rect = (gameBoard != null) ? gameBoard.GetComponent<RectTransform>() : null;
        bool dropOnBoard = !CardIsNotDragable()
            && rect != null
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
            ReturnToHand();
        }
    }

    /// <summary>
    /// Вернуть карту в руку её владельца.
    ///
    /// <para><b>Родителя ищем на месте, а не берём запомненного при
    /// <see cref="Initialize"/>.</b> Тот приходит из двух мест с разным
    /// смыслом: <c>CardViewFactory.CreateCardView</c> отдаёт родителя на
    /// момент создания — а карты создаются в колоде, — и только
    /// <c>EnableDrag</c> отдаёт руку. При этом <c>EnableDrag</c> проходит по
    /// руке лишь в начале хода, поэтому карта, добранная эффектом посреди
    /// хода, оставалась с родителем-колодой или вовсе без него — и тогда
    /// после броска мимо стола лежала статично в корне канваса.</para>
    /// </summary>
    private void ReturnToHand()
    {
        var hand = ResolveHandPanel();
        if (hand != null)
        {
            // worldPositionStays: false — панель руки повёрнута к центру
            // стола, и сохранение мировой позы оставило бы карте
            // компенсирующий локальный трансформ, который дерётся с
            // раскладкой. Тот же приём, что в PlayerHandUI.AnimateCardToHand.
            transform.SetParent(hand, false);

            // Позицию, поворот и масштаб карте назначит FanLayoutGroup; без
            // немедленной пересборки она сделала бы это лишь на следующем
            // проходе раскладки, и карта успела бы мелькнуть не на месте.
            var handRect = hand.GetComponent<RectTransform>();
            if (handRect != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(handRect);
            return;
        }

        // Руку определить не удалось — например, партия уже кончилась и UI
        // разобран. Остаётся прежнее поведение: запомненный родитель, каким
        // бы он ни был, и позиция начала перетаскивания. Ставить его через
        // worldPositionStays: false здесь нельзя — этот родитель запросто
        // окажется колодой, и карта прыгнула бы в неё.
        if (parentToReturnTo != null)
            transform.SetParent(parentToReturnTo);
        transform.position = startPosition;
    }

    private Transform ResolveHandPanel()
    {
        if (card?.Owner == null) return null;

        var manager = UnGameManager.Instance;
        var handUI = (manager != null) ? manager._playerHandUI : null;
        return (handUI != null) ? handUI.GetHandPanel(card.Owner) : null;
    }

    private void OnInit()
    {
        startPosition = transform.position;
        transform.SetParent(transform.root);
        // Без этого флаг оставался false, и OnDrag звал OnInit на каждом
        // кадре — startPosition затиралась текущей позицией, так что «вернуть
        // на место» возвращало карту под курсор, а не в руку.
        dragStarted = true;
    }
    public void ClearAllListeners()
    {
        OnCardSelected = null;
    }

}