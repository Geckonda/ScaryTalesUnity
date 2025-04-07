using ScaryTales;
using ScaryTales.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;

namespace Assets.Scripts
{
    // Не используется, есть более специальные классы : UIEntities
    public class GameVisualManager : MonoBehaviour
    {
        private IGameContext _context;


        public Transform GameBoardPanel;
        public Transform TimeOfDaySlot;
        public Transform DiscardPile;

        private GameObject _cardPrefab;
        private Dictionary<Card, CardView> _cardToCardViewMap = new Dictionary<Card, CardView>();

        public TMP_Text Player1Name;
        public TMP_Text Player2Name;
        public TMP_Text Player1ScoreText;
        public TMP_Text Player2ScoreText;
        private Dictionary<Player, TMP_Text> _playerScorePanels;
        public TMP_Text CurrentPlayerText;

        private Player _currentPlayer;


        void Start()
        {
            _context = UnGameManager.Instance.GameManager._context;

            // Подписываемся на события
            _context.GameManager.OnCardPlayed += HandleCardPlayed;
            _context.GameManager.OnCardMovedToDiscardPile += HandleCardMovedToDiscardPile;
            _context.GameManager.OnCardMovedToBoard += HandleCardMovedToBoard;
            _context.GameManager.OnCardMovedToTimeOfDaySlot += HandleCardMovedToTimeOfDaySlot;
            _context.GameManager.OnMessagePrinted += HandleMessagePrinted;
            _context.GameManager.OnAddPointsToPlayer += HandleAddPointsToPlayer;


                _playerScorePanels = new()
            {
                { _context.Players[0], Player1ScoreText },
                { _context.Players[1], Player2ScoreText }
            };
        }

        private void HandleAddPointsToPlayer(Player player)
        {
            if (_playerScorePanels.TryGetValue(player, out TMP_Text panel))
            {
                panel.text = player.Score.ToString();
            }
        }

        private void HandleCardPlayed(Card card)
        {
            Debug.Log($"Карта {card.Name} разыграна");
            // Здесь можно добавить анимацию или UI-эффект
        }

        private void HandleCardMovedToDiscardPile(Card card)
        {
            Debug.Log($"Карта {card.Name} перемещена в сброс");

            CardView cardView = GetCardView(card);
            if (cardView != null)
            {
                cardView.transform.SetParent(DiscardPile); // Перемещаем CardView в сброс
                cardView.transform.SetAsLastSibling(); // Убедимся, что карта отображается поверх остальных
            }
            else
            {
                Debug.LogError($"CardView для карты {card.Name} не найден!");
            }
        }

        private void HandleCardMovedToBoard(Card card)
        {
            Debug.Log($"Карта {card.Name} перемещена на стол");
        }

        private void HandleCardMovedToTimeOfDaySlot(Card card)
        {
            Debug.Log($"Карта {card.Name} перемещена в слот времени суток");

            // Удаляем старую карту из слота (если есть)
            Transform child = TimeOfDaySlot.GetChild(0);
            child.SetParent(DiscardPile);

            // Получаем CardView для текущей карты
            CardView cardView = GetCardView(card);
            if (cardView != null)
            {
                // Перемещаем CardView в слот времени суток
                cardView.transform.SetParent(TimeOfDaySlot);
            }
            else
            {
                Debug.LogError($"CardView для карты {card.Name} не найден!");
            }
        }


        private void HandleMessagePrinted(string message)
        {
            //Debug.Log(message);
            // Здесь можно отобразить сообщение в UI
        }
        private void CardPrefubInit()
        {
            _cardPrefab = Resources.Load<GameObject>("CardPrefab");

            if (_cardPrefab == null)
            {
                Debug.LogError("Префаб карты не найден!");
            }
        }
        public void CreateCardView(Card card, Transform parent)
        {
            GameObject cardInstance = Instantiate(_cardPrefab, parent);
            CardView cardView = cardInstance.GetComponent<CardView>();

            if (cardView == null)
            {
                Debug.LogError("Компонент CardView не найден!");
                return;
            }

            cardView.Initialize(card);
            _cardToCardViewMap[card] = cardView; // Добавляем связь в словарь

            DragAndDrop dragAndDrop = cardInstance.GetComponent<DragAndDrop>();
            if (dragAndDrop != null)
            {
                dragAndDrop.Initialize(_context.GameManager, card, GameBoardPanel);
            }
        }
        public void UpdateCurrentPlayerText()
        {
            if (CurrentPlayerText != null)
            {
                CurrentPlayerText.text = $"Сейчас ходит: {_currentPlayer.Name}";
            }
        }

        public CardView GetCardView(Card card)
        {
            if (_cardToCardViewMap.TryGetValue(card, out CardView cardView))
            {
                return cardView;
            }
            return null;
        }
    }
}
