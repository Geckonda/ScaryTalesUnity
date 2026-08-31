using ScaryTales.Enums;
using System.Collections.Generic;
using System.Linq;

namespace ScaryTales.Decisions
{
    public abstract class DecisionRequest { }

    public sealed class PickCardRequest : DecisionRequest
    {
        public IReadOnlyList<int> CandidateCardIds { get; }

        /// <summary>
        /// Можно ли отказаться от выбора.
        ///
        /// <para>По умолчанию нельзя, и это важное умолчание: у эффекта
        /// разыгранной карты выбор обязателен — карта уже на столе и очки за
        /// неё начислены, так что «передумал» означало бы бесплатную отмену
        /// половины хода. Отказ разрешают только там, где к моменту вопроса
        /// ещё ничего не потрачено, — сегодня это правило A1-2, которое
        /// специально спрашивает ДО того, как забрать меч.</para>
        ///
        /// <para>Отказ прилетает эффекту как <c>OperationCanceledException</c>:
        /// эффект просто не доживает до своих последствий, и знать о нём ему
        /// не нужно.</para>
        /// </summary>
        public bool CanCancel { get; }

        public PickCardRequest(IEnumerable<int> ids, bool canCancel = false)
        {
            CandidateCardIds = ids.ToList();
            CanCancel = canCancel;
        }
    }

    public sealed class PickItemRequest : DecisionRequest
    {
        public IReadOnlyList<ItemType> CandidateItemTypes { get; }

        public PickItemRequest(IEnumerable<ItemType> types)
        {
            CandidateItemTypes = types.ToList();
        }
    }

    public sealed class PickRuleEffectRequest : DecisionRequest
    {
        public IReadOnlyList<int> CandidateRuleEffectIds { get; }

        public PickRuleEffectRequest(IEnumerable<int> ids)
        {
            CandidateRuleEffectIds = ids.ToList();
        }
    }

    public sealed class ConfirmRequest : DecisionRequest
    {
        public string? Prompt { get; }

        public ConfirmRequest(string? prompt = null)
        {
            Prompt = prompt;
        }
    }
}
