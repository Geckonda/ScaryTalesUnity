using ScaryTales.Abstractions;
using ScaryTales.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ScaryTales.CardEffects
{
    public class WizardEffect : ICardEffect
    {
        public CardEffectTimeType Type => CardEffectTimeType.Instant;

        public async Task ApplyEffect(IGameContext context)
        {
            var manager = context.GameManager;
            var player = context.GameState.GetCurrentPlayer();

            var card = manager.TryDrawCardFromDeck();
            if(card != null)
            {
                manager.PrintMessage($"Игрок {player.Name} вытянул карту {card.Name} и тут же разыграл.");
                player.AddCardToHand(card);
                card.Position = CardPosition.InHand;
                card.Owner = player;
                await AnimationManager.Instance.WaitForAllAnimations();
                await manager.PlayCard(card);
            }
            return;
        }
    }
}
