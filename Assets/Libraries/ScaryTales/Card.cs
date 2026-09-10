using ScaryTales.Abstractions;
using ScaryTales.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace ScaryTales
{
    public abstract class Card
    {
        public int Id { get; set; }
        public abstract string Name { get; }
        public abstract CardType Type { get; }
        public abstract int Points { get; }
        public abstract string EffectDescription { get; }
        /// <summary>
        /// Позиция карты в реальном времени
        /// </summary>
        public CardPosition Position { get; set; } = CardPosition.InDeck;

        /// <summary>
        /// Позиция карты после разыгрывания
        /// </summary>
        public abstract CardPosition PositionAfterPlay { get; }
        /// <summary>
        /// Текущий владелец карты
        /// </summary>
        public Player? Owner { get; set; }
        /// <summary>
        /// Сколько такой карты в колоде
        /// </summary>
        public abstract int CardCountInDeck { get; }

        public abstract ICardEffect Effect { get; }

        // ---- Постоянные правила ----
        //
        // Всё, что ниже, действует, пока карта лежит на столе перед игроком, и
        // потому не выражается через ICardEffect: тот умеет только «сделать
        // что-то один раз», а здесь правила, которые надо СПРАШИВАТЬ — в начале
        // хода и в момент взятия карты.
        //
        // По умолчанию карта не накладывает ничего, так что двадцать
        // существующих карт эти члены не касаются вовсе.

        /// <summary>
        /// Запрещает ли эта карта своему владельцу брать карты из колоды.
        ///
        /// <para>Спрашивается в <c>GameManager.DrawCard</c> — единственной
        /// точке, через которую карты попадают из колоды в руку. Поэтому
        /// запрет накрывает и обязательное взятие в начале хода, и взятия от
        /// эффектов карт и правил партии. Сброс запретом не затронут: там
        /// карты берутся не из колоды.</para>
        /// </summary>
        public virtual bool BlocksDeckDraw => false;

        /// <summary>
        /// Сколько дополнительных карт владелец может разыграть за свой ход,
        /// пока эта карта перед ним.
        ///
        /// <para>Выдаётся заново каждый ход, потому что непотраченное сгорает
        /// вместе с ходом (<c>ClearExtraPlays</c>). В ход, когда карту
        /// выложили, право выдаёт <c>GameManager.PlayCard</c> сразу по
        /// приземлении, в последующие — <c>ApplyStartOfTurnBoardRules</c> в
        /// начале хода. Ровно один раз за ход в обоих случаях.</para>
        /// </summary>
        public virtual int ExtraPlaysPerTurn => 0;

        /// <summary>
        /// Пора ли сбросить эту карту в начале хода её владельца.
        ///
        /// <para>Спрашивается до обязательного взятия — карта, которая уходит
        /// по этому условию, не должна мешать взять в тот же ход.</para>
        /// </summary>
        public virtual bool ShouldDiscardAtTurnStart(IGameContext context) => false;

        public abstract Task ActivateEffect(IGameContext context);

        public abstract Card Clone();

        public override string ToString()
        {
            return $"{this.Id} | {this.Name} | ow:{this.Owner.Name} | pos: {this.Position}"; 
        }
    }
}
