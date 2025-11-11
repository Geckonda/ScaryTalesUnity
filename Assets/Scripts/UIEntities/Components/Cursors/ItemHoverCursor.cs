using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Assets.Scripts.UIEntities.Components.Cursors
{
    public class ItemHoverCursor : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public void OnPointerEnter(PointerEventData eventData)
        {
            ItemView itemView = GetComponent<ItemView>();
            // Используем менеджер если есть, иначе старый код
            if (CursorManager.Instance != null)
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
