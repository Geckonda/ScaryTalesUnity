using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ScaryTales.Lobby
{
    /// <summary>
    /// Приводит имя, которое ввёл игрок, к тому, что можно показать за столом.
    ///
    /// <para>Чистые функции, вынесенные из серверной комнаты: тут нет ни сети,
    /// ни Unity, зато есть три решения, которые легко сломать незаметно —
    /// вырезание разметки, схлопывание пробелов и различение тёзок.</para>
    /// </summary>
    public static class NameSanitizer
    {
        public const int DefaultMaxLength = 14;

        /// <summary>
        /// Чистит имя и делает его непохожим на уже занятые.
        ///
        /// <para><b>Что вырезается и почему.</b> Угловые скобки — потому что
        /// на них держится разметка подписей, и один игрок мог бы перекрасить
        /// интерфейс всем остальным. Управляющие символы и переводы строк —
        /// потому что подпись однострочная. Пробелы схлопываются и не
        /// начинают имя: длинная цепочка пробелов рвёт вёрстку у всех, кроме
        /// того, кто её ввёл.</para>
        ///
        /// <para><b>Регистр.</b> Первая буква заглавная, остальные строчные, по
        /// инвариантной культуре. Иначе за столом соседствуют «ВАСЯ», «вася» и
        /// «вАсЯ», и капс тянет внимание сильнее, чем чужой ход. Культура
        /// именно инвариантная: сервер может крутиться где угодно, а в турецкой
        /// локали <c>ToLower('I')</c> даёт «ı», и один и тот же ник
        /// превращался бы в разный в зависимости от машины.</para>
        /// </summary>
        /// <param name="requested">Что ввёл игрок. Допускается null.</param>
        /// <param name="existing">Имена, уже занятые за этим столом.</param>
        /// <param name="seatOrdinal">
        /// Номер для запасного имени, если чистить оказалось нечего:
        /// получится «PlayerN».
        /// </param>
        public static string Sanitize(
            string requested,
            IEnumerable<string> existing,
            int seatOrdinal,
            int maxLength = DefaultMaxLength)
        {
            var taken = existing?.ToList() ?? new List<string>();

            var cleaned = new StringBuilder(maxLength);
            bool lastWasSpace = false;

            foreach (var c in requested ?? string.Empty)
            {
                if (c == '<' || c == '>' || char.IsControl(c)) continue;

                if (char.IsWhiteSpace(c))
                {
                    if (lastWasSpace || cleaned.Length == 0) continue;
                    lastWasSpace = true;
                    cleaned.Append(' ');
                }
                else
                {
                    lastWasSpace = false;
                    cleaned.Append(c);
                }
                if (cleaned.Length >= maxLength) break;
            }

            var name = cleaned.ToString().TrimEnd();
            if (name.Length == 0)
                name = $"Player{seatOrdinal}";

            return MakeUnique(Capitalize(name), taken, maxLength);
        }

        /// <summary>Первая буква заглавная, остальные строчные, инвариантно.</summary>
        public static string Capitalize(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            return char.ToUpperInvariant(name[0]) + name.Substring(1).ToLowerInvariant();
        }

        /// <summary>
        /// Два «Саши» за одним столом хуже, чем «Саша» и «Саша (2)»: путаница
        /// в том, чей сейчас ход, стоит дороже, чем испорченный ник. Сравнение
        /// без учёта регистра — «саша» и «Саша» это одна и та же проблема.
        /// </summary>
        public static string MakeUnique(string name, IReadOnlyList<string> taken, int maxLength = DefaultMaxLength)
        {
            if (!IsTaken(name, taken)) return name;

            for (int suffix = 2; suffix < 100; suffix++)
            {
                var tail = $" ({suffix})";

                // Подрезаем ОСНОВУ, а не готовую строку. Обрежь мы результат
                // целиком — откусился бы сам различитель, и получился бы
                // второй игрок с тем же именем: ровно то, от чего этот метод.
                var head = name.Length + tail.Length > maxLength
                    ? name.Substring(0, Math.Max(1, maxLength - tail.Length)).TrimEnd()
                    : name;

                var candidate = head + tail;
                if (!IsTaken(candidate, taken)) return candidate;
            }

            // Недостижимо на четырёх местах за столом, но молчать здесь нельзя:
            // одинаковые имена хуже некрасивого.
            return $"{name} #{taken.Count + 1}";
        }

        private static bool IsTaken(string name, IReadOnlyList<string> taken) =>
            taken.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
    }
}
