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
        [Range(-300, 300)] public float verticalOffset = -100f;
        [Range(0.1f, 3f)] public float scale = 1.3f;
        [Range(0, 100)] public float ellipseHeight = 30f;
        [Range(0, 90)] public float rotationAngle = 10f;

        [Tooltip("Насколько соседние карты заходят друг на друга, долей от ширины карты. Это доля, а не пиксели, потому что шаг обязан считаться от ВИДИМОЙ ширины (cellSize * scale): пока шаг брался от нескейленной ячейки, уменьшение карт раздвигало руку, и на двух-трёх картах между ними зияли просветы.")]
        [Range(0f, 0.8f)] public float overlap = 0.3f;

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

            float available = rectTransform.rect.width;

            // Пересчёт может случиться раньше, чем у панели посчитан её rect.
            // Тогда startX уходит в минус и рука уезжает за экран — ровно та
            // же ловушка, что стоила расследования в CardTableLayout.
            if (available <= 0f) return;

            // Шаг считается от ВИДИМОЙ ширины карты, а не от ячейки. Раньше он
            // брался как cellSize.x + spacing, то есть от нескейленного
            // размера: при scale 1.3 карта была шире шага и рука лежала
            // внахлёст, а стоило уменьшить карты до 1 — тот же шаг развёл их,
            // и на двух-трёх картах между ними появились просветы. Доля
            // перекрытия от такого не зависит.
            float visualWidth = cellSize.x * scale;
            float step = visualWidth * (1f - Mathf.Clamp01(overlap));
            float totalWidth = visualWidth + step * (count - 1);

            // Много карт — сжимаем сильнее, но внутри панели, а не за её край.
            // Прежняя адаптация («минус 10 к spacing за каждую карту сверх
            // пяти») к ширине панели отношения не имела вовсе.
            if (totalWidth > available && count > 1)
            {
                step = (available - visualWidth) / (count - 1);
                totalWidth = available;
            }

            float startX = (available - totalWidth) / 2f;

            for (int i = 0; i < count; i++)
            {
                var child = rectChildren[i];

                float normalized = count > 1 ? (float)i / (count - 1) * 2f - 1f : 0f;

                // sizeDelta задаём ДО SetChildAlongAxis: тот считает позицию
                // от текущего размера ячейки, и с картой, приехавшей другого
                // размера, промахнулся бы на разницу.
                child.sizeDelta = cellSize;

                // SetChildAlongAxis кладёт нескейленный rect, а масштаб карта
                // получает вокруг своего pivot (низ-центр). По X это значит,
                // что видимый центр совпадает с центром ячейки.
                float visualCenterX = startX + i * step + visualWidth / 2f;
                float x = visualCenterX - cellSize.x / 2f;

                float y = rectTransform.rect.height / 2 + verticalOffset +
                          Mathf.Pow(normalized, 2) * ellipseHeight;

                float rotationZ = -normalized * rotationAngle; // знак минус — чтобы наклон был "наружу"

                SetChildAlongAxis(child, 0, x);
                SetChildAlongAxis(child, 1, y);

                child.localRotation = Quaternion.Euler(0, 0, rotationZ);
                child.localScale = Vector3.one * scale;
            }
        }



    }
}
