﻿using ScaryTales.Abstractions;
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

        public Task ApplyEffect(IGameContext context)
        {
            var state = context.GameState;
            var board = context.GameBoard;
            var manager = context.GameManager;
            var player = state.GetCurrentPlayer();
            var deck = context.Deck;

            manager.DrawCard(player);
            manager.DrawCard(player);

            var merchants = board.GetCardsOnBoard("Купец");
            manager.AddPointsToPlayer(player, merchants.Count * 2);
            return Task.CompletedTask;
        }
    }
}
