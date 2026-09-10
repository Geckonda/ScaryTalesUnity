using ScaryTales;
using ScaryTales.Enums;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class CardView : MonoBehaviour, IPointerClickHandler
{
    public event Action<Card> OnCardClicked;

    public TMP_Text _cardNameText;
    public TMP_Text _cardDescriptionText;
    public TMP_Text _cardScoreText;
    public TMP_Text _cardTypeText;
    public TMP_Text _cardQuantityText;
    public Image _highlightFrame; // Ссылка на Image для рамки

    public Card _card;
    private Image _background;

    [Header("Лицо карты")]
    [Tooltip("Контейнер со всем, что видно только у лицевой стороны.")]
    [SerializeField] private GameObject _face;

    [Tooltip("Картинка карты. Отдельный Image, а не корневой: корень занят рамкой и рубашкой.")]
    [SerializeField] private Image _artImage;

    [Header("Значки")]
    [SerializeField] private Image _pointsIcon;
    [SerializeField] private Image _typeIcon;
    [SerializeField] private Image _countIcon;
    [SerializeField] private GameObject _afterPlayBadge;
    [SerializeField] private Image _afterPlayIcon;

    [Header("Оформление")]
    [SerializeField] private CardStyle _style;

    [SerializeField] private Sprite _enemyBackgroundSprite;

    // Лицо карты грузится один раз на представление. Раньше Resources.Load
    // случался при каждом FaceUp, а теперь этот вызов приходится ровно на
    // середину разворота в полёте — обращение к диску дало бы рывок именно
    // там, где он заметнее всего.
    private Sprite _frontSprite;
    private bool _frontSpriteLoaded;

    // Фон лицевой стороны берётся с самого префаба, а не из отдельного поля.
    //
    // Рубашка подменяет корневому Image и спрайт, и цвет (ей нужен чистый белый,
    // иначе картинка уйдёт в тон фона), поэтому исходный вид надо где-то держать —
    // после переворота обратно его больше неоткуда взять. Раньше для этого было
    // сериализованное поле, и фон приходилось задавать дважды: на Image и в нём.
    // Забыл второе — фон возвращался к старому в первый же FaceUp, причём только
    // в игре, потому что в редакторе Image выглядел правильно.
    private Sprite _frontFrameSprite;
    private Color _frontFrameColor = Color.white;
    private bool _frontLookCached;

    public bool IsFacedUp { get; private set; }

    public void Initialize(Card card)
    {
        _card = card;
        _background = GetComponent<Image>();
        CacheFrontLook();

        // Кэш лица привязан к карте, а не к представлению: Initialize означает
        // «стань вот этой картой», и оставшийся от прежней картинки спрайт был
        // бы просто чужим лицом. В игре представление на карту одно и повторно
        // не инициализируется, так что лишних чтений с диска это не добавляет.
        _frontSprite = null;
        _frontSpriteLoaded = false;

        DisplayCard();
        SetHighlight(false); // По умолчанию рамка выключена
    }

    public void DisplayCard()
    {
        _cardNameText.text = _card.Name;
        _cardDescriptionText.text = _card.EffectDescription;
        _cardScoreText.text = _card.Points == 0 ? "?" : _card.Points.ToString();
        _cardTypeText.text = _card.Type.GetDescription();
        _cardQuantityText.text = _card.CardCountInDeck.ToString();

        ApplyStyle();
    }

    /// <summary>
    /// Раскладывает по значкам то, что зависит от самой карты: иконку и цвет типа,
    /// картинки ПО и количества, значок «куда уходит карта после розыгрыша».
    ///
    /// <para>Пока художник не нарисовал иконки, все поля стиля пустые — тогда мы просто
    /// не трогаем картинки, и карта читается словом и числом, как читалась раньше.
    /// Появился спрайт — достаточно положить его в <see cref="CardStyle"/>.</para>
    /// </summary>
    private void ApplyStyle()
    {
        if (_style == null) return;

        SetIcon(_pointsIcon, _style.PointsIcon);
        SetIcon(_countIcon, _style.CountIcon);

        Sprite typeSprite = _style.GetTypeIcon(_card.Type);
        bool showTypeIcon = _style.ShowTypeIcon && typeSprite != null;

        if (_typeIcon != null)
        {
            // Тинт на иконку не идёт: Image умножает спрайт на цвет, и настоящий
            // рисунок от этого только портится. Цвет типа остаётся для слова —
            // то есть для случая, когда иконки для типа ещё нет.
            _typeIcon.sprite = typeSprite;
            _typeIcon.gameObject.SetActive(showTypeIcon);
        }

        // Иконка заменяет слово, а не дополняет его. Тип, у которого картинки пока
        // нет, продолжает читаться словом — так набор иконок можно пополнять по одной,
        // не оставляя карты без обозначения типа.
        if (_cardTypeText != null)
        {
            _cardTypeText.color = _style.GetTypeTint(_card.Type);
            _cardTypeText.gameObject.SetActive(!showTypeIcon);
        }

        if (_afterPlayBadge != null)
            _afterPlayBadge.SetActive(_style.ShowAfterPlayBadge);

        if (_afterPlayIcon != null)
            SetIcon(_afterPlayIcon, _style.GetPositionIcon(_card.PositionAfterPlay));
    }

    private static void SetIcon(Image image, Sprite sprite)
    {
        if (image == null) return;
        if (sprite == null)
        {
            image.gameObject.SetActive(false);
            return;
        }

        image.sprite = sprite;
        image.gameObject.SetActive(true);
    }

    /// <summary>
    /// Запоминает, как выглядит корневой Image до того, как его накроет рубашка.
    /// Зовётся из <see cref="Initialize"/>, то есть до первого FaceUp/FaceDown —
    /// там на нём ещё стоит то, что задано в префабе.
    /// </summary>
    private void CacheFrontLook()
    {
        if (_frontLookCached || _background == null) return;

        _frontFrameSprite = _background.sprite;
        _frontFrameColor = _background.color;
        _frontLookCached = true;
    }

    public void FaceUp()
    {
        SetFaceVisible(true);

        if (_background != null)
        {
            CacheFrontLook();
            _background.color = _frontFrameColor;
            _background.sprite = _frontFrameSprite;
        }

        if (!_frontSpriteLoaded)
        {
            _frontSprite = Resources.Load<Sprite>($"CardsImages/{_card.Name}");
            _frontSpriteLoaded = true;

            // Без картинки карта отрисуется пустой — и молча. Новая карта
            // попадает в игру раньше, чем её рисунок, и «почему карта белая»
            // не должно стоить расследования. Один раз на представление:
            // флаг выше не даёт повториться.
            if (_frontSprite == null)
                Debug.LogWarning($"[CardView] Нет картинки для карты «{_card.Name}» — " +
                                 $"ожидается Assets/Resources/CardsImages/{_card.Name}.PNG. " +
                                 "Карта покажется без лица.");
        }

        if (_artImage != null) _artImage.sprite = _frontSprite;
    }

    public void FaceDown()
    {
        SetFaceVisible(false);

        if (_background != null)
        {
            _background.color = new Color(1f, 1f, 1f, 1f); // Белый цвет с полной непрозрачностью
            _background.sprite = _enemyBackgroundSprite;
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        OnCardClicked?.Invoke(_card);
    }

    /// <summary>
    /// Включает или выключает рамку
    /// </summary>
    public void SetHighlight(bool isHighlighted)
    {
        if (_highlightFrame != null)
            _highlightFrame.gameObject.SetActive(isHighlighted);
    }

    /// <summary>
    /// Показывает или прячет лицо карты целиком — одним контейнером.
    ///
    /// <para>Раньше здесь гасились пять текстов по отдельности, и каждое новое поле
    /// нужно было не забыть сюда дописать: забыл — и оно светится поверх рубашки.
    /// Один контейнер закрывает этот класс ошибок целиком.</para>
    /// </summary>
    private void SetFaceVisible(bool isVisible)
    {
        IsFacedUp = isVisible;
        if (_face != null) _face.SetActive(isVisible);
    }
}
