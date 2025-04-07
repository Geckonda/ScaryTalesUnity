using System.Collections.Generic;
using UnityEngine;

public class ItemContainer : MonoBehaviour
{
    public static ItemContainer Instance { get; private set; }

    public Transform contentPanel; // Куда добавляются предметы

    private void Awake()
    {
        Instance = this;
        gameObject.SetActive(false); // По умолчанию скрыто
    }

    public void Show(List<ItemView> itemViews)
    {
        gameObject.SetActive(true);



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
    }
    public void Hide()
    {
        gameObject.SetActive(false);
    }
}
