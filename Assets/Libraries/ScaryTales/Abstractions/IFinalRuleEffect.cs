using ScaryTales;
using ScaryTales.Abstractions;
using ScaryTales.Enums;
using System.Collections.Generic;

namespace Assets.Libraries.ScaryTales.Abstractions
{
    /// <summary>
    /// Итог правила для одного игрока: сколько очков и какие предметы за это
    /// сыграли.
    ///
    /// <para>Разбор нужен показу: в конце партии предметы игрока выкладываются
    /// на стол, и засчитанные подсвечиваются. Без него анимация могла бы
    /// показать только число и не смогла бы объяснить, откуда оно.</para>
    ///
    /// <para><see cref="CountedItems"/> — типы <b>с повторами</b>: набор из
    /// трёх монет и палки приходит как [Coin, Coin, Coin, MagicStick]. Именно
    /// типы, а не конкретные предметы: монеты взаимозаменяемы, а
    /// идентификаторы предметов на сервере и на клиенте расходятся после
    /// первого же снятия предмета (см. <c>ClientGameView.HandleItemRemoved</c>).
    /// </para>
    /// </summary>
    public readonly struct RuleScoring
    {
        public int Points { get; }
        public IReadOnlyList<ItemType> CountedItems { get; }

        public RuleScoring(int points, IReadOnlyList<ItemType> countedItems)
        {
            Points = points;
            CountedItems = countedItems ?? new List<ItemType>();
        }

        /// <summary>Правило игроку ничего не дало.</summary>
        public static RuleScoring None => new RuleScoring(0, null);
    }

    /// <summary>
    /// Правило, которое считается в конце партии — каждому игроку, без вопросов.
    ///
    /// <para><b>Почему отдельно от <see cref="IRuleEffect"/>.</b> Игровые
    /// правила интерактивны: они ждут ответа через <c>Router</c>, относятся к
    /// тому, кто ходит, и отвечают «состоялось или нет». Правило конца партии —
    /// чистый счёт сразу для всех, и ему нужен не тот вопрос и не тот ответ.
    /// Сведение их в один интерфейс заставило бы каждую сторону притворяться
    /// другой.</para>
    ///
    /// <para><see cref="Score"/> синхронный <b>намеренно</b>: так конструкцией
    /// запрещено задавать вопросы в конце партии. Разбор, в котором игроков о
    /// чём-то спрашивают, — совсем другая задача: очередь, отказы, выход
    /// игрока посреди ответа. Когда она понадобится, отсутствие <c>Task</c>
    /// здесь заставит её увидеть, а не проглядеть.</para>
    /// </summary>
    public interface IFinalRuleEffect : IRuleEffect
    {
        RuleScoring Score(IGameContext context, Player player);
    }
}
