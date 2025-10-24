using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Assets.Scripts.UIEntities.Components.Cursors
{
    public class CardHoverCursor : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public void OnPointerEnter(PointerEventData eventData)
        {
            CardView cardView = GetComponent<CardView>();
            // Используем менеджер если есть, иначе старый код
            if (CursorManager.Instance != null && cardView.IsFacedUp)
                CursorManager.Instance.SetPointerCursor();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            // Возвращаем курсор по умолчанию через менеджер
            if (CursorManager.Instance != null)
                CursorManager.Instance.SetDefaultCursor();
            else
                Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        }
    }
}
