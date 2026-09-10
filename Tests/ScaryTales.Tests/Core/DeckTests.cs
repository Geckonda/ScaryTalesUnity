using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ScaryTales;
using ScaryTales.Tests.Fakes;

namespace ScaryTales.Tests.Core
{
    [TestFixture]
    public class DeckTests
    {
        /// <summary>Колода из карт с предсказуемыми именами: «К1», «К2», …</summary>
        private static Deck MakeDeck(int size)
        {
            var templates = Enumerable.Range(1, size)
                .Select(i => (Card)new TestCard(name: $"К{i}"))
                .ToList();
            return new Deck(templates);
        }

        /// <summary>Кладёт наверх карты с указанными id, порядок остальных не важен.</summary>
        private static void PutOnTop(Deck deck, params int[] ids)
        {
            var rest = deck.GetCardIds().Where(id => !ids.Contains(id));
            deck.ShuffleById(ids.Concat(rest).ToList());
        }

        // ---- Заведение колоды ----

        [Test]
        public void Шаблон_разворачивается_в_нужное_число_экземпляров()
        {
            var deck = new Deck(new List<Card> { new TestCard(countInDeck: 3) });

            Assert.That(deck.CardsRemaining, Is.EqualTo(3));
            Assert.That(deck.CountDeckInitial, Is.EqualTo(3));
        }

        [Test]
        public void Пустой_список_шаблонов_даёт_пустую_колоду()
        {
            var deck = new Deck(new List<Card>());

            Assert.That(deck.CardsRemaining, Is.Zero);
            Assert.That(deck.GetCardIds(), Is.Empty);
        }

        [Test]
        public void Экземпляры_независимы_друг_от_друга()
        {
            // Клонирование шаблона, а не переиспользование: иначе три «Ночи» в
            // колоде были бы одним объектом, и владелец одной стал бы
            // владельцем всех.
            var deck = new Deck(new List<Card> { new TestCard(countInDeck: 2) });
            var first = deck.DrawCard();
            var second = deck.DrawCard();

            Assert.That(first, Is.Not.SameAs(second));
            Assert.That(first.Id, Is.Not.EqualTo(second.Id));
        }

        // ---- Взятие ----

        [Test]
        public void Взятие_снимает_карту_с_вершины()
        {
            var deck = MakeDeck(5);
            PutOnTop(deck, 4);

            var card = deck.DrawCard();

            Assert.That(card.Id, Is.EqualTo(4));
            Assert.That(deck.CardsRemaining, Is.EqualTo(4));
            Assert.That(deck.GetCardIds(), Does.Not.Contain(4));
        }

        [Test]
        public void Взятие_из_пустой_колоды_возвращает_null_а_не_бросает()
        {
            // На этом стоит условие конца партии: цикл хода зовёт взятие
            // безусловно и полагается на тихий отказ.
            var deck = new Deck(new List<Card>());

            Assert.That(deck.DrawCard(), Is.Null);
        }

        [Test]
        public void Колода_вычерпывается_до_конца_без_потерь()
        {
            var deck = MakeDeck(7);
            var drawn = new List<int>();

            Card card;
            while ((card = deck.DrawCard()) != null) drawn.Add(card.Id);

            Assert.That(drawn, Is.EquivalentTo(Enumerable.Range(1, 7)));
            Assert.That(deck.CardsRemaining, Is.Zero);
        }

        [Test]
        public void Изначальный_размер_не_меняется_при_взятии()
        {
            var deck = MakeDeck(5);
            deck.DrawCard();
            deck.DrawCard();

            Assert.That(deck.CardsRemaining, Is.EqualTo(3));
            Assert.That(deck.CountDeckInitial, Is.EqualTo(5), "он про начало партии, а не про сейчас");
        }

        // ---- Подсматривание ----

        [Test]
        public void Подсматривание_не_снимает_карт()
        {
            // Ради Тайной помощницы: пока карты в колоде, брошенный вопрос
            // ничего не стоит. Сними мы их — вышедший игрок унёс бы обе из игры.
            var deck = MakeDeck(5);
            PutOnTop(deck, 2, 3);

            var top = deck.PeekTop(2);

            Assert.That(top.Select(c => c.Id), Is.EqualTo(new[] { 2, 3 }));
            Assert.That(deck.CardsRemaining, Is.EqualTo(5));
            Assert.That(deck.PeekTop(2).Select(c => c.Id), Is.EqualTo(new[] { 2, 3 }), "повторный взгляд даёт то же");
        }

        [Test]
        public void Подсматривание_отдаёт_сколько_есть_а_не_сколько_просят()
        {
            var deck = MakeDeck(2);

            Assert.That(deck.PeekTop(5).Count, Is.EqualTo(2));
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void Подсматривание_неположительного_числа_даёт_пустой_список(int count)
        {
            Assert.That(MakeDeck(3).PeekTop(count), Is.Empty);
        }

        [Test]
        public void Подсматривание_в_пустой_колоде_даёт_пустой_список()
        {
            Assert.That(new Deck(new List<Card>()).PeekTop(2), Is.Empty);
        }

        [Test]
        public void Подсмотренная_карта_берётся_следующей()
        {
            // Связка, на которой держится Тайная помощница: посмотрели две,
            // лишнюю убрали из игры, а победившую забирает обычное взятие.
            var deck = MakeDeck(5);
            PutOnTop(deck, 3);

            var peeked = deck.PeekTop(1).Single();

            Assert.That(deck.DrawCard().Id, Is.EqualTo(peeked.Id));
        }

        // ---- Снятие конкретной карты ----

        [Test]
        public void Снятие_по_id_достаёт_карту_из_середины()
        {
            var deck = MakeDeck(5);

            var card = deck.TakeCardById(3);

            Assert.That(card.Id, Is.EqualTo(3));
            Assert.That(deck.CardsRemaining, Is.EqualTo(4));
            Assert.That(deck.GetCardIds(), Does.Not.Contain(3));
        }

        [Test]
        public void Снятие_по_отсутствующему_id_возвращает_null()
        {
            var deck = MakeDeck(3);

            Assert.That(deck.TakeCardById(99), Is.Null);
            Assert.That(deck.CardsRemaining, Is.EqualTo(3));
        }

        [Test]
        public void Снятие_по_имени_достаёт_карту()
        {
            // Так цикл хода достаёт «Ночь» на старте партии.
            var deck = MakeDeck(4);

            var card = deck.TakeCardByName("К2");

            Assert.That(card, Is.Not.Null);
            Assert.That(card.Name, Is.EqualTo("К2"));
            Assert.That(deck.CardsRemaining, Is.EqualTo(3));
        }

        [Test]
        public void Снятие_по_имени_берёт_ровно_один_экземпляр()
        {
            var deck = new Deck(new List<Card> { new TestCard(name: "Ночь", countInDeck: 4) });

            deck.TakeCardByName("Ночь");

            Assert.That(deck.CardsRemaining, Is.EqualTo(3));
        }

        [Test]
        public void Снятие_по_отсутствующему_имени_возвращает_null()
        {
            var deck = MakeDeck(3);

            Assert.That(deck.TakeCardByName("Кракен"), Is.Null);
            Assert.That(deck.CardsRemaining, Is.EqualTo(3));
        }

        [Test]
        public void Снятие_по_имени_из_пустой_колоды_возвращает_null()
        {
            Assert.That(new Deck(new List<Card>()).TakeCardByName("Ночь"), Is.Null);
        }

        // ---- Тасовка по списку id ----

        [Test]
        public void Тасовка_по_id_задаёт_точный_порядок()
        {
            // Это и есть способ разложить колоду в тесте: сеять Random в
            // проде не требуется, порядок задаётся снаружи.
            var deck = MakeDeck(4);

            deck.ShuffleById(new List<int> { 4, 3, 2, 1 });

            Assert.That(deck.GetCardIds(), Is.EqualTo(new[] { 4, 3, 2, 1 }));
        }

        [Test]
        public void Круговой_прогон_через_список_id_сохраняет_колоду()
        {
            var deck = MakeDeck(6);
            var before = deck.GetCardIds();

            deck.ShuffleById(before);

            Assert.That(deck.GetCardIds(), Is.EqualTo(before));
        }

        [Test]
        public void Тасовка_по_списку_другой_длины_бросает()
        {
            // Сервер шлёт порядок колоды клиентам; расхождение длин означает
            // разные версии каталога, и падать здесь честнее, чем молча
            // потерять карты.
            var deck = MakeDeck(4);

            Assert.Throws<ArgumentOutOfRangeException>(() => deck.ShuffleById(new List<int> { 1, 2 }));
        }

        [Test]
        public void Список_id_отдаётся_в_порядке_колоды()
        {
            var deck = MakeDeck(3);
            deck.ShuffleById(new List<int> { 2, 1, 3 });

            Assert.That(deck.GetCardIds(), Is.EqualTo(new[] { 2, 1, 3 }));
            Assert.That(deck.PeekTop(3).Select(c => c.Id), Is.EqualTo(new[] { 2, 1, 3 }));
        }

        // ---- Возврат карт ----

        [Test]
        public void Возврат_карт_увеличивает_колоду_и_отдаёт_счётчик()
        {
            // Нужен, когда игрок уходит посреди партии: его рука не должна
            // пропасть, иначе партия кончится раньше времени.
            var deck = MakeDeck(5);
            var hand = new List<Card> { deck.DrawCard(), deck.DrawCard() };

            var returned = deck.ReturnCardsAndShuffle(hand);

            Assert.That(returned, Is.EqualTo(2));
            Assert.That(deck.CardsRemaining, Is.EqualTo(5));
            Assert.That(deck.GetCardIds(), Is.EquivalentTo(Enumerable.Range(1, 5)));
        }

        [Test]
        public void Возврат_пропускает_null_и_не_считает_их()
        {
            var deck = MakeDeck(3);
            var card = deck.DrawCard();

            var returned = deck.ReturnCardsAndShuffle(new List<Card> { card, null, null });

            Assert.That(returned, Is.EqualTo(1));
            Assert.That(deck.CardsRemaining, Is.EqualTo(3));
        }

        [Test]
        public void Возврат_null_коллекции_ничего_не_делает()
        {
            var deck = MakeDeck(3);

            Assert.That(deck.ReturnCardsAndShuffle(null), Is.Zero);
            Assert.That(deck.CardsRemaining, Is.EqualTo(3));
        }

        [Test]
        public void Возврат_пустой_коллекции_ничего_не_делает()
        {
            var deck = MakeDeck(3);
            var before = deck.GetCardIds();

            Assert.That(deck.ReturnCardsAndShuffle(new List<Card>()), Is.Zero);
            Assert.That(deck.GetCardIds(), Is.EqualTo(before), "пустой возврат не должен и тасовать");
        }

        [Test]
        public void Возврат_не_меняет_изначальный_размер()
        {
            var deck = MakeDeck(4);
            var card = deck.DrawCard();
            deck.ReturnCardsAndShuffle(new[] { card });

            Assert.That(deck.CountDeckInitial, Is.EqualTo(4));
        }

        [Test]
        public void Вернувшиеся_карты_не_ложатся_предсказуемой_стопкой_сверху()
        {
            // Состав руки ушедшего игрока видели все за столом. Лягут карты
            // сверху по порядку — и следующие взятия перестанут быть тайной.
            // Тест вероятностный по своей природе, поэтому смотрим на серию:
            // без тасовки верхняя карта была бы одной и той же всегда.
            var topCards = new HashSet<int>();
            for (int attempt = 0; attempt < 40; attempt++)
            {
                var deck = MakeDeck(10);
                var hand = new List<Card> { deck.DrawCard(), deck.DrawCard(), deck.DrawCard() };
                deck.ReturnCardsAndShuffle(hand);
                topCards.Add(deck.PeekTop(1).Single().Id);
            }

            Assert.That(topCards.Count, Is.GreaterThan(1), "после возврата колода не перетасована");
        }
    }
}
