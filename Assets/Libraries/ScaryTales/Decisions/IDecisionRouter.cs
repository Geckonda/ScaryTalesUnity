using System.Threading.Tasks;

namespace ScaryTales.Decisions
{
    public interface IDecisionRouter
    {
        Task<CardPick> PickCard(int playerId, PickCardRequest request);
        Task<ItemPick> PickItem(int playerId, PickItemRequest request);
        Task<RuleEffectPick> PickRuleEffect(int playerId, PickRuleEffectRequest request);
        Task<ConfirmPick> Confirm(int playerId, ConfirmRequest request);
    }
}
