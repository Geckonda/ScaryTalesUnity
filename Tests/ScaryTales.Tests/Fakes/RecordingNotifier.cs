using System.Collections.Generic;
using System.Linq;
using ScaryTales.Abstractions;

namespace ScaryTales.Tests.Fakes
{
    /// <summary>
    /// Запоминает всё, что движок сказал игрокам.
    ///
    /// <para>Полезен не ради самих текстов, а ради порядка: у клиента порядок
    /// событий и есть визуальный сценарий («показать до начисления»), и часть
    /// эффектов держится именно на нём.</para>
    /// </summary>
    public class RecordingNotifier : INotifier
    {
        public List<string> Messages { get; } = new();

        public void Notify(string message) => Messages.Add(message);

        /// <summary>Было ли сказано что-то, содержащее эту подстроку.</summary>
        public bool Said(string fragment) => Messages.Any(m => m.Contains(fragment));

        public override string ToString() => string.Join("\n", Messages);
    }
}
