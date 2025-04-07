using ScaryTales.Abstractions;
using ScaryTales;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Assets.Scripts.Factories
{
    public class CardViewFactory
    {
        private readonly IGameManager _gameManager;
        private readonly Transform _gameBoardPanel;
        private readonly GameObject _cardPrefab;

        public CardViewFactory(IGameManager gameManager, Transform gameBoardPanel, GameObject cardPrefab)
        {
            _gameManager = gameManager;
            _gameBoardPanel = gameBoardPanel;
            _cardPrefab = cardPrefab;
        }

        public CardView CreateCardView(Card card, Transform parent)
        {
            if (_cardPrefab == null) return null;

            GameObject cardInstance = GameObject.Instantiate(_cardPrefab, parent);
            CardView cardView = cardInstance.GetComponent<CardView>();

            if (cardView == null)
            {
                Debug.LogError("Компонент CardView не найден!");
                return null;
            }

            cardView.Initialize(card);

            // Инициализация DragAndDrop
            DragAndDrop dragAndDrop = cardInstance.GetComponent<DragAndDrop>();
            if (dragAndDrop != null)
            {
                dragAndDrop.Initialize(_gameManager, card, _gameBoardPanel);
            }

            return cardView;
        }
    }
}
