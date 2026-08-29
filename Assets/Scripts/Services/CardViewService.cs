using ScaryTales;
using System.Collections.Generic;
using System;
using UnityEngine;
using Assets.Scripts.Factories;

public class CardViewService
{
    private static CardViewService _instance;
    public static CardViewService Instance => _instance ??= new CardViewService();

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

    private readonly CardViewFactory _cardViewFactory;
    private readonly Dictionary<Card, CardView> _cardToCardViewMap = new Dictionary<Card, CardView>();

    public CardViewFactory CardViewFactory => _cardViewFactory;
    private CardViewService()
    {
        var gameBoardPanel = UnGameManager.Instance.GameBoardPanel;
        var cardPrefab = Resources.Load<GameObject>("CardPrefab");

        _cardViewFactory = new CardViewFactory(gameBoardPanel, cardPrefab);
    }

    public void BundleCardAndCardView(Card card, CardView view)
    {
        if (_cardToCardViewMap.ContainsKey(card))
            throw new ArgumentException("Такая карта уже имеет представление.");

        _cardToCardViewMap.Add(card, view);
    }

    public CardView GetCardView(Card card)
    {
        _cardToCardViewMap.TryGetValue(card, out CardView cardView);
        return cardView;
    }

    public CardView CreateCardView(Card card, Transform parent)
    {
        var cardView = _cardViewFactory.CreateCardView(card, parent);
        if (cardView != null)
        {
            _cardToCardViewMap[card] = cardView;
        }
        return cardView;
    }

    /// <summary>
    /// Создает клона на один раз
    /// </summary>
    /// <param name="card">Обычная карта</param>
    public CardView CreateSingleCardViewClone(Card card, Transform parent)
    {
        var cardView = _cardViewFactory.CreateCardView(card.Clone(), parent);

        return cardView;
    }
}