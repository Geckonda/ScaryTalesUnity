﻿using ScaryTales.Abstractions;
using ScaryTales.Enums;
using ScaryTales.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ScaryTales.CardEffects
{
    public class PrincessEffect : ICardEffect
    {
        public CardEffectTimeType Type => CardEffectTimeType.Instant;

        public async Task ApplyEffect(IGameContext context)
        {
            var state = context.GameState;
            var board = context.GameBoard;
            var manager = context.GameManager;
            var player = state.GetCurrentPlayer();

            var men = board.GetCardsOnBoard(CardType.Man);
            if (!men.Any())
            {
                manager.PrintMessage("Нет ни одной карты типа 'Мужчина' на столе.");
                return;
            }
            var man = await player.SelectCardAmongOthers(men);
            board.RemoveCardFromBoard(man);
            manager.PutCardInPlayerHand(man, player);
        }
    }
}
