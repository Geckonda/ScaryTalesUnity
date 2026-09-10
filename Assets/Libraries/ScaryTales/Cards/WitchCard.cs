using ScaryTales.Abstractions;
using ScaryTales.CardEffects;
using ScaryTales.Enums;
using System.Threading.Tasks;

namespace ScaryTales.Cards
{
    /// <summary>
    /// Ведьма: сделка. Пока она перед вами, вы играете быстрее, но рука больше
    /// не пополняется — и однажды кончается. Три правила ниже действуют, только
    /// пока карта лежит на столе, поэтому выражены переопределениями
    /// постоянных правил <see cref="Card"/>, а не мгновенным эффектом.
    /// </summary>
    public class WitchCard : Card
    {
        public override string Name => "Ведьма";

        public override CardType Type => CardType.Woman;

        public override int Points => 0;

        public override string EffectDescription => "Вы не можете брать карты из колоды. Можете играть 1 дополнительную карту в свой ход. Сбросьте ведьму в начале своего хода, если у вас не осталось карт на руке или если карты закончились в колоде.";

        public override CardPosition PositionAfterPlay => CardPosition.BeforePlayer;

        public override int CardCountInDeck => 2;

        public override ICardEffect Effect => new WitchEffect();

        /// <inheritdoc/>
        public override bool BlocksDeckDraw => true;

        /// <inheritdoc/>
        public override int ExtraPlaysPerTurn => 1;

        /// <summary>
        /// «Сбросьте ведьму в начале своего хода, если у вас не осталось карт
        /// на руке или если карты закончились в колоде.»
        ///
        /// <para>Оба условия — про исчерпание: брать больше неоткуда или
        /// нечего играть. Без этого правила запрет на взятие подвесил бы
        /// партию: рука пустеет, а конец партии наступает как раз по пустой
        /// руке после взятия, которого Ведьма не даёт сделать.</para>
        /// </summary>
        public override bool ShouldDiscardAtTurnStart(IGameContext context)
        {
            if (Owner == null) return false;

            return Owner.HandCardCount == 0 || context.Deck.CardsRemaining == 0;
        }

        public override async Task ActivateEffect(IGameContext context)
        {
            await Effect.ApplyEffect(context);
        }

        public override Card Clone()
        {
            return new WitchCard();
        }
    }
}
