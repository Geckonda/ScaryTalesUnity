using ScaryTales.Enums;

namespace ScaryTales.Decisions
{
    public abstract class DecisionResolution { }

    public sealed class CardPick : DecisionResolution
    {
        public int CardId { get; }
        public CardPick(int cardId) { CardId = cardId; }
    }

    public sealed class ItemPick : DecisionResolution
    {
        public ItemType ItemType { get; }
        public ItemPick(ItemType itemType) { ItemType = itemType; }
    }

    public sealed class RuleEffectPick : DecisionResolution
    {
        // null = the player skipped rule selection
        public int? RuleEffectId { get; }
        public RuleEffectPick(int? ruleEffectId) { RuleEffectId = ruleEffectId; }
    }

    public sealed class ConfirmPick : DecisionResolution
    {
        public bool Confirmed { get; }
        public ConfirmPick(bool confirmed) { Confirmed = confirmed; }
    }
}
