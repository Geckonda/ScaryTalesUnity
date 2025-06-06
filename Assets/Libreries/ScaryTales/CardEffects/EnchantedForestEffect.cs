using ScaryTales.Abstractions;
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

                // Запускаем выбор карт у обоих игроков одновременно
                var localPlayerTask = localPlayer.SelectCardAmongOthers(localPlayer.Hand);
                var localOpponentTask = localOpponent.SelectCardAmongOthers(localOpponent.Hand);

                // Показываем параллельные сообщения (если нужно)
                manager.PrintMessage($"Игрок {localPlayer.Name} выбирает карту для сброса.");
                manager.PrintMessage($"Игрок {localOpponent.Name} выбирает карту для сброса.");

                // Дожидаемся, когда оба выберут
                var cards = await Task.WhenAll(localPlayerTask, localOpponentTask);

                // Обрабатываем результат
                localPlayer.RemoveCardFromHand(cards[0]);
                manager.PutCardToDiscardPile(cards[0]);

                localOpponent.RemoveCardFromHand(cards[1]);
                manager.PutCardToDiscardPile(cards[1]);
            }
        }
    }
}
