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
        public void ShowWinner(string winner)
        {
            ShowMessage($"Победитель: {winner}");
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
