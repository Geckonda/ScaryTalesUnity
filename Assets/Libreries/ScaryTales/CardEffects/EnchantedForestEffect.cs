using ScaryTales.Abstractions;
using ScaryTales.Decisions;
using ScaryTales.Enums;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ScaryTales.CardEffects
{
    /// <summary>
    /// "Draw 1 card. If Day, all players draw 1 card. If Night, all players
    /// discard 1 card." (per docs/CARDS.md). Active player gets the initial
    /// "draw 1" plus, on Day, an extra one as part of "all players draw".
    /// </summary>
    public class EnchantedForestEffect : ICardEffect
    {
        public CardEffectTimeType Type => CardEffectTimeType.Instant;

        public async Task ApplyEffect(IGameContext context)
        {
            var state = context.GameState;
            var manager = context.GameManager;
            var deck = context.Deck;
            var current = state.GetCurrentPlayer();
            var players = context.Players;

            if (deck.CardsRemaining == 0)
            {
                manager.PrintMessage("В колоде не осталось карт.");
                return;
            }
            manager.DrawCard(current);

            if (deck.CardsRemaining == 0)
            {
                manager.PrintMessage("В колоде не осталось карт.");
                return;
            }

            if (!state.IsNight)
            {
                manager.PrintMessage("Все игроки вытягивают 1 карту из колоды");
                foreach (var p in players)
                    manager.DrawCard(p);
                return;
            }

            manager.PrintMessage("Все игроки сбрасывают 1 карту из своей руки");

            // Snapshot each player's hand and request picks concurrently.
            // Players with empty hands are skipped — nothing to discard.
            var snapshots = new List<(Player player, List<Card> hand)>();
            var pickTasks = new List<Task<CardPick>>();
            foreach (var p in players)
            {
                var hand = p.Hand.ToList();
                if (hand.Count == 0) continue;
                snapshots.Add((p, hand));
                pickTasks.Add(context.Router.PickCard(
                    p.Id,
                    new PickCardRequest(hand.Select(c => c.Id))));
                manager.PrintMessage($"Игрок {p.Name} выбирает карту для сброса.");
            }

            var picks = await Task.WhenAll(pickTasks);

            for (int i = 0; i < snapshots.Count; i++)
            {
                var (player, hand) = snapshots[i];
                var pick = picks[i];
                var card = hand.FirstOrDefault(c => c.Id == pick.CardId);
                if (card == null) continue;
                player.RemoveCardFromHand(card);
                manager.PutCardToDiscardPile(card);
            }
        }
    }
}
