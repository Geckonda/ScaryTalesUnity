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
    public class HiddenCaveCard : Card
    {
        public override string Name => "Потаенная пещера";

        public override CardType Type => CardType.Place;

        public override int Points => 2;

        public override string EffectDescription => "Возьмите из запаса 1 волшебную палку или 1 золотую монету.";

        public override CardPosition PositionAfterPlay => CardPosition.OnGameBoard;

        public override int CardCountInDeck => 2;

        public override ICardEffect Effect
            => new ItemDistributor(new List<ItemType> { ItemType.MagicStick, ItemType.Coin});

        public override async Task ActivateEffect(IGameContext context)
        {
            await Effect.ApplyEffect(context);
        }

        public override Card Clone()
        {
            return new HiddenCaveCard();
        }
    }
}
