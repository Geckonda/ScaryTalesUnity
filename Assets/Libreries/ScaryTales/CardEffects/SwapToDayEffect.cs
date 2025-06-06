﻿using ScaryTales.Abstractions;
using ScaryTales.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ScaryTales.CardEffects
{
    public class SwapToDayEffect : ICardEffect
    {
        public CardEffectTimeType Type => CardEffectTimeType.Instant;

        public Task ApplyEffect(IGameContext context)
        {
            var state = context.GameState;
            var manager = context.GameManager;

            manager.PrintMessage("Наступает день!");
            state.SetPhase(false);
            return Task.CompletedTask;
        }
    }
}
