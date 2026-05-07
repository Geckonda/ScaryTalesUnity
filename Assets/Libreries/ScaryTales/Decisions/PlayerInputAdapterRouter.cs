using Assets.Libreries.ScaryTales.Abstractions;
using ScaryTales.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ScaryTales.Decisions
{
    // Phase 1 adapter: implements IDecisionRouter by delegating to each
    // Player's IPlayerInput. Lets new router-based effect code coexist with
    // the legacy IPlayerInput call sites while the migration is in progress.
    // Replaced by NetworkDecisionRouter in Phase 3.
    public class PlayerInputAdapterRouter : IDecisionRouter
    {
        private readonly IReadOnlyList<Player> _players;
        private readonly IGameBoard _board;
        private readonly ItemManager _itemManager;
        private Func<int, IRuleEffect?>? _findRuleEffect;

        public PlayerInputAdapterRouter(
            IReadOnlyList<Player> players,
            IGameBoard board,
            ItemManager itemManager,
            Func<int, IRuleEffect?>? findRuleEffect = null)
        {
            _players = players;
            _board = board;
            _itemManager = itemManager;
            _findRuleEffect = findRuleEffect;
        }

        // Late-binds the rule-effect lookup. The current rule lives on the
        // Unity layer (UnGameManager.CurrentRuleInGame) and is not known when
        // GameBuilder constructs the router; this lets Unity wire it after init.
        public void SetRuleEffectLookup(Func<int, IRuleEffect?> lookup)
        {
            _findRuleEffect = lookup;
        }

        public async Task<CardPick> PickCard(int playerId, PickCardRequest request)
        {
            var player = RequirePlayer(playerId);
            var candidates = ResolveCardIds(request.CandidateCardIds);
            var picked = await player.PlayerInput.SelectCard(candidates);
            return new CardPick(picked.Id);
        }

        public async Task<ItemPick> PickItem(int playerId, PickItemRequest request)
        {
            var player = RequirePlayer(playerId);
            var candidates = request.CandidateItemTypes
                .Select(t => _itemManager.GetCloneItemByType(t))
                .Where(i => i != null)
                .ToList();
            var picked = await player.PlayerInput.SelectItem(candidates);
            return new ItemPick(picked.Type);
        }

        public async Task<RuleEffectPick> PickRuleEffect(int playerId, PickRuleEffectRequest request)
        {
            if (_findRuleEffect == null)
                throw new InvalidOperationException(
                    "PickRuleEffect requires a findRuleEffect delegate. Call SetRuleEffectLookup first.");

            var player = RequirePlayer(playerId);
            var candidates = request.CandidateRuleEffectIds
                .Select(id => _findRuleEffect(id))
                .Where(e => e != null)
                .ToList();
            var picked = await player.PlayerInput.SelectRuleEffect(candidates);
            // null = the player skipped; preserve the legacy semantic.
            return new RuleEffectPick(picked?.Id);
        }

        public async Task<ConfirmPick> Confirm(int playerId, ConfirmRequest request)
        {
            var player = RequirePlayer(playerId);
            var yes = await player.PlayerInput.YesOrNo();
            return new ConfirmPick(yes);
        }

        private Player RequirePlayer(int id)
        {
            var p = _players.FirstOrDefault(x => x.Id == id);
            if (p == null) throw new InvalidOperationException($"No player with id {id}");
            if (p.PlayerInput == null) throw new InvalidOperationException($"Player {id} has no IPlayerInput");
            return p;
        }

        private List<Card> ResolveCardIds(IEnumerable<int> ids)
        {
            var reachable = new List<Card>();
            reachable.AddRange(_board.GetCardsOnBoard());
            reachable.AddRange(_board.GetCardsFromDiscardPile());
            var tod = _board.GetCardFromTimeOfDaySlot();
            if (tod != null) reachable.Add(tod);
            foreach (var p in _players)
                reachable.AddRange(p.Hand);

            var byId = reachable
                .GroupBy(c => c.Id)
                .ToDictionary(g => g.Key, g => g.First());

            var result = new List<Card>();
            foreach (var id in ids)
            {
                if (byId.TryGetValue(id, out var card))
                    result.Add(card);
            }
            return result;
        }
    }
}
