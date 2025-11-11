using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Assets.Scripts.UIEntities.Components
{
    public class HoverCardScaler : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private CardView _createdCardView; // Храним ссылку на созданный объект

        public void OnPointerEnter(PointerEventData eventData)
        {
            CardView cardView = GetComponent<CardView>();
            var cardsScalerPanel = GameObject.Find("CardScaleContainer");

            if (cardsScalerPanel != null && cardView.IsFacedUp)
            {
                // Создаём новый CardView и сохраняем ссылку
                _createdCardView = CardViewService.Instance.CreateSingleCardViewClone(
                    cardView._card,
                    cardsScalerPanel.transform
                );
                _createdCardView.transform.localScale = new Vector3(2.3f, 2.3f, 2.3f);
                _createdCardView.FaceUp();
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            // Проверяем, что объект был создан и существует
            if (_createdCardView != null && _createdCardView.gameObject != null)
            {
                Destroy(_createdCardView.gameObject);
                _createdCardView = null; // Очищаем ссылку
            }
        }
    }
}
