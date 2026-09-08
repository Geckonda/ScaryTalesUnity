using ScaryTales.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ScaryTales
{
    public class Deck
    {
        /// <summary>
        /// Основная колода
        /// </summary>
        private List<Card> _cards = new();
        /// <summary>
        /// Изначальное количество карт в колоде
        /// </summary>
        private readonly int _countDeckInitial;
        public int CountDeckInitial => _countDeckInitial;

        /// <summary>
        /// Конструктор для создания колоды по шаблонам
        /// </summary>
        /// <param name="templates">Шаблоны</param>
        public Deck(List<Card> templates)
        {
            int count = 1;
            foreach (var template in templates)
                for (int i = 0; i < template.CardCountInDeck; i++)
                {
                    var card = template.Clone();
                    card.Id = count++;
                    _cards.Add(card);
                }

            Shuffle();

            _countDeckInitial = _cards.Count;
        }

        /// <summary>
        /// Метод для тасовки колоды
        /// </summary>
        public void Shuffle()
        {
            var rnd = new Random();
            _cards = _cards.OrderBy(x => rnd.Next()).ToList();
        }

        /// <summary>
        /// Метод для тасовки калоды по Id
        /// </summary>
        /// <param name="ids"> Список Id, по которым нужн отсортировать</param>
        public void ShuffleById(List<int> ids)
        {
            if (_cards.Count != ids.Count)
                throw new ArgumentOutOfRangeException("Несоответсиве количества карт и количество Id");

            var cardDict = _cards.ToDictionary(card => card.Id);
            _cards = ids
                .Where(id => cardDict.ContainsKey(id)) // на случай если в ids есть Id, которых нет в колоде
                .Select(id => cardDict[id])
                .ToList();
        }

        /// <summary>
        /// Возвращает коллекцию идентификаторов карт, содержащихся в текущей колоде.
        /// </summary>
        /// <remarks>
        /// Идентификаторы возвращаются в порядке расположения карт внутри колоды на момент вызова метода.
        /// </remarks>
        /// <returns>Список идентификаторов карт <see cref="int"/>.</returns>
        public List<int> GetCardIds() => _cards.Select(x => x.Id).ToList();


        /// <summary>
        /// Показывает верх колоды, НЕ снимая карт.
        ///
        /// <para>Ради Тайной помощницы: «посмотрите 2 верхние карты» — это
        /// именно взгляд. Снять обе и вернуть лишнюю было бы хуже, потому что
        /// между вопросом и ответом игрок может выйти: снятые карты остались бы
        /// вне партии, а колода — короче, чем должна. Пока карты в колоде,
        /// брошенный вопрос не стоит ничего.</para>
        /// </summary>
        /// <param name="count">Сколько карт сверху. Меньше, если их столько нет.</param>
        /// <returns>Карты в порядке от вершины; пустой список у пустой колоды.</returns>
        public List<Card> PeekTop(int count)
        {
            if (count <= 0) return new List<Card>();
            return _cards.Take(count).ToList();
        }

        /// <summary>
        /// Снимает из колоды конкретную карту, где бы она в ней ни лежала.
        /// </summary>
        /// <returns>Карта или null, если такой в колоде нет.</returns>
        public Card? TakeCardById(int id)
        {
            var card = _cards.FirstOrDefault(x => x.Id == id);
            if (card != null)
                _cards.Remove(card);
            return card;
        }

        /// <summary>
        /// Метод для вытягивания карты из колоды
        /// </summary>
        /// <returns>Карта на вершине колоды</returns>
        public Card? DrawCard()
        {
            if (_cards.Count == 0) return null;

            var card = _cards[0];
            _cards.RemoveAt(0);
            return card;
        }


        /// <summary>
        /// Возвращает карты в колоду и перетасовывает её.
        ///
        /// <para>Нужно, когда игрок уходит посреди партии: его рука не должна
        /// пропадать из игры, потому что партия кончается ровно тогда, когда
        /// колода иссякла (см. серверный цикл хода). Тасуем, чтобы вернувшиеся
        /// карты не легли предсказуемой стопкой сверху — их состав видели все
        /// за столом.</para>
        /// </summary>
        /// <returns>Сколько карт вернулось.</returns>
        public int ReturnCardsAndShuffle(IEnumerable<Card> cards)
        {
            if (cards == null) return 0;

            int added = 0;
            foreach (var card in cards)
            {
                if (card == null) continue;
                _cards.Add(card);
                added++;
            }

            if (added > 0) Shuffle();
            return added;
        }

        /// <summary>
        /// Получение текущего состояния колоды (количество карт)
        /// </summary>
        public int CardsRemaining => _cards.Count;

        /// <summary>
        /// Получение конкретной карты по имени из колоды
        /// </summary>
        /// <param name="name">Название карты</param>
        /// <returns>Если такой карты нет, вернет null</returns>
        public Card? TakeCardByName(string name)
        {
            if (_cards.Count == 0) return null;

            var card = _cards.FirstOrDefault(x => x.Name == name);
            if (card != null)
                _cards.Remove(card);
            return card;
        }
    }
}
