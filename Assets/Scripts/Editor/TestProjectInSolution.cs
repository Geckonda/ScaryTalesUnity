using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ScaryTales.EditorTools
{
    /// <summary>
    /// Вписывает <c>Tests/ScaryTales.Tests</c> в решение, которое генерирует Unity.
    ///
    /// <para><b>Зачем это код, а не разовая правка руками.</b> Файлы
    /// <c>ScaryTalesUnity.sln</c> и <c>*.csproj</c> Unity создаёт заново при
    /// каждой перекомпиляции скриптов — они и в <c>.gitignore</c> по этой
    /// причине. Проект, добавленный в решение мышкой, живёт до первого
    /// изменения любого <c>.cs</c>, после чего молча исчезает. Поэтому
    /// добавление должно происходить в тот же момент, когда решение
    /// создаётся.</para>
    ///
    /// <para>Пакет <c>com.unity.ide.visualstudio</c> зовёт этот метод, отдавая
    /// готовый текст решения и ожидая изменённый обратно.</para>
    ///
    /// <para>Тестовый проект — обычный .NET-проект, он лежит вне <c>Assets/</c>
    /// и Unity о нём ничего не знает и знать не должна: он собирается без
    /// Unity, за секунды, и им же пользуется CI. Решение — единственное место,
    /// где две эти половины встречаются.</para>
    /// </summary>
    public class TestProjectInSolution : AssetPostprocessor
    {
        private const string ProjectName = "ScaryTales.Tests";

        /// <summary>Путь относительно корня проекта — того, где лежит Assets.</summary>
        private const string ProjectRelativePath = @"Tests\ScaryTales.Tests\ScaryTales.Tests.csproj";

        /// <summary>
        /// Свой, постоянный. Именно постоянство здесь и важно: меняющийся
        /// идентификатор каждый раз выглядел бы для Visual Studio новым
        /// проектом, и она теряла бы всё, что о нём помнит, — от раскладки
        /// вкладок до списка тестов.
        /// </summary>
        private const string ProjectGuid = "{3F1C0A47-9B2E-4D68-A5C1-7E0D2B4F8A96}";

        /// <summary>Тип «проект на C#» — тот же, что у всех проектов, которые пишет Unity.</summary>
        private const string CSharpProjectType = "{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}";

        private static string OnGeneratedSlnSolution(string path, string content)
        {
            try
            {
                return Inject(content);
            }
            catch (Exception e)
            {
                // Сломать генерацию решения — значит оставить человека вообще
                // без проектов в IDE. Это несопоставимо хуже, чем отсутствие
                // одного тестового проекта, поэтому любая неожиданность здесь
                // заканчивается возвратом исходного текста.
                Debug.LogWarning($"[{nameof(TestProjectInSolution)}] не удалось добавить тесты в решение: {e.Message}");
                return content;
            }
        }

        private static string Inject(string content)
        {
            if (string.IsNullOrEmpty(content)) return content;

            // Уже вписан — Unity могла позвать нас по кругу.
            if (content.Contains(ProjectGuid)) return content;

            if (!ProjectFileExists())
            {
                // Проект унесли или переименовали. Ссылка на несуществующий
                // файл превратила бы решение в нерабочее, так что молча
                // отступаем — лучше решение без тестов, чем решение с ошибкой.
                return content;
            }

            var newLine = content.Contains("\r\n") ? "\r\n" : "\n";

            content = InsertProjectDeclaration(content, newLine);
            content = InsertBuildConfigurations(content, newLine);
            return content;
        }

        private static bool ProjectFileExists()
        {
            var projectRoot = Directory.GetParent(Application.dataPath);
            if (projectRoot == null) return false;

            var relative = ProjectRelativePath.Replace('\\', Path.DirectorySeparatorChar);
            return File.Exists(Path.Combine(projectRoot.FullName, relative));
        }

        /// <summary>Объявление проекта идёт последним в списке, прямо перед секцией Global.</summary>
        private static string InsertProjectDeclaration(string content, string newLine)
        {
            var anchor = newLine + "Global" + newLine;
            int index = content.IndexOf(anchor, StringComparison.Ordinal);
            if (index < 0) return content; // формат решения не тот, какого мы ждали

            var declaration =
                $"Project(\"{CSharpProjectType}\") = \"{ProjectName}\", \"{ProjectRelativePath}\", \"{ProjectGuid}\"{newLine}" +
                $"EndProject{newLine}";

            return content.Insert(index + newLine.Length, declaration);
        }

        /// <summary>
        /// Без этих четырёх строк проект в решении виден, но не собирается и не
        /// отдаёт тесты обозревателю: Visual Studio не знает, какую его
        /// конфигурацию считать активной.
        /// </summary>
        private static string InsertBuildConfigurations(string content, string newLine)
        {
            const string anchor = "GlobalSection(ProjectConfigurationPlatforms) = postSolution";
            int index = content.IndexOf(anchor, StringComparison.Ordinal);
            if (index < 0) return content;

            int lineEnd = content.IndexOf(newLine, index, StringComparison.Ordinal);
            if (lineEnd < 0) return content;

            // Отступ табами — так же, как в остальном файле.
            var configurations =
                $"\t\t{ProjectGuid}.Debug|Any CPU.ActiveCfg = Debug|Any CPU{newLine}" +
                $"\t\t{ProjectGuid}.Debug|Any CPU.Build.0 = Debug|Any CPU{newLine}" +
                $"\t\t{ProjectGuid}.Release|Any CPU.ActiveCfg = Release|Any CPU{newLine}" +
                $"\t\t{ProjectGuid}.Release|Any CPU.Build.0 = Release|Any CPU{newLine}";

            return content.Insert(lineEnd + newLine.Length, configurations);
        }
    }
}
