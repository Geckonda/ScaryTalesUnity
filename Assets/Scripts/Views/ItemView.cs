using ScaryTales;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class ItemView : MonoBehaviour, IPointerClickHandler
{
    public event Action<Item> OnItemClicked;

    public TMP_Text itemNameText;
    public Image highlightFrame; // Подсветка предмета

    private Item _item;

    public void Initialize(Item item)
    {
        _item = item;
        DisplayItem();
        SetHighlight(false); // Отключаем подсветку по умолчанию
    }

    public void DisplayItem()
    {
        itemNameText.text = _item.Name;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        OnItemClicked?.Invoke(_item);
    }

    /// <summary>
    /// Включает или выключает рамку (подсветку)
    /// </summary>
    public void SetHighlight(bool isHighlighted)
    {
        if (highlightFrame != null)
            highlightFrame.gameObject.SetActive(isHighlighted);
    }
}
