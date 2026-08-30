using ScaryTales.Enums;
using System.Collections.Generic;
using System.Linq;

namespace ScaryTales.Decisions
{
    public abstract class DecisionRequest { }

    public sealed class PickCardRequest : DecisionRequest
    {
        public IReadOnlyList<int> CandidateCardIds { get; }

        public PickCardRequest(IEnumerable<int> ids)
        {
            CandidateCardIds = ids.ToList();
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
