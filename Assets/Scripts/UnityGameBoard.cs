using ScaryTales;
using ScaryTales.Abstractions;
using ScaryTales.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Assets.Scripts
{
    public class UnityGameBoard : MonoBehaviour, IGameBoard
    {
        public void AddCardOnBoard(Card card)
        {
            throw new NotImplementedException();
        }

        public void AddCardToDiscardPile(Card card)
        {
            throw new NotImplementedException();
        }

        public int CardsOnBoardCount()
        {
            throw new NotImplementedException();
        }

        public int DiscardPileCount()
        {
            throw new NotImplementedException();
        }

        public Card GetCardFromTimeOfDaySlot()
        {
            throw new NotImplementedException();
        }

        public List<Card> GetCardsFromDiscardPile()
        {
            throw new NotImplementedException();
        }

        public List<Card> GetCardsOnBoard()
        {
            throw new NotImplementedException();
        }

        public List<Card> GetCardsOnBoard(Player player)
        {
            throw new NotImplementedException();
        }

        public List<Card> GetCardsOnBoard(CardType type)
        {
            throw new NotImplementedException();
        }

        public List<Card> GetCardsOnBoard(string name)
        {
            throw new NotImplementedException();
        }

        public void MoveAllCardsToDiscardPile()
        {
            throw new NotImplementedException();
        }

        public void MoveCardFromBoardToDiscardPile(Card card)
        {
            throw new NotImplementedException();
        }

        public void RemoveCardFromBoard(Card card)
        {
            throw new NotImplementedException();
        }

        public void SetTimeOfDaySlot(Card card)
        {
            throw new NotImplementedException();
        }
    }
}
