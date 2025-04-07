﻿using ScaryTales.Abstractions;
using ScaryTales.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ScaryTales.CardEffects
{
    /// <summary>
    /// Эффект, позхволяивающйи накапливать фиксированное количество ПО
    /// </summary>
    public class PassiveFixPointsFarmEffect : ICardEffect
    {
        private readonly int _points;
        /// <summary>
        /// Создает эффект фиксированной добычи ПО
        /// </summary>
        /// <param name="points">Размер ПО</param>
        public PassiveFixPointsFarmEffect(int points)
        {
            _points = points;
        }
        public CardEffectTimeType Type => CardEffectTimeType.PermanentAtTheEnd;

        public Task ApplyEffect(IGameContext context)
        {
            var state = context.GameState;
            var manager = context.GameManager;
            var player = state.GetCurrentPlayer();

            manager.AddPointsToPlayer(player, _points);
            return Task.CompletedTask;
        }
    }
}
