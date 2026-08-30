using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

using UnityEngine.EventSystems;

namespace Assets.Scripts.UIEntities.Components
{
    using UnityEngine;
    using UnityEngine.UI;
    using System.Collections.Generic;
    using UnityEngine.UIElements;

    [ExecuteAlways]
    public class FanLayoutGroup : LayoutGroup
    {
        [Range(0, 180)] public float angle = 110f;
        [Range(0, 100)] public float spacing = 40f;
        [Range(-1000, 1000)] public float verticalOffset = -150f; // <- добавили

        [Range(0, 300)] public float radius = 0f;
        [Range(0.1f, 3f)] public float scale = 1f; // масштаб карты
        public bool fanUpwards = true; // направление веера

        public override void CalculateLayoutInputHorizontal()
        {
            base.CalculateLayoutInputHorizontal();
            ArrangeCards();
        }

        public override void CalculateLayoutInputVertical() => ArrangeCards();
        public override void SetLayoutHorizontal() => ArrangeCards();
        public override void SetLayoutVertical() => ArrangeCards();

        private void ArrangeCards()
        {
            int count = rectChildren.Count;
            if (count == 0) return;

            float stepAngle = count > 1 ? angle / (count - 1) : 0;

            for (int i = 0; i < count; i++)
            {
                var child = rectChildren[i];

                float rotationZ = -angle / 2 + i * stepAngle;
                float radians = rotationZ * Mathf.Deg2Rad;

                Vector2 pos = new Vector2(
                    Mathf.Sin(radians) * radius,
                    (fanUpwards ? 1 : -1) * Mathf.Cos(radians) * radius
                );

                // SetChildAlongAxis places the *unscaled* rect, and the card
                // pivots at its bottom edge — so a scaled-down card keeps its
                // bottom and shrinks upward, drifting out of the panel.
                // Compensating here makes verticalOffset == 0 mean "веер по
                // центру панели" at any scale.
                float h = child.rect.height;
                float centred = rectTransform.rect.height / 2f - h + h * scale / 2f;

                SetChildAlongAxis(child, 0, rectTransform.rect.width / 2 + pos.x - child.rect.width / 2);
                SetChildAlongAxis(child, 1, centred + pos.y + verticalOffset);

                child.localRotation = Quaternion.Euler(0, 0, rotationZ);
                child.localScale = Vector3.one * scale;
            }
        }

    }
}
