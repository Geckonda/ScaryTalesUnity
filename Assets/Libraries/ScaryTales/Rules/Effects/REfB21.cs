using Assets.Libraries.ScaryTales.Abstractions;
using ScaryTales;
using ScaryTales.Abstractions;
using ScaryTales.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Assets.Libraries.ScaryTales.Rules.Effects
{
    public class REfB21 : IFinalRuleEffect
    {
        // Сколько очков за набор и из чего набор состоит. Вынесено в
        // константы затем, что эти три числа связаны: они стоят и в подсчёте,
        // и в описании правила, которое игрок читает на баннере.
        private const int PointsPerSet = 12;
        private const int CoinsPerSet = 3;
        private const int SticksPerSet = 1;

        public REfB21(int id)
        {
            this._id = id;
        }
        private int _id;
        public int Id => _id;

        public string Description => "Получите 12 ПО за каждый ваш набор из 3 золотых монет и 1 волшебной палки.";

        /// <summary>
        /// Считает правило одному игроку. Ничего не меняет: начисляет очки
        /// тот, кто вызвал, — разбор в конце партии (<c>Room.ScoreFinalRule</c>).
        ///
        /// <para>Разделение неслучайно. Разбор должен успеть рассказать
        /// клиентам, что он насчитал, <b>прежде чем</b> начислит очки: иначе
        /// счёт на табличке вырастет раньше, чем предметы до неё долетят.</para>
        /// </summary>
        public RuleScoring Score(IGameContext context, Player player)
        {
            if (player == null) return RuleScoring.None;

            int sets = Math.Min(player.ItemAmount(ItemType.Coin) / CoinsPerSet,
                                player.ItemAmount(ItemType.MagicStick) / SticksPerSet);
            if (sets <= 0) return RuleScoring.None;

            // Перечисляем предметы поштучно: показу нужно подсветить ровно
            // столько монет и палок, сколько сыграло, а не «монеты вообще».
            var counted = new List<ItemType>();
            for (int i = 0; i < sets * CoinsPerSet; i++) counted.Add(ItemType.Coin);
            for (int i = 0; i < sets * SticksPerSet; i++) counted.Add(ItemType.MagicStick);

            return new RuleScoring(sets * PointsPerSet, counted);
        }

        /// <summary>
        /// Вход из <see cref="IRuleEffect"/> — тот же подсчёт для текущего
        /// игрока. Разбор конца партии сюда не ходит: ему нужны все игроки и
        /// разбор набора, поэтому он зовёт <see cref="Score"/> напрямую.
        /// </summary>
        public Task<bool> ApplyEffect(IGameContext context)
        {
            var manager = context.GameManager;
            var player = context.GameState.GetCurrentPlayer();

            var scoring = Score(context, player);
            if (scoring.Points <= 0)
            {
                manager.PrintMessage($"Игрок {player.Name} не собрал ни одного набора — правило конца игры ничего не даёт.");
                return Task.FromResult(false);
            }

            manager.AddPointsToPlayer(player, scoring.Points);
            return Task.FromResult(true);
        }

        public bool IsEffectAvailable(IGameContext context)
            => Score(context, context.GameState.GetCurrentPlayer()).Points > 0;
    }
}
