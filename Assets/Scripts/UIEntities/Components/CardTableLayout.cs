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
        int count = rectChildren.Count;
        if (count == 0) return;

        // Считаем сколько "шагов" уменьшения нужно
        int shrinkSteps = count / shrinkThreshold;
        float scale = Mathf.Pow(shrinkStep, shrinkSteps); // каждый шаг уменьшает на коэффициент

        float scaledWidth = cardSize.x * scale;
        float scaledHeight = cardSize.y * scale;
        float totalSpacing = spacing * (count - 1);
        float totalWidth = count * scaledWidth + totalSpacing;

        float startX = (rectTransform.rect.width - totalWidth) / 2f;

        for (int i = 0; i < count; i++)
        {
            var child = rectChildren[i];

            float x = startX + i * (scaledWidth + spacing);

            SetChildAlongAxis(child, 0, x, scaledWidth);
            SetChildAlongAxis(child, 1, 0, scaledHeight);

            child.localScale = new Vector3(scale, scale, 1f); // Масштабируем карту
        }
    }
}
