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
    public class PrincessEffect : ICardEffect
    {
        public CardEffectTimeType Type => CardEffectTimeType.Instant;

        public async Task ApplyEffect(IGameContext context)
        {
            var state = context.GameState;
            var board = context.GameBoard;
            var manager = context.GameManager;
            var player = state.GetCurrentPlayer();

            var men = board.GetCardsOnBoard(CardType.Man);
            if (!men.Any())
            {
                manager.PrintMessage("Нет ни одной карты типа 'Мужчина' на столе.");
                return;
            }

            var pick = await context.Router.PickCard(
                player.Id,
                new PickCardRequest(men.Select(c => c.Id)));
            var man = men.First(c => c.Id == pick.CardId);

            board.RemoveCardFromBoard(man);
            manager.PutCardInPlayerHand(man, player);
        }
    }
}
