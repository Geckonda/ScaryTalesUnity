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
    public class GypsyCard : Card
    {
        public override string Name => "Цыганка";

        public override CardType Type => CardType.Woman;

        public override int Points => 1;

        public override string EffectDescription => "Все игроки показывают карты на руках. Получите 1 ПО за каждое раскрытое место. Возьмите 1 карту из колоды за каждую раскрытую цыганку.";

        public override CardPosition PositionAfterPlay => CardPosition.OnGameBoard;

        public override int CardCountInDeck => 4;

        public override ICardEffect Effect => new GypsyEffect();

        public override async Task ActivateEffect(IGameContext context)
        {
            await Effect.ApplyEffect(context);
        }

        public override Card Clone()
        {
            return new GypsyCard();
        }
    }
}
