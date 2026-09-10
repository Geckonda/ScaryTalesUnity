using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ScaryTales.Lobby;

namespace ScaryTales.Tests.Lobby
{
    [TestFixture]
    public class RoomCodeTests
    {
        // ---- Алфавит ----

        [Test]
        public void В_алфавите_нет_символов_которые_путают()
        {
            // I, L и O люди набирают как 1 и 0 даже читая с экрана.
            Assert.That(RoomCode.Alphabet, Does.Not.Contain("I"));
            Assert.That(RoomCode.Alphabet, Does.Not.Contain("L"));
            Assert.That(RoomCode.Alphabet, Does.Not.Contain("O"));
        }

        [Test]
        public void В_алфавите_нет_цифр()
        {
            // Код читают вслух, а цифры тянут за собой S/5, Z/2, B/8.
            Assert.That(RoomCode.Alphabet.Any(char.IsDigit), Is.False);
        }

        [Test]
        public void Алфавит_из_двадцати_трёх_различных_заглавных_букв()
        {
            Assert.That(RoomCode.Alphabet.Length, Is.EqualTo(23));
            Assert.That(RoomCode.Alphabet.Distinct().Count(), Is.EqualTo(23));
            Assert.That(RoomCode.Alphabet, Is.EqualTo(RoomCode.Alphabet.ToUpperInvariant()));
        }

        // ---- Приведение ----

        [Test]
        public void Регистр_приводится_к_верхнему()
        {
            Assert.That(RoomCode.Normalize("abcd"), Is.EqualTo("ABCD"));
        }

        [TestCase("A B C D")]
        [TestCase("AB-CD")]
        [TestCase("AB_CD")]
        [TestCase("  ab cd  ")]
        [TestCase("a-b c_d")]
        public void Разделители_которые_ставят_при_диктовке_убираются(string typed)
        {
            Assert.That(RoomCode.Normalize(typed), Is.EqualTo("ABCD"));
        }

        [Test]
        public void Посторонние_символы_остаются_а_не_вычищаются()
        {
            // Код с лишним символом просто ни с чем не совпадёт, и «нет такой
            // комнаты» — честнее, чем молча выкинуть символ и посадить игрока
            // за чужой стол.
            Assert.That(RoomCode.Normalize("AB!CD"), Is.EqualTo("AB!CD"));
            Assert.That(RoomCode.Normalize("ABC1"), Is.EqualTo("ABC1"));
        }

        [Test]
        public void Пустое_и_null_дают_пустую_строку()
        {
            Assert.That(RoomCode.Normalize(null), Is.EqualTo(string.Empty));
            Assert.That(RoomCode.Normalize(""), Is.EqualTo(string.Empty));
            Assert.That(RoomCode.Normalize("   "), Is.EqualTo(string.Empty));
        }

        [Test]
        public void Приведение_идемпотентно()
        {
            var once = RoomCode.Normalize(" ab-cd ");

            Assert.That(RoomCode.Normalize(once), Is.EqualTo(once));
        }

        // ---- Подбор кода ----

        [Test]
        public void Код_нужной_длины_и_только_из_алфавита()
        {
            var rng = new Random(20260910);

            for (int i = 0; i < 200; i++)
            {
                var code = RoomCode.Allocate(_ => false, rng);

                Assert.That(code.Length, Is.EqualTo(RoomCode.Length));
                Assert.That(code.All(RoomCode.Alphabet.Contains), Is.True, $"код «{code}» вне алфавита");
            }
        }

        [Test]
        public void Выданный_код_проходит_приведение_без_изменений()
        {
            // Иначе игрок, набравший ровно то, что видит, не попал бы в комнату.
            var rng = new Random(1);

            for (int i = 0; i < 50; i++)
            {
                var code = RoomCode.Allocate(_ => false, rng);
                Assert.That(RoomCode.Normalize(code), Is.EqualTo(code));
            }
        }

        [Test]
        public void Занятый_код_не_выдаётся_повторно()
        {
            var taken = new HashSet<string>();
            var rng = new Random(42);

            for (int i = 0; i < 100; i++)
            {
                var code = RoomCode.Allocate(taken.Contains, rng);
                Assert.That(code, Is.Not.Null);
                Assert.That(taken.Add(code), Is.True, $"код «{code}» выдан дважды");
            }
        }

        [Test]
        public void Когда_все_коды_заняты_подбор_сдаётся_а_не_виснет()
        {
            // Сервер, не нашедший свободный код за сотню попыток, держит
            // неправдоподобное число комнат. Упасть заметно лучше, чем
            // зациклиться навсегда.
            Assert.That(RoomCode.Allocate(_ => true, new Random(0)), Is.Null);
        }

        [Test]
        public void Коды_не_повторяются_подряд()
        {
            // Грубая проверка, что источник случайности вообще работает:
            // сотня подряд одинаковых кодов означала бы сломанный генератор.
            var rng = new Random(7);
            var codes = Enumerable.Range(0, 100)
                .Select(_ => RoomCode.Allocate(_ => false, rng))
                .Distinct()
                .Count();

            Assert.That(codes, Is.GreaterThan(90));
        }
    }
}
