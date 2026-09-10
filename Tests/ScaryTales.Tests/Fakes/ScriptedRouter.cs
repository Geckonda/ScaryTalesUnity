using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ScaryTales.Decisions;
using ScaryTales.Enums;

namespace ScaryTales.Tests.Fakes
{
    /// <summary>
    /// Игрок, чьи ответы записаны заранее.
    ///
    /// <para>Эффекты не читают ввод — они <i>просят</i> решение через
    /// <see cref="IDecisionRouter"/> и ждут. Здесь этот роутер отвечает из
    /// очереди, поэтому эффект проходит целиком и синхронно.</para>
    ///
    /// <para>Роутер ещё и записывает, о чём его спросили. Это половина
    /// проверок: сплошь и рядом важно не только что эффект сделал, но и что он
    /// предложил на выбор — например, что Тайная помощница спрашивает
    /// <c>fromDeck</c>, а правило A3-2 разрешает отказ.</para>
    /// </summary>
    public class ScriptedRouter : IDecisionRouter
    {
        private readonly Queue<Func<DecisionRequest, DecisionResolution>> _answers = new();

        /// <summary>Все запросы по порядку — в них видно, что именно предложили игроку.</summary>
        public List<DecisionRequest> Requests { get; } = new();

        public List<PickCardRequest> CardRequests => Requests.OfType<PickCardRequest>().ToList();
        public List<PickItemRequest> ItemRequests => Requests.OfType<PickItemRequest>().ToList();

        /// <summary>Сколько раз у игрока вообще что-то спросили.</summary>
        public int AskCount => Requests.Count;

        // ---- Сценарий ----

        /// <summary>Выбрать карту с этим id.</summary>
        public ScriptedRouter PickCardId(int cardId) => Answer(_ => new CardPick(cardId));

        /// <summary>Выбрать первую предложенную карту, какой бы она ни была.</summary>
        public ScriptedRouter PickFirstCard() =>
            Answer(r => new CardPick(((PickCardRequest)r).CandidateCardIds.First()));

        /// <summary>Выбрать предложенную карту по её месту в списке кандидатов.</summary>
        public ScriptedRouter PickCardAt(int index) =>
            Answer(r => new CardPick(((PickCardRequest)r).CandidateCardIds[index]));

        public ScriptedRouter PickItemType(ItemType type) => Answer(_ => new ItemPick(type));

        public ScriptedRouter PickFirstItem() =>
            Answer(r => new ItemPick(((PickItemRequest)r).CandidateItemTypes.First()));

        public ScriptedRouter PickRuleEffect(int? id) => Answer(_ => new RuleEffectPick(id));

        public ScriptedRouter Confirm(bool confirmed = true) => Answer(_ => new ConfirmPick(confirmed));

        /// <summary>
        /// Игрок отказался. Прилетает эффекту как <see cref="OperationCanceledException"/>:
        /// эффект просто не доживает до своих последствий.
        /// </summary>
        public ScriptedRouter Decline() =>
            Answer(_ => throw new OperationCanceledException("игрок отказался от выбора"));

        /// <summary>
        /// Игрок вышел, не ответив. Отличается от отказа только намерением, но
        /// последствия для эффекта те же — и именно это надо проверять: всё,
        /// потраченное до вопроса, останется потраченным.
        /// </summary>
        public ScriptedRouter Abandon() =>
            Answer(_ => throw new OperationCanceledException("игрок вышел, не ответив"));

        /// <summary>
        /// Ответ, которого не было среди кандидатов, — так себя ведёт
        /// подделанный клиентом интент.
        /// </summary>
        public ScriptedRouter PickForgedCardId(int cardId = 999_999) => PickCardId(cardId);

        private ScriptedRouter Answer(Func<DecisionRequest, DecisionResolution> answer)
        {
            _answers.Enqueue(answer);
            return this;
        }

        // ---- IDecisionRouter ----

        public Task<CardPick> PickCard(int playerId, PickCardRequest request) => Next<CardPick>(request);
        public Task<ItemPick> PickItem(int playerId, PickItemRequest request) => Next<ItemPick>(request);
        public Task<RuleEffectPick> PickRuleEffect(int playerId, PickRuleEffectRequest request) => Next<RuleEffectPick>(request);
        public Task<ConfirmPick> Confirm(int playerId, ConfirmRequest request) => Next<ConfirmPick>(request);

        private Task<T> Next<T>(DecisionRequest request) where T : DecisionResolution
        {
            Requests.Add(request);

            if (_answers.Count == 0)
                throw new InvalidOperationException(
                    $"Эффект спросил {request.GetType().Name} ({Requests.Count}-й вопрос), " +
                    "а в сценарии ответы кончились. Либо эффект спрашивает лишнее, " +
                    "либо тест недосказан.");

            var answer = _answers.Dequeue()(request);

            if (!(answer is T typed))
                throw new InvalidOperationException(
                    $"На {request.GetType().Name} сценарий отвечает {answer.GetType().Name}. " +
                    "Порядок ответов разошёлся с порядком вопросов.");

            return Task.FromResult(typed);
        }
    }
}
