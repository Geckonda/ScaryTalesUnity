using ScaryTales;
using System.Collections.Generic;
using System;
using UnityEngine;
using Assets.Scripts.Factories;

public class CardViewService
{
    private static CardViewService _instance;
    public static CardViewService Instance => _instance ??= new CardViewService();

    private readonly CardViewFactory _cardViewFactory;
    private readonly Dictionary<Card, CardView> _cardToCardViewMap = new Dictionary<Card, CardView>();

    private CardViewService()
    {
        // ѕолучаем зависимости (например, через UnGameManager)
        var gameManager = UnGameManager.Instance.GameManager;
        var gameBoardPanel = UnGameManager.Instance.GameBoardPanel;
        var cardPrefab = Resources.Load<GameObject>("CardPrefab");

        _cardViewFactory = new CardViewFactory(gameManager, gameBoardPanel, cardPrefab);
    }

    public void BundleCardAndCardView(Card card, CardView view)
    {
        if (_cardToCardViewMap.ContainsKey(card))
            throw new ArgumentException("“ака€ карта уже имеет представление.");

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
}