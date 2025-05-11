﻿using Assets.Libreries.ScaryTales.CardEffects;
using ScaryTales.Abstractions;
using ScaryTales.CardEffects;
using ScaryTales.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ScaryTales.Cards
{
    public class NightCard : Card
    {

        public override string Name => "Ночь";

        public override CardType Type => CardType.Event;

        public override int Points => 2;

        public override string EffectDescription => "Сейчас ночь. Сбросьте разыгранную карту 'День'";

        public override CardPosition PositionAfterPlay => CardPosition.TimeOfDay;

        public override int CardCountInDeck => 4;

        public override ICardEffect Effect => new SwapGameTimeEffect(true);

        public override async Task ActivateEffect(IGameContext context)
        {
            var card = context.GameBoard.GetCardFromTimeOfDaySlot()!;
            context.GameManager.PutCardToDiscardPile(card);
            if (context.GameState.IsNight == false)
                await Effect.ApplyEffect(context);
        }

        public override Card Clone()
        {
            return new NightCard();
        }
    }
}
