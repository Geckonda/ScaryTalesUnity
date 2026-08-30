using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Раскладка зоны, где карты лежат на столе: одинаковые карты складываются
/// в стопку, группы центруются по ширине зоны.
///
/// Зона может быть сильно уже карты (у оппонентов при 3-4 игроках), поэтому
/// раскладка масштабируется через <see cref="scale"/> и никогда не выходит
/// за края зоны — при нехватке места группы наползают друг на друга внутри
/// неё вместо того, чтобы вываливаться наружу.
/// </summary>
public class CardTableLayout : LayoutGroup
{
    [Header("Настройки")]
    public Vector2 cardSize = new Vector2(150f, 250f); // базовый размер карты
    public float spacing = 5f;                         // базовое расстояние между группами
    public float stackStep = 20f;                      // сдвиг одинаковых карт в стопке

    [Tooltip("Масштаб карт в этой зоне. Ставится из SeatLayout по слоту места; общий стол остаётся на 1.")]
    [Range(0.1f, 3f)] public float scale = 1f;

    public override void CalculateLayoutInputHorizontal()
    {
        base.CalculateLayoutInputHorizontal();
        UpdateLayout();
    }

    public override void CalculateLayoutInputVertical() => UpdateLayout();
    public override void SetLayoutHorizontal() => UpdateLayout();
    public override void SetLayoutVertical() => UpdateLayout();

    private void UpdateLayout()
    {
        if (rectChildren.Count == 0) return;

        // Группировка по названию карты
        Dictionary<string, List<RectTransform>> cardGroups = new();

        foreach (var child in rectChildren)
        {
            var cardView = child.GetComponent<CardView>();
            if (cardView == null) continue;

            string name = cardView._cardNameText.text;
            if (!cardGroups.ContainsKey(name))
                cardGroups[name] = new List<RectTransform>();

            cardGroups[name].Add(child);
        }

        int groupCount = cardGroups.Count;
        if (groupCount == 0) return;

        float s = Mathf.Max(0.01f, scale);
        float visualWidth = cardSize.x * s;
        float available = rectTransform.rect.width;

        float step = visualWidth + spacing;
        float totalWidth = groupCount * visualWidth + spacing * (groupCount - 1);

        if (totalWidth > available && groupCount > 1)
        {
            // Не вываливаемся за края зоны: сжимаем шаг так, чтобы крайние
            // группы встали ровно по краям, и позволяем им наползать.
            step = (available - visualWidth) / (groupCount - 1);
            totalWidth = available;
        }

        float startX = (available - totalWidth) / 2f;

        // SetChildAlongAxis работает с нескейленным rect, а pivot карты —
        // низ-центр. По X центр не зависит от масштаба, по Y карта при
        // уменьшении "садится" вниз, поэтому её верх поднимаем обратно к
        // верхнему краю зоны.
        float topCorrection = cardSize.y * (1f - s);

        int groupIndex = 0;
        foreach (var group in cardGroups)
        {
            var cards = group.Value;
            float visualCenterX = startX + groupIndex * step + visualWidth / 2f;
            float x = visualCenterX - cardSize.x / 2f;

            for (int i = 0; i < cards.Count; i++)
            {
                var card = cards[i];
                float y = -stackStep * s * i - topCorrection;

                SetChildAlongAxis(card, 0, x, cardSize.x);      // по X
                SetChildAlongAxis(card, 1, y, cardSize.y);      // по Y

                // Карта приезжает из веера руки со своим поворотом и
                // масштабом — на столе она должна лежать ровно.
                card.localRotation = Quaternion.identity;
                card.localScale = Vector3.one * s;
            }

            groupIndex++;
        }
    }
}
