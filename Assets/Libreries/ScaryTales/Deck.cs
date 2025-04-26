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
