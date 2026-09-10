using ScaryTales.Abstractions;
using ScaryTales.Enums;
using System.Threading.Tasks;

namespace ScaryTales.CardEffects
{
    /// <summary>
    /// У Ведьмы нет мгновенного эффекта: в момент розыгрыша не происходит
    /// ничего, карта просто ложится перед игроком.
    ///
    /// <para>Вся её сила постоянная и живёт в переопределениях
    /// <c>Card.BlocksDeckDraw</c>, <c>Card.ExtraPlaysPerTurn</c> и
    /// <c>Card.ShouldDiscardAtTurnStart</c> — см. <c>WitchCard</c>. Пустой
    /// эффект здесь не заглушка, а честный ответ: <c>Card.Effect</c>
    /// абстрактен и требует вернуть что-то, а возвращать «начисли ноль ПО»
    /// значило бы притворяться, что эффект есть.</para>
    /// </summary>
    public class WitchEffect : ICardEffect
    {
        public CardEffectTimeType Type => CardEffectTimeType.Instant;

        public Task ApplyEffect(IGameContext context) => Task.CompletedTask;
    }
}
