using ScaryTales;
using System.Collections.Generic;
using System;
using UnityEngine;
using Assets.Scripts.Factories;

public class ItemViewService
{
    private static ItemViewService _instance;
    public static ItemViewService Instance => _instance ??= new ItemViewService();

    private readonly ItemViewFactory _itemViewFactory;
    private readonly Dictionary<Item, ItemView> _itemToViewMap = new();

    private ItemViewService()
    {
        var gameManager = UnGameManager.Instance.GameManager;
        var gameBoardPanel = UnGameManager.Instance.GameBoardPanel;
        var itemPrefab = Resources.Load<GameObject>("ItemPrefab");

        _itemViewFactory = new ItemViewFactory(gameManager, gameBoardPanel, itemPrefab);
    }

    public void BundleItemAndView(Item item, ItemView view)
    {
        if (_itemToViewMap.ContainsKey(item))
            throw new ArgumentException("Этот предмет уже имеет представление.");

        _itemToViewMap.Add(item, view);
    }

    public ItemView GetItemView(Item item)
    {
        _itemToViewMap.TryGetValue(item, out ItemView itemView);
        return itemView;
    }

    public ItemView CreateItemView(Item item, Transform parent)
    {
        var itemView = _itemViewFactory.CreateItemView(item, parent);
        if (itemView != null)
        {
            _itemToViewMap[item] = itemView;
        }
        return itemView;
    }
}
