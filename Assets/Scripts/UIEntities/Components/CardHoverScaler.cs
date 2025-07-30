using UnityEngine;
using UnityEngine.EventSystems;

public class CardHoverScaler : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private float hoverScaleMultiplier = 1.5f;

    private Vector3 originalScale;
    private bool isHovered = false;
    private DragAndDrop dragAndDrop;

    private void Awake()
    {
        dragAndDrop = GetComponent<DragAndDrop>();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (isHovered || (dragAndDrop != null && dragAndDrop.IsDragging))
            return;

        originalScale = transform.localScale;
        transform.localScale = originalScale * hoverScaleMultiplier;
        isHovered = true;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (!isHovered || (dragAndDrop != null && dragAndDrop.IsDragging))
            return;

        transform.localScale = originalScale;
        isHovered = false;
    }
}
