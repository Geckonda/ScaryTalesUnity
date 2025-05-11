using ScaryTales.Abstractions;
using ScaryTales.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Assets.Libreries.ScaryTales.CardEffects
{
    public class CardStealEffect : ICardEffect
    {
        private readonly List<CardType> _cardsTypes;
        public CardStealEffect(List<CardType> cardTypes)
        {
            _cardsTypes = cardTypes;
        }
        public CardEffectTimeType Type => CardEffectTimeType.Instant;

        public async Task ApplyEffect(IGameContext context)
        {
            if (!_cardsTypes.Any())
                throw new ArgumentNullException("В коллекции нет ни одного типа");

            var state = context.GameState;
            var board = context.GameBoard;
            var manager = context.GameManager;
            var player = state.GetCurrentPlayer();
            bool anyCard = false;
            foreach (var cardType in _cardsTypes)
            {
                var cards = board.GetCardsOnBoard(cardType);
                if (!cards.Any())
                {
                    PrintAbsentCardTypes(cardType);
                    continue;
                }
                anyCard = true;
                var card = await player.SelectCardAmongOthers(cards);
                board.RemoveCardFromBoard(card);
                manager.PutCardInPlayerHand(card, player);
            }
            if (!anyCard)
                PrintAbsentCardTypes(null);
        }
        private string PrintAbsentCardTypes(CardType? type)
        {
            switch (type)
            {
                case CardType.Woman:
                    return "Нет ни одной карты типа 'Женщина' на столе.";
                case CardType.Place:
                    return "Нет ни одной карты типа 'Место' на столе.";
                case CardType.Man:
                    return "Нет ни одной карты типа 'Мужчина' на столе.";
                case CardType.Event:
                    return "Нет ни одной карты типа 'Событие' на столе.";
                case CardType.Monster:
                    return "Нет ни одной карты типа 'Злодей' на столе.";
                default:
                    return "Не нашлось ни одной карты для сброса.";
            }
        }
    }
}
