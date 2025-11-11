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
            gameObject.SetActive(true);
            WinnerText.text = $"Победитель: {winner}";
        }
    }
}
