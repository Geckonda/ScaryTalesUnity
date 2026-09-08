using ScaryTales.Abstractions;
using ScaryTales.Cards;
using ScaryTales.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ScaryTales.CardEffects
{
    /// <summary>
    /// Все игроки показывают руки; разыгравший получает по очку за каждое
    /// раскрытое место и берёт по карте за каждую раскрытую цыганку.
    /// </summary>
    public class GypsyEffect : ICardEffect
    {
        public CardEffectTimeType Type => CardEffectTimeType.Instant;

        public Task ApplyEffect(IGameContext context)
        {
            var manager = context.GameManager;
            var player = context.GameState.GetCurrentPlayer();

            // Списки снимаются ОДИН раз, до всякой выдачи карт. Взятая по этому
            // же эффекту цыганка не должна порождать ещё одно взятие: считаются
            // те, что были раскрыты, а не те, что появились из-за раскрытия.
            var hands = context.Players.SelectMany(p => p.Hand).ToList();
            var places = hands.Where(c => c.Type == CardType.Place).ToList();
            // По типу, а не по имени. MerchantEffect сравнивает строку "Купец",
            // и такое сравнение тихо ломается при переименовании карты —
            // компилятор о нём не знает.
            var gypsies = hands.Where(c => c is GypsyCard).ToList();

            manager.PrintMessage($"Цыганка раскрыла руки: мест — {places.Count}, цыганок — {gypsies.Count}.");

            // Раскрытие уходит ПЕРВЫМ, до очков и до взятия.
            //
            // События применяются у клиента по одному и только между
            // анимациями, поэтому порядок вызовов здесь и есть порядок того,
            // что игроки увидят. Обратный порядок показал бы результат раньше
            // причины.
            manager.RevealAllHands(player, revealed: true);

            // Каждое место выезжает, приносит своё очко и уезжает.
            foreach (var place in places)
            {
                manager.ShowcaseCard(place, stepOut: true);
                manager.AddPointsToPlayer(player, 1);
                manager.ShowcaseCard(place, stepOut: false);
            }

            // Каждая цыганка выезжает, приносит карту и уезжает. Взятие стоит
            // МЕЖДУ выездом и заездом намеренно: так видно, какая именно
            // цыганка дала эту карту.
            foreach (var gypsy in gypsies)
            {
                manager.ShowcaseCard(gypsy, stepOut: true);
                manager.DrawCard(player);
                manager.ShowcaseCard(gypsy, stepOut: false);
            }

            manager.RevealAllHands(player, revealed: false);
            return Task.CompletedTask;
        }
    }
}
