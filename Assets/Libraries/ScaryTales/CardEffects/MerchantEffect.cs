using ScaryTales.Abstractions;
using ScaryTales.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ScaryTales.CardEffects
{
    public class MerchantEffect : ICardEffect
    {
        public CardEffectTimeType Type => CardEffectTimeType.Instant;

        public async Task ApplyEffect(IGameContext context)
        {
            var state = context.GameState;
            var board = context.GameBoard;
            var manager = context.GameManager;
            var player = state.GetCurrentPlayer();
            var deck = context.Deck;

            manager.DrawCard(player);
            // Две карты подряд без паузы сливаются в одно движение — игрок не
            // видит, что взял именно две.
            await context.Clock.Beat(300);
            manager.DrawCard(player);

            var merchants = board.GetCardsOnBoard("Купец");
            manager.AddPointsToPlayer(player, merchants.Count * 2);
            return;
        }
    }
}
