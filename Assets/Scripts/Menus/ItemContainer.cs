using ScaryTales;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ItemContainer : MonoBehaviour
{
    public static ItemContainer Instance { get; private set; }

    public Transform contentPanel; // Куда добавляются предметы
    [SerializeField] private Button ShowBtn;
    [SerializeField] private Button CloseBtn;
    [SerializeField] private TMP_Text IsEmptyText;

    private List<ItemView> _itemViews = new List<ItemView>();

    private void Awake()
    {
        Instance = this;
        gameObject.SetActive(false); // По умолчанию скрыто
    }
    public void Show(List<Item> items)
    {
        gameObject.SetActive(true);
        ShowBtn.gameObject.SetActive(false);

        if (items.Count == 0)
        {
            IsEmptyText.gameObject.SetActive(true);
            return;
        }
        else
            IsEmptyText.gameObject.SetActive(false);

        ConvertItemsToViews(items);
        foreach (var itemView in _itemViews)
        {
            itemView.transform.SetParent(contentPanel, true);
        }
    }

    public void Show(List<ItemView> itemViews, bool isClosedWindow)
    {
        gameObject.SetActive(true);
        ShowBtn.gameObject.SetActive(false);

        if (itemViews.Count == 0)
        {
            IsEmptyText.gameObject.SetActive(true);
            return;
        }
        else
            IsEmptyText.gameObject.SetActive(false);

        if (!isClosedWindow)
        {
            CloseBtn.gameObject.SetActive(false);
        }

        // Размещаем все переданные itemView в контейнере
        foreach (var itemView in itemViews)
        {
            itemView.transform.SetParent(contentPanel, true);
        }
        Debug.Log($"contentPanel has {contentPanel.childCount} children after adding items.");
    }
    public void ClearContentPanelchildren()
    {
        // Очищаем контейнер перед добавлением новых элементов
        foreach (Transform child in contentPanel)
        {
            Destroy(child.gameObject);
            Debug.Log($"Destroying {child.name}");
        }
        _itemViews.Clear();
    }
    public void Hide()
    {
        gameObject.SetActive(false);
        ClearContentPanelchildren();
        ShowBtn.gameObject.SetActive(true);
        CloseBtn.gameObject.SetActive(true);
    }

    private void ConvertItemsToViews(List<Item> items)
    {
        foreach (var item in items)
        {
            var itemView = ItemViewService.Instance.CreateItemView(item, contentPanel);
            if (itemView != null)
            {
                Debug.Log($"Предмет '{item.Name}' доступен для выбора");
                _itemViews.Add(itemView);
            }
        }
    }
}
