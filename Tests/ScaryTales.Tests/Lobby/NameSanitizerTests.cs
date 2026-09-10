using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using NUnit.Framework;
using ScaryTales.Lobby;

namespace ScaryTales.Tests.Lobby
{
    [TestFixture]
    public class NameSanitizerTests
    {
        private static readonly string[] Nobody = new string[0];

        private static string Clean(string requested, params string[] existing) =>
            NameSanitizer.Sanitize(requested, existing ?? Nobody, existing?.Length + 1 ?? 1);

        // ---- Вырезание опасного ----

        [Test]
        public void Угловые_скобки_вырезаются()
        {
            // На них держится разметка подписей: без вырезания один игрок
            // перекрасил бы интерфейс всем остальным.
            Assert.That(Clean("<b>Вася</b>"), Is.EqualTo("Bвася/b"));
        }

        [Test]
        public void Управляющие_символы_и_переводы_строк_не_проходят()
        {
            // Подпись однострочная; перевод строки ломает вёрстку у всех,
            // кроме автора.
            //
            // Табуляция и перевод строки — управляющие символы, и проверка на
            // них стоит раньше проверки на пробел. Поэтому они вырезаются
            // целиком, а не превращаются в пробел. Обычный пробел остаётся —
            // это проверяется отдельно, ниже.
            Assert.That(Clean("Ва\nся"), Is.EqualTo("Вася"));
            Assert.That(Clean("Ва\tся"), Is.EqualTo("Вася"));
            Assert.That(Clean("Вася"), Is.EqualTo("Вася"));
        }

        // ---- Пробелы ----

        [Test]
        public void Пробелы_схлопываются()
        {
            Assert.That(Clean("Вася     Пупкин"), Is.EqualTo("Вася пупкин"));
        }

        [Test]
        public void Имя_не_начинается_и_не_кончается_пробелом()
        {
            Assert.That(Clean("   Вася   "), Is.EqualTo("Вася"));
        }

        [Test]
        public void Имя_из_одних_пробелов_считается_пустым()
        {
            Assert.That(Clean("      "), Is.EqualTo("Player1"));
        }

        // ---- Запасное имя ----

        [Test]
        public void Пустое_имя_заменяется_на_номер_места()
        {
            Assert.That(NameSanitizer.Sanitize("", Nobody, 3), Is.EqualTo("Player3"));
        }

        [Test]
        public void Null_не_роняет_и_даёт_запасное_имя()
        {
            Assert.That(NameSanitizer.Sanitize(null, Nobody, 1), Is.EqualTo("Player1"));
        }

        // ---- Длина ----

        [Test]
        public void Длинное_имя_обрезается()
        {
            var name = NameSanitizer.Sanitize("Абвгдеёжзийклмнопрстуфхцч", Nobody, 1);

            Assert.That(name.Length, Is.LessThanOrEqualTo(NameSanitizer.DefaultMaxLength));
        }

        [Test]
        public void Ровно_предельное_имя_не_режется()
        {
            var exact = new string('А', NameSanitizer.DefaultMaxLength);

            Assert.That(NameSanitizer.Sanitize(exact, Nobody, 1).Length,
                Is.EqualTo(NameSanitizer.DefaultMaxLength));
        }

        // ---- Регистр ----

        [Test]
        public void Капс_приводится_к_обычному_виду()
        {
            // «ВАСЯ» рядом с «Петя» тянет внимание сильнее, чем чужой ход.
            Assert.That(Clean("ВАСЯ"), Is.EqualTo("Вася"));
            Assert.That(Clean("вАсЯ"), Is.EqualTo("Вася"));
            Assert.That(Clean("вася"), Is.EqualTo("Вася"));
        }

        [Test]
        public void Регистр_приводится_одинаково_в_любой_локали()
        {
            // В турецкой локали ToLower('I') даёт «ı». Сервер может крутиться
            // где угодно, и один и тот же ник не должен превращаться в разный
            // в зависимости от машины.
            var previous = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("tr-TR");
                Assert.That(Clean("IVAN"), Is.EqualTo("Ivan"));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        // ---- Тёзки ----

        [Test]
        public void Тёзка_получает_различитель()
        {
            Assert.That(Clean("Саша", "Саша"), Is.EqualTo("Саша (2)"));
        }

        [Test]
        public void Тёзка_узнаётся_независимо_от_регистра()
        {
            Assert.That(Clean("САША", "Саша"), Is.EqualTo("Саша (2)"));
        }

        [Test]
        public void Третий_тёзка_получает_следующий_номер()
        {
            Assert.That(Clean("Саша", "Саша", "Саша (2)"), Is.EqualTo("Саша (3)"));
        }

        [Test]
        public void Различитель_не_откусывается_при_обрезке()
        {
            // Обрезай мы готовую строку целиком, суффикс « (2)» отвалился бы,
            // и за столом снова оказались бы два одинаковых имени — ровно то,
            // от чего этот код.
            var taken = new string('А', NameSanitizer.DefaultMaxLength);

            var second = NameSanitizer.Sanitize(taken, new[] { taken }, 2);

            Assert.That(second.Length, Is.LessThanOrEqualTo(NameSanitizer.DefaultMaxLength));
            Assert.That(second, Does.EndWith(" (2)"), "различитель обрезан — снова два одинаковых имени");
            Assert.That(second, Is.Not.EqualTo(taken));
        }

        [Test]
        public void Четверо_за_столом_получают_четыре_разных_имени()
        {
            // Сквозная проверка того, ради чего всё: за столом не должно быть
            // двух одинаковых подписей.
            var seated = new List<string>();
            for (int seat = 1; seat <= 4; seat++)
                seated.Add(NameSanitizer.Sanitize("Саша", seated, seat));

            Assert.That(seated, Is.Unique);
            Assert.That(seated, Is.EqualTo(new[] { "Саша", "Саша (2)", "Саша (3)", "Саша (4)" }));
        }

        [Test]
        public void Разные_имена_не_трогаются()
        {
            Assert.That(Clean("Петя", "Саша", "Вася"), Is.EqualTo("Петя"));
        }
    }
}
