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
    public Image background;

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
        SetColorItem();
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
    private void SetColorItem()
    {
        switch (_item.Type)
        {
            case ScaryTales.Enums.ItemType.Coin:
                background.color = new Color(255, 200, 0);
                break;
            case ScaryTales.Enums.ItemType.Sword:
                background.color = Color.red;
                break;
            case ScaryTales.Enums.ItemType.Armor:
                background.color = new Color(139, 0, 255);
                break;
            case ScaryTales.Enums.ItemType.MagicStick:
                background.color = Color.green;
                break;
            default:
                break;
        }
    }
}
