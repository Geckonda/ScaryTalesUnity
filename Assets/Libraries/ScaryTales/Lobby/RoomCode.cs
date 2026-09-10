using System;
using System.Text;

namespace ScaryTales.Lobby
{
    /// <summary>
    /// Код комнаты — тот, который игрок диктует другу голосом.
    ///
    /// <para>Алфавит и правила приведения вынесены из серверного реестра
    /// комнат, потому что это решения про людей, а не про сеть, и проверять их
    /// нужно без поднятого сервера.</para>
    /// </summary>
    public static class RoomCode
    {
        /// <summary>
        /// Только буквы, и без I, L и O.
        ///
        /// <para>Только буквы — потому что код читают вслух, а смесь с цифрами
        /// тянет за собой всё семейство S/5, Z/2, B/8. I, L и O выброшены
        /// отдельно: их путают с единицей и нулём даже при чтении с экрана.
        /// Остаётся 23 однозначных символа.</para>
        /// </summary>
        public const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ";

        /// <summary>23^4 ≈ 280 тысяч — с запасом на один сервер.</summary>
        public const int Length = 4;

        /// <summary>
        /// Приводит введённое к виду, сравнимому с хранимым кодом: верхний
        /// регистр, без пробелов и дефисов, которые люди вставляют, диктуя код.
        ///
        /// <para>Символы вне алфавита намеренно <b>остаются</b>, а не
        /// вычищаются. Код с посторонним символом просто ни с чем не совпадёт,
        /// и «нет такой комнаты» — более честный ответ, чем молча выкинуть
        /// лишнее и посадить игрока за чужой стол.</para>
        /// </summary>
        public static string Normalize(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;

            var buffer = new StringBuilder(raw.Length);
            foreach (var c in raw)
            {
                if (char.IsWhiteSpace(c) || c == '-' || c == '_') continue;
                buffer.Append(char.ToUpperInvariant(c));
            }
            return buffer.ToString();
        }

        /// <summary>
        /// Подбирает код, который никем не занят.
        ///
        /// <para>Повторяет попытку при совпадении, но не бесконечно: сервер,
        /// который не нашёл свободный код за сотню попыток, держит
        /// неправдоподобное число комнат, и упасть заметно лучше, чем
        /// зациклиться.</para>
        /// </summary>
        /// <param name="isTaken">Занят ли такой код прямо сейчас.</param>
        /// <returns>Свободный код или null, если подобрать не удалось.</returns>
        public static string Allocate(Func<string, bool> isTaken, Random rng, int maxAttempts = 100)
        {
            var buffer = new StringBuilder(Length);
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                buffer.Clear();
                for (int i = 0; i < Length; i++)
                    buffer.Append(Alphabet[rng.Next(Alphabet.Length)]);

                var candidate = buffer.ToString();
                if (isTaken == null || !isTaken(candidate)) return candidate;
            }
            return null;
        }
    }
}
