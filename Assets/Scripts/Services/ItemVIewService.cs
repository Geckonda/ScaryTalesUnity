using ScaryTales;
using System.Collections.Generic;
using System;
using UnityEngine;
using Assets.Scripts.Factories;

public class ItemViewService
{
    private static ItemViewService _instance;
    public static ItemViewService Instance => _instance ??= new ItemViewService();

    /// <summary>
    /// Drops the cached instance so the next access builds a fresh one.
    ///
    /// This is a plain C# static, so it is NOT subject to Unity's fake-null:
    /// it survives the scene reload that ends every game, still holding the
    /// destroyed scene's Transforms and views. Creating a card against a
    /// destroyed parent gives it no parent at all, which is why cards ended up
    /// loose in the scene on a second game. Called from UnGameManager.Awake,
    /// which is once per scene load — exactly the lifetime these want.
    /// </summary>
    public static void Reset() => _instance = null;

    private readonly ItemViewFactory _itemViewFactory;
    private readonly Dictionary<Item, ItemView> _itemToViewMap = new();

    private ItemViewService()
    {
        var itemPrefab = Resources.Load<GameObject>("ItemPrefab");
        _itemViewFactory = new ItemViewFactory(itemPrefab);
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
