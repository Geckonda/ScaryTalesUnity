using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class CardTableLayout : LayoutGroup
{
    [Header("Настройки")]
    public Vector2 cardSize = new Vector2(150f, 250f); // базовый размер карты
    public float spacing = 5f;                        // базовое расстояние между картами
    public int shrinkThreshold = 5;                    // каждые N карт будет уменьшение
    public float shrinkStep = 0.95f;                    // коэффициент уменьшения на каждом этапе

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

        // Вычисляем общую ширину всех групп
        float groupWidth = cardSize.x;
        float totalSpacing = spacing * (cardGroups.Count - 1);
        float totalWidth = groupWidth * cardGroups.Count + totalSpacing;

        // Начальная X-позиция (левый край, чтобы всё оказалось по центру)
        float startX = (rectTransform.rect.width - totalWidth) / 2f;

        int groupIndex = 0;
        foreach (var group in cardGroups)
        {
            var cards = group.Value;
            float x = startX + groupIndex * (cardSize.x + spacing);

            float yOffset = 20f;

            for (int i = 0; i < cards.Count; i++)
            {
                var card = cards[i];
                float y = yOffset * i;

                SetChildAlongAxis(card, 0, x, cardSize.x);      // по X
                SetChildAlongAxis(card, 1, -y, cardSize.y);     // по Y

                card.localScale = Vector3.one;
            }

            groupIndex++;
        }
    }


}
