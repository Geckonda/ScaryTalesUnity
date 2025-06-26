using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace Assets.Scripts.UIEntities.Components
{
    [ExecuteAlways]
    public class HandLayoutGroup : LayoutGroup
    {
        public Vector2 cellSize = new Vector2(100f, 150f);
        public Vector2 spacing = new Vector2(10f, 0f);
        [Range(-300, 300)] public float verticalOffset = -100f;
        [Range(0.1f, 3f)] public float scale = 1.3f;
        [Range(0, 100)] public float ellipseHeight = 30f;
        [Range(0, 100)] public float spacingX = 20f;
        [Range(0, 90)] public float rotationAngle = 10f;

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

            float currentSpacingX = spacing.x;

            // --- Адаптивное уменьшение spacing ---
            int maxComfortableCards = 5;
            float overload = count - maxComfortableCards;
            float baseSpacing = spacing.x;

            if (count > maxComfortableCards)
            {
                currentSpacingX = baseSpacing - overload * 10f; // уменьшаем на 10 за каждую "лишнюю"
            }
            else
            {
                currentSpacingX = baseSpacing;
            }

            float totalWidth = count * cellSize.x + (count - 1) * currentSpacingX;
            float startX = (rectTransform.rect.width - totalWidth) / 2f;

            for (int i = 0; i < count; i++)
            {
                var child = rectChildren[i];

                float x = startX + i * (cellSize.x + currentSpacingX);
                float normalized = count > 1 ? (float)i / (count - 1) * 2f - 1f : 0f;

                float y = rectTransform.rect.height / 2 + verticalOffset +
                          Mathf.Pow(normalized, 2) * ellipseHeight;

                float rotationZ = -normalized * rotationAngle; // знак минус — чтобы наклон был "наружу"

                // Устанавливаем позицию (учитывая, что pivot внизу!)
                SetChildAlongAxis(child, 0, x);
                SetChildAlongAxis(child, 1, y);

                child.sizeDelta = cellSize;
                child.localRotation = Quaternion.Euler(0, 0, rotationZ);
                child.localScale = Vector3.one * scale;
            }
        }



    }
}
