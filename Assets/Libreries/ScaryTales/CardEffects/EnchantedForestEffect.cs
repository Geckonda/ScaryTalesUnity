using ScaryTales.Abstractions;
using ScaryTales.Decisions;
using ScaryTales.Enums;
using ScaryTales.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ScaryTales.CardEffects
{
    public class EnchantedForestEffect : ICardEffect
    {
        public CardEffectTimeType Type => CardEffectTimeType.Instant;

        // NOTE: 2-player concept (LocalPlayer + LocalOpponent). Phase 4 will
        // generalize this to "every player picks a card" by iterating context.Players.
        public async Task ApplyEffect(IGameContext context)
        {
            var state = context.GameState;
            var manager = context.GameManager;
            var localPlayer = manager.LocalPlayer;
            var localOpponent = manager.LocalOpponent;
            var deck = context.Deck;

            if(deck.CardsRemaining == 0)
            {
                manager.PrintMessage("В колоде не осталось карт.");
                return;
            }
            manager.DrawCard(localPlayer);

            if (deck.CardsRemaining == 0)
            {
                manager.PrintMessage("В колоде не осталось карт.");
                return;
            }

            if (!state.IsNight)
            {
                manager.PrintMessage("Все игроки вытягивают 1 карту из колоды");
                manager.DrawCard(localPlayer);
                manager.DrawCard(localOpponent);
            }
            else
            {
                manager.PrintMessage("Все игроки сбрасывают 1 карту из своей руки");

                var localHandSnapshot = localPlayer.Hand.ToList();
                var opponentHandSnapshot = localOpponent.Hand.ToList();

                var localPickTask = context.Router.PickCard(
                    localPlayer.Id,
                    new PickCardRequest(localHandSnapshot.Select(c => c.Id)));
                var opponentPickTask = context.Router.PickCard(
                    localOpponent.Id,
                    new PickCardRequest(opponentHandSnapshot.Select(c => c.Id)));

                manager.PrintMessage($"Игрок {localPlayer.Name} выбирает карту для сброса.");
                manager.PrintMessage($"Игрок {localOpponent.Name} выбирает карту для сброса.");

                var picks = await Task.WhenAll(localPickTask, opponentPickTask);

                var localCard = localHandSnapshot.First(c => c.Id == picks[0].CardId);
                var opponentCard = opponentHandSnapshot.First(c => c.Id == picks[1].CardId);

                localPlayer.RemoveCardFromHand(localCard);
                manager.PutCardToDiscardPile(localCard);

                localOpponent.RemoveCardFromHand(opponentCard);
                manager.PutCardToDiscardPile(opponentCard);
            }
        }
    }
}
