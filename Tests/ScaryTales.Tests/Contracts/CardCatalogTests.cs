using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ScaryTales;
using ScaryTales.Enums;

namespace ScaryTales.Tests.Contracts
{
    /// <summary>
    /// Каталог карт — это формат провода, а не просто список.
    ///
    /// <para><see cref="Deck"/> раздаёт id позиционно по
    /// <see cref="GameBuilder.MakeCardTemplates"/>, и клиент строит свой каталог
    /// по этому же списку. Значит порядок списка — общий контракт сторон.
    /// Вставка карты в середину молча перенумеровывает всё, что ниже: сервер
    /// пришлёт «карта №45 в слот времени суток», клиент найдёт у себя под №45
    /// другую карту и честно её нарисует. Ничего не упадёт — игра просто начнёт
    /// показывать не то, что происходит.</para>
    ///
    /// <para>Эти тесты падают ровно в тот момент, когда контракт меняют. Если
    /// изменение намеренное — правьте эталон здесь и не забудьте, что старые
    /// клиенты с новым сервером уже не совместимы.</para>
    /// </summary>
    [TestFixture]
    public class CardCatalogTests
    {
        /// <summary>
        /// Эталон: имя и количество экземпляров, в порядке каталога.
        /// Порядок здесь значим — это он раздаёт id.
        /// </summary>
        private static readonly (string Name, int Count)[] ExpectedCatalog =
        {
            ("Дитя ночи", 2),
            ("Старый мудрец", 2),
            ("Темный владыка", 2),
            ("Дракон", 2),
            ("Зачарованный лес", 2),
            ("Принцесса", 2),
            ("Купец", 3),
            ("Волшебник", 2),
            ("Ночь", 4),
            ("День", 3),
            ("Огр", 2),
            ("Мудрость короля", 2),
            ("Сумасбродство короля", 2),
            ("Фея", 2),
            ("Молодой герой", 2),
            ("Потаенная пещера", 2),
            ("Проклятый замок", 2),
            ("Чары", 2),
            // Новые карты дописываются В КОНЕЦ — и здесь тоже.
            ("Цыганка", 4),
            ("Тайная помощница", 2),
            ("Ведьма", 2),
        };

        [Test]
        public void Каталог_карт_идёт_в_ожидаемом_порядке()
        {
            var actual = GameBuilder.MakeCardTemplates()
                .Select(c => (c.Name, c.CardCountInDeck))
                .ToArray();

            Assert.That(actual, Is.EqualTo(ExpectedCatalog),
                "Порядок или состав каталога изменился — это перенумеровывает карты на проводе.");
        }

        [Test]
        public void В_колоде_сорок_восемь_карт_двадцати_одного_вида()
        {
            var templates = GameBuilder.MakeCardTemplates();

            // Числа держим явными, а не считаем из ExpectedCatalog: смысл теста
            // в том, чтобы изменение размера колоды нельзя было не заметить.
            Assert.That(templates.Count, Is.EqualTo(21), "видов карт");
            Assert.That(templates.Sum(c => c.CardCountInDeck), Is.EqualTo(48), "карт всего");
        }

        [Test]
        public void Отпечаток_каталога_не_изменился()
        {
            // Отпечаток едет в GameStartedEvent, и клиент сверяет его со своим.
            // Литерал, а не пересчёт: пересчёт по тем же правилам согласился бы
            // сам с собой при любой поломке.
            Assert.That(GameBuilder.CardCatalogVersion(), Is.EqualTo(ExpectedCatalogVersion));
        }

        private const int ExpectedCatalogVersion = -1023349457;

        [Test]
        public void Колода_раздаёт_id_подряд_начиная_с_единицы()
        {
            var deck = new Deck(GameBuilder.MakeCardTemplates());
            var ids = deck.GetCardIds();

            Assert.That(ids.Count, Is.EqualTo(48));
            Assert.That(ids.Distinct().Count(), Is.EqualTo(48), "id повторяются");
            Assert.That(ids.OrderBy(i => i), Is.EqualTo(Enumerable.Range(1, 48)),
                "id должны быть ровно 1..48 без пропусков");
        }

        [Test]
        public void Id_идут_по_порядку_каталога_а_не_по_порядку_в_колоде()
        {
            // Колода тасуется в конструкторе, поэтому позиция в ней случайна.
            // Но соответствие «id → имя» задаётся каталогом и обязано совпасть
            // с тем, что построит клиент.
            var templates = GameBuilder.MakeCardTemplates();
            var expectedNameById = new List<string> { null }; // индекс 0 не используется: id начинаются с 1
            foreach (var t in templates)
                for (int i = 0; i < t.CardCountInDeck; i++)
                    expectedNameById.Add(t.Name);

            var deck = new Deck(templates);
            foreach (var id in deck.GetCardIds())
            {
                var card = deck.TakeCardById(id);
                Assert.That(card, Is.Not.Null, $"карта {id} пропала из колоды");
                Assert.That(card.Name, Is.EqualTo(expectedNameById[id]), $"id {id} принадлежит другой карте");
            }
        }

        [Test]
        public void Имена_карт_уникальны()
        {
            // На имени завязан не только UI: MerchantEffect ищет карту по
            // литералу "Купец", а арт грузится из Resources/CardsImages/<Имя>.
            var names = GameBuilder.MakeCardTemplates().Select(c => c.Name).ToList();
            Assert.That(names.Distinct().Count(), Is.EqualTo(names.Count));
        }

        [Test]
        public void Каждая_карта_описана_и_положительна_по_числу_экземпляров()
        {
            foreach (var card in GameBuilder.MakeCardTemplates())
            {
                Assert.That(card.Name, Is.Not.Null.And.Not.Empty, $"{card.GetType().Name}: пустое имя");
                Assert.That(card.CardCountInDeck, Is.GreaterThan(0), $"{card.Name}: ноль экземпляров");
                Assert.That(card.Points, Is.GreaterThanOrEqualTo(0),
                    $"{card.Name}: отрицательные очки — Player.AddPoints на них бросает");
            }
        }

        [Test]
        public void Каталог_предметов_идёт_в_ожидаемом_порядке()
        {
            var actual = GameBuilder.MakeItemTemplates().Select(i => i.Type).ToArray();

            Assert.That(actual, Is.EqualTo(new[]
            {
                ItemType.Coin,
                ItemType.Armor,
                ItemType.Sword,
                ItemType.MagicStick,
            }));
        }
    }
}
