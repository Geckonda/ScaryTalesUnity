using Assets.Scripts.Utilities;
using ScaryTales;
using UnityEngine;

namespace Assets.Scripts.Factories
{
    public class CardViewFactory
    {
        private readonly Transform _gameBoardPanel;
        private readonly GameObject _cardPrefab;

        public CardViewFactory(Transform gameBoardPanel, GameObject cardPrefab)
        {
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

            DragAndDrop dragAndDrop = cardInstance.GetComponent<DragAndDrop>();
            if (dragAndDrop != null)
            {
                dragAndDrop.Initialize(card, _gameBoardPanel, parent);

                if (CardSelectionService.CurrentSelectionHandler != null)
                {
                    dragAndDrop.OnCardSelected += CardSelectionService.CurrentSelectionHandler;
                }
            }

            return cardView;
        }

        public void EnableDrag(CardView cardView)
        {
            var dragAndDrop = cardView.GetComponent<DragAndDrop>();
            if (dragAndDrop == null)
                dragAndDrop = cardView.gameObject.AddComponent<DragAndDrop>();

            dragAndDrop.Initialize(cardView._card, _gameBoardPanel, cardView.transform.parent);
        }

        public void DisableDrag(CardView cardView)
        {
            var dragAndDrop = cardView.GetComponent<DragAndDrop>();
            if (dragAndDrop != null)
                GameObject.Destroy(dragAndDrop);
        }
    }
}
