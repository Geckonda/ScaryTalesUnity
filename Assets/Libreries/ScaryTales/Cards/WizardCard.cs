﻿using ScaryTales.Abstractions;
using ScaryTales.CardEffects;
using ScaryTales.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ScaryTales.Cards
{
    public class WizardCard : Card
    {
        public override string Name => "Волшебник";

        public override CardType Type => CardType.Man;

        public override int Points => 2;

        public override string EffectDescription => "Раскройте верхнюю карту колоды и немедленно ее разыграйте.";

        public override CardPosition PositionAfterPlay => CardPosition.OnGameBoard;

        public override int CardCountInDeck => 2;

        public override ICardEffect Effect => new WizardEffect();

        public override async Task ActivateEffect(IGameContext context)
        {
            await Effect.ApplyEffect(context);
        }

        public override Card Clone()
        {
            return new WizardCard();
        }
    }
}
