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
                // Здесь стоял await AnimationManager.WaitForAllAnimations() —
                // наследие тех времён, когда движок крутился на каждом клиенте
                // и эффект мог дождаться локальной анимации. Теперь этот код
                // исполняется на сервере, где анимаций нет вообще, так что
                // вызов был пустым, а ядро зря тянулось к Unity-компоненту.
                // Порядок «вытянул — разыграл» на экране обеспечивает очередь
                // событий клиента.
                await manager.PlayCard(card);
            }
            return;
        }
    }
}
