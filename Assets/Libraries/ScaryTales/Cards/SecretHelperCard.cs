using ScaryTales.Abstractions;
using ScaryTales.CardEffects;
using ScaryTales.Enums;
using System.Threading.Tasks;

namespace ScaryTales.Cards
{
    public class SecretHelperCard : Card
    {
        public override string Name => "Тайная помощница";

        public override CardType Type => CardType.Woman;

        public override int Points => 1;

        public override string EffectDescription => "Посмотрите 2 верхние карты колоды. Добавьте одну на руку, вторую удалите из игры. Можно разыграть 1 дополнительную карту в этом ходу.";

        public override CardPosition PositionAfterPlay => CardPosition.Discarded;

        public override int CardCountInDeck => 2;

        public override ICardEffect Effect => new SecretHelperEffect();

        public override async Task ActivateEffect(IGameContext context)
        {
            await Effect.ApplyEffect(context);
        }

        public override Card Clone()
        {
            return new SecretHelperCard();
        }
    }
}
