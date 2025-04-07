using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace Assets.Scripts.Views
{
    public class DiscardPileView : MonoBehaviour
    {
        public static DiscardPileView Instance { get; private set; }

        public Image targetImage; // Ссылка на компонент Image
        public Sprite newSprite;   // Спрайт, который вы хотите установить

        private void Awake()
        {

            if (Instance == null)
            {
                Instance = this;
            }
            else
            {
                Destroy(gameObject);
            }
        }
        public void SetSuit()
        {
            if (targetImage != null && newSprite != null)
            {
                // Устанавливаем новый спрайт
                targetImage.color = Color.white;
                targetImage.sprite = newSprite;
            }
            else
            {
                Debug.LogError("Target Image or New Sprite is not assigned!");
            }
        }
    }
}
