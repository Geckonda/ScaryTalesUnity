using ScaryTales.Abstractions;
using ScaryTales.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Assets.Libreries.ScaryTales.CardEffects
{
    public class SwapGameTimeEffect : ICardEffect
    {
        private readonly bool _isNight;
        public SwapGameTimeEffect(bool isNight)
        {
            _isNight = isNight;
        }
        public CardEffectTimeType Type => CardEffectTimeType.Instant;

        public Task ApplyEffect(IGameContext context)
        {
            var state = context.GameState;
            var manager = context.GameManager;
            if (!_isNight)
                manager.PrintMessage("Наступает день!");
            else
                manager.PrintMessage("Наступает ночь!");
            state.SetPhase(_isNight);
            return Task.CompletedTask;
        }
    }
}
