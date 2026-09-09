using System;
using ScaryTales.Enums;
using UnityEngine;

/// <summary>
/// Оформление карты, которое <b>зависит от данных карты</b> и потому не может быть запечено
/// в статичный префаб: иконка и цвет типа, значки ПО и количества, значок «куда уходит карта».
///
/// <para>Рамка, плашка заголовка и панель описания сюда намеренно не попали — они одинаковы
/// для всех карт и живут прямо на <c>Image</c>-компонентах префаба. Свою графику туда
/// подставляют перетаскиванием спрайта, без правок кода.</para>
///
/// <para>Пока спрайтов нет, поля пустые: <see cref="CardView"/> тогда просто не трогает
/// соответствующие картинки, а тип и количество читаются словом и числом, как раньше.
/// Появился спрайт — положили в поле и включили галочку.</para>
/// </summary>
[CreateAssetMenu(menuName = "ScaryTales/Card Style", fileName = "CardStyle")]
public class CardStyle : ScriptableObject
{
    [Serializable]
    public struct TypeStyle
    {
        public CardType Type;
        public Sprite Icon;
        public Color Tint;
    }

    [Serializable]
    public struct PositionStyle
    {
        public CardPosition Position;
        public Sprite Icon;
    }

    [Header("Значки")]
    [Tooltip("Подложка под очки победы. Пусто — останется то, что стоит на префабе.")]
    public Sprite PointsIcon;

    [Tooltip("Подложка под количество карт в колоде.")]
    public Sprite CountIcon;

    [Header("Переключатели")]
    [Tooltip("Показывать иконку типа рядом со словом. Включать, когда иконки нарисованы.")]
    public bool ShowTypeIcon;

    [Tooltip("Показывать значок «куда уходит карта после розыгрыша».")]
    public bool ShowAfterPlayBadge;

    [Header("Типы карт")]
    public TypeStyle[] Types = Array.Empty<TypeStyle>();

    [Header("Куда уходит карта")]
    public PositionStyle[] Positions = Array.Empty<PositionStyle>();

    [Tooltip("Цвет значка типа, если для типа ничего не задано.")]
    public Color FallbackTint = Color.white;

    public Sprite GetTypeIcon(CardType type)
    {
        foreach (var style in Types)
            if (style.Type == type) return style.Icon;
        return null;
    }

    public Color GetTypeTint(CardType type)
    {
        foreach (var style in Types)
            if (style.Type == type) return style.Tint;
        return FallbackTint;
    }

    public Sprite GetPositionIcon(CardPosition position)
    {
        foreach (var style in Positions)
            if (style.Position == position) return style.Icon;
        return null;
    }
}
