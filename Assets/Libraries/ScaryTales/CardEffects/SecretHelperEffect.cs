using ScaryTales.Abstractions;
using ScaryTales.Decisions;
using ScaryTales.Enums;
using System.Linq;
using System.Threading.Tasks;

namespace ScaryTales.CardEffects
{
    /// <summary>
    /// Игрок смотрит 2 верхние карты колоды, одну забирает на руку, вторую
    /// удаляет из игры. И получает право разыграть ещё одну карту в этом ходу.
    /// </summary>
    public class SecretHelperEffect : ICardEffect
    {
        public CardEffectTimeType Type => CardEffectTimeType.Instant;

        public async Task ApplyEffect(IGameContext context)
        {
            var manager = context.GameManager;
            var player = context.GameState.GetCurrentPlayer();

            // Дополнительный ход выдаётся первым и безусловно: карта обещает
            // его словом «можно», а не в обмен на удачный просмотр. Пустая
            // колода отменяет просмотр, но не подарок.
            manager.GrantExtraPlay();

            // Смотрим, не снимая: пока карты в колоде, брошенный вопрос ничего
            // не стоит. Сними мы их заранее — вышедший посреди выбора игрок
            // унёс бы обе с собой (см. Deck.PeekTop).
            var top = context.Deck.PeekTop(2);

            if (top.Count == 0)
            {
                manager.PrintMessage("В колоде не осталось карт — смотреть нечего.");
                return;
            }

            if (top.Count == 1)
            {
                // Выбирать не из чего, удалять нечего. Спрашивать здесь значило
                // бы открыть окно с одной кнопкой.
                manager.PrintMessage($"Игрок {player.Name} забирает последнюю карту колоды.");
                manager.DrawCard(player);
                return;
            }

            // Названий карт в сообщении нет намеренно: их видит только тот, кто
            // смотрит, а журнал читают все.
            manager.PrintMessage($"Игрок {player.Name} смотрит 2 верхние карты колоды.");

            // Показ идёт ДО вопроса: пока смотрящий выбирает в своём окне,
            // остальные за столом должны видеть, из-за чего пауза. Две рубашки
            // над колодой — вся причина; какие за ними карты, знать никому не
            // нужно, поэтому уходит только их число.
            manager.ShowPeekedCards(player, top.Count);

            var pick = await context.Router.PickCard(
                player.Id,
                new PickCardRequest(top.Select(c => c.Id), fromDeck: true));

            // Ответу верим не на слово: он приходит по сети, а за ушедшего
            // игрока его вообще подставляет роутер. Что бы ни пришло, выбор
            // обязан попасть в одну из двух показанных карт.
            var kept = top.FirstOrDefault(c => c.Id == pick.CardId) ?? top[0];
            var removed = top.First(c => c.Id != kept.Id);

            // Сообщение ПЕРЕД обоими действиями: оно объясняет то, что игроки
            // сейчас увидят, — как одна рубашка растворяется, а вторая уходит
            // в руку. Какие это были карты, по-прежнему не говорим.
            manager.PrintMessage($"Игрок {player.Name} забирает одну из них, вторая покидает игру.");

            // Удаление ИДЁТ ПЕРВЫМ, и это не только про порядок показа: снятая
            // лишняя карта оставляет выбранную на вершине колоды, откуда её и
            // берёт обычное взятие — со своим событием, анимацией и пересчётом
            // колоды. Отдельный путь «положить карту в руку из ниоткуда» здесь
            // не нужен.
            manager.RemoveCardFromGame(removed);
            manager.DrawCard(player);
        }
    }
}
