using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Assets.Scripts.UIEntities.Components
{// ===== КАРТА, ПОДНИМАЮЩАЯСЯ ПРИ НАВЕДЕНИИ =====
    public class HoverLift : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public float liftAmount = 20f;
        private Vector3 originalPosition;
        private bool lifted = false;

        private void Start()
        {
            originalPosition = transform.localPosition;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (lifted) return;
            transform.localPosition += new Vector3(0, liftAmount, 0);
            lifted = true;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (!lifted) return;
            transform.localPosition = originalPosition;
            lifted = false;
        }
    }
}
