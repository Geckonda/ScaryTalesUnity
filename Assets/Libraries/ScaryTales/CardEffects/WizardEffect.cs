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

                // В руку — молча, без события, и это намеренно.
                //
                // Волшебник карту в руку не БЕРЁТ: он её раскрывает и тут же
                // разыгрывает. Рука здесь — технический перевалочный пункт,
                // потому что PlayCard требует, чтобы карта была у игрока.
                // Игрок видит ровно то, что происходит: карта вылетает из
                // колоды на стол.
                //
                // 2026-08-31 это переделали на PutCardInPlayerHand ради
                // консистентности зеркала — и получили заметную регрессию:
                // клиент честно анимировал полёт в руку, следом приходил
                // полёт на стол, и два твина на одном трансформе тянули карту
                // в разные стороны. Она зависала между рукой и столом, а её
                // положение приходилось ждать до следующей перерисовки.
                // Откатано: несуществующий в игре шаг нельзя показывать ради
                // стройности событий.
                //
                // Расхождение, которое та правка лечила, остаётся, но оно
                // безобидно: карту клиент всё равно увидит следующим же
                // событием «на стол», а в DeckOrder она числится лишь как
                // порядок рубашек, который ничем не отображается.
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
