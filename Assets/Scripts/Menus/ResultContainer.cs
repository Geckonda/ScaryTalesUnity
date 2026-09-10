using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Assets.Scripts.Network;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Assets.Scripts.Menus
{
    public class ResultContainer : MonoBehaviour
    {
        public static ResultContainer Instance { get; private set; }
        [SerializeField] private TMP_Text WinnerText;

        [Tooltip("Кнопка «Выйти» на экране результата. Пока не привязана, уйти из законченной партии можно только через меню по Esc.")]
        [SerializeField] private Button _exitButton;

        /// <summary>
        /// Показан ли уже итог партии. По нему обрыв связи отличает «партия
        /// кончилась, и это её следствие» от «партия оборвалась прямо сейчас».
        /// </summary>
        public bool IsShowing => gameObject.activeSelf;

        private void Awake()
        {
            Instance = this;
            if (_exitButton != null) _exitButton.onClick.AddListener(Exit);
            gameObject.SetActive(false); // По умолчанию скрыто
        }
        /// <summary>
        /// Экран результата. Победителей может быть несколько: тайбрейкера в
        /// правилах нет, и все, кто набрал наибольший счёт, выигрывают вместе.
        ///
        /// <para>Ничья названа ничьёй прямым текстом. «Победители: Вася, Петя»
        /// без этого слова читается как список призёров, где первый главнее, —
        /// а они равны.</para>
        /// </summary>
        public void ShowWinner(IReadOnlyList<string> winners)
        {
            if (winners == null || winners.Count == 0)
            {
                // Победителей не осталось — партия кончилась, а назвать некого.
                // Такое возможно, если все вышли; молчать тут хуже, чем сказать.
                ShowMessage("Партия окончена.");
                return;
            }

            ShowMessage(winners.Count == 1
                ? $"Победитель: {winners[0]}"
                : $"Ничья! Победители: {string.Join(", ", winners)}");
        }

        /// <summary>
        /// Same panel, arbitrary text. Used when the game ends without a
        /// winner — a player left, or the server tore the room down.
        /// </summary>
        public void ShowMessage(string message)
        {
            gameObject.SetActive(true);
            WinnerText.text = message;

            // Партия больше не выбрасывает игрока в меню сама, так что без
            // этой кнопки экран результата — тупик со всего одним выходом
            // (Esc). Молчать об этом нельзя: выглядело бы как зависание.
            if (_exitButton == null)
                Debug.LogWarning("[ResultContainer] Кнопка «Выйти» не привязана в сцене — из законченной партии придётся выходить через меню по Esc.");
        }

        /// <summary>
        /// Тот же выход, что у «Выйти из комнаты» в меню по Esc: отключиться
        /// и вернуться в меню. Сервер освободит место и уничтожит комнату,
        /// когда её покинет последний игрок.
        /// </summary>
        private void Exit()
        {
            GameConnectionManager.ReturnToMenu();
        }
    }
}
