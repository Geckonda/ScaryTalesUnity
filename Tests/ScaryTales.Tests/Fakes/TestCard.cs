using System.Threading.Tasks;
using ScaryTales;
using ScaryTales.Abstractions;
using ScaryTales.Enums;

namespace ScaryTales.Tests.Fakes
{
    /// <summary>
    /// Карта, которую можно собрать под конкретный тест.
    ///
    /// <para>Настоящие карты каталога прибиты гвоздями: количество в колоде,
    /// очки и позиция у них — переопределённые свойства без сеттеров, и колоду
    /// из «трёх карт по одной штуке» из них не собрать. Здесь всё задаётся
    /// конструктором.</para>
    ///
    /// <para>Для тестов про сам каталог это не годится и не нужно — там
    /// проверяются как раз настоящие карты.</para>
    /// </summary>
    public class TestCard : Card
    {
        private readonly string _name;
        private readonly CardType _type;
        private readonly int _points;
        private readonly CardPosition _positionAfterPlay;
        private readonly int _countInDeck;
        private readonly ICardEffect _effect;

        public TestCard(
            string name = "Тестовая карта",
            CardType type = CardType.Event,
            int points = 0,
            CardPosition positionAfterPlay = CardPosition.OnGameBoard,
            int countInDeck = 1,
            ICardEffect effect = null)
        {
            _name = name;
            _type = type;
            _points = points;
            _positionAfterPlay = positionAfterPlay;
            _countInDeck = countInDeck;
            _effect = effect ?? new NoEffect();
        }

        public override string Name => _name;
        public override CardType Type => _type;
        public override int Points => _points;
        public override string EffectDescription => "—";
        public override CardPosition PositionAfterPlay => _positionAfterPlay;
        public override int CardCountInDeck => _countInDeck;
        public override ICardEffect Effect => _effect;

        /// <summary>Сколько раз у карты спрашивали её эффект.</summary>
        public int ActivationCount { get; private set; }

        public override Task ActivateEffect(IGameContext context)
        {
            ActivationCount++;
            return _effect.ApplyEffect(context);
        }

        public override Card Clone() =>
            new TestCard(_name, _type, _points, _positionAfterPlay, _countInDeck, _effect);

        private class NoEffect : ICardEffect
        {
            public CardEffectTimeType Type => CardEffectTimeType.Instant;
            public Task ApplyEffect(IGameContext context) => Task.CompletedTask;
        }
    }
}
