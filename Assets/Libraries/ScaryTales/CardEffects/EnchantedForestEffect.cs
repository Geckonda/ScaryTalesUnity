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
            var current = state.GetCurrentPlayer();
            var players = context.Players;

            // Взятие — по возможности: DrawCard сам сообщает о пустой колоде.
            //
            // Раньше здесь стояли два досрочных выхода по `CardsRemaining == 0`,
            // и второй из них убивал НОЧНУЮ половину эффекта. А ночью колода
            // не нужна вовсе: игроки сбрасывают из руки. На исходе партии,
            // когда колода как раз и пустеет, лес просто переставал работать —
            // ровно в тот момент, когда сброс всей руки решает исход.
            manager.DrawCard(current);

            if (!state.IsNight)
            {
                manager.PrintMessage("Все игроки вытягивают 1 карту из колоды");
                foreach (var p in players)
                    manager.DrawCard(p);
                return;
            }

            manager.PrintMessage("Все игроки сбрасывают 1 карту из своей руки");

            // Спрашиваем всех разом, но каждый сбрасывает СРАЗУ, как ответил,
            // не дожидаясь остальных.
            //
            // Раньше здесь стоял Task.WhenAll на все ответы, и только потом
            // общий цикл сброса. За столом это выглядело мёртвой паузой:
            // выбрал — и ничего не происходит, а кого ждут, непонятно.
            // Теперь уход карты в сброс сам показывает, кто уже определился,
            // а кто ещё думает.
            //
            // Опросы по-прежнему уходят одновременно: AskAndDiscard доходит
            // до первого await синхронно, так что цикл успевает разослать все
            // запросы, прежде чем начнёт ждать.
            var discards = new List<Task>();
            foreach (var p in players)
            {
                var hand = p.Hand.ToList();
                if (hand.Count == 0) continue;
                discards.Add(AskAndDiscard(context, p, hand));
                manager.PrintMessage($"Игрок {p.Name} выбирает карту для сброса.");
            }

            await Task.WhenAll(discards);
        }

        /// <summary>
        /// Спросить одного игрока и тут же сбросить выбранное.
        ///
        /// <para>Снимок руки сделан ДО ожидания ответа, а за это время рука
        /// могла измениться: игрок вышел посреди опроса, и его карты уже
        /// вернулись в колоду. Сбросить такую карту значило бы положить в
        /// сброс то, что лежит в колоде, — один экземпляр в двух зонах
        /// сразу.</para>
        ///
        /// <para>Проверка обязана быть здесь, а не у зовущего: эффект сам
        /// выбрал спрашивать всех разом, ему и отвечать за то, что мир между
        /// вопросом и ответом не стоял на месте.</para>
        /// </summary>
        private static async Task AskAndDiscard(IGameContext context, Player player, List<Card> hand)
        {
            var pick = await context.Router.PickCard(
                player.Id,
                new PickCardRequest(hand.Select(c => c.Id)));

            var card = hand.FirstOrDefault(c => c.Id == pick.CardId);
            if (card == null || !player.HasCard(card)) return;

            player.RemoveCardFromHand(card);
            context.GameManager.PutCardToDiscardPile(card);
        }
    }
}
