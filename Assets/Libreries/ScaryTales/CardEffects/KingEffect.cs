using ScaryTales.Abstractions;
using ScaryTales.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Assets.Libreries.ScaryTales.CardEffects
{
    public class KingEffect : ICardEffect
    {
        private readonly bool _isWisdom;
        public KingEffect(bool isWisdom)
        {
            _isWisdom = isWisdom;
        }
        public CardEffectTimeType Type => CardEffectTimeType.Instant;

        public Task ApplyEffect(IGameContext context)
        {
            var state = context.GameState;
            var board = context.GameBoard;
            var manager = context.GameManager;
            var player = state.GetCurrentPlayer();

            manager.DrawCard(player);
            var places = board.GetCardsOnBoard(CardType.Place);
            if (places.Count == 0)
            {
                manager.PrintMessage("Нет ни одной карты типа 'Место' на столе");
                return Task.CompletedTask;
            }
            var earnedPoints = places.Count;
            if ((state.IsNight && !_isWisdom) || (!state.IsNight && _isWisdom))
                manager.AddPointsToPlayer(player, earnedPoints * 2);
            else
                manager.AddPointsToPlayer(player, earnedPoints);
            return Task.CompletedTask;
        }
    }
}
