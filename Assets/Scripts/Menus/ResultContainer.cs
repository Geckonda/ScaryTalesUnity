using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;

namespace Assets.Scripts.Menus
{
    public class ResultContainer : MonoBehaviour
    {
        public static ResultContainer Instance { get; private set; }
        [SerializeField] private TMP_Text WinnerText;

        private void Awake()
        {
            Instance = this;
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
        }
    }
}
