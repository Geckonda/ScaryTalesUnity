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

    private Card _card;
    private Image _background;

    [SerializeField] private Sprite _enemyBackgroundSprite; 

    public void Initialize(Card card)
    {
        _card = card;
        _background = GetComponent<Image>();
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
        SetCardViewBackground(_card.Owner);
    }
    public void SetCardViewBackground(Player player)
    {
        if(player == null)
            return;
        var localPlayer = UnGameManager.Instance.LocalPlayer;
        if(player.Id != localPlayer.Id)
        {
            _background.color = new Color(1f, 1f, 1f, 1f); // Белый цвет с полной непрозрачностью
            ChangeTextVisibility(false);
            _background.sprite = _enemyBackgroundSprite;

        }
        else
        {
            _background.color = Color.black;
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
    private void ChangeTextVisibility(bool isVisible)
    {
        _cardNameText.gameObject.SetActive(isVisible);
        _cardDescriptionText.gameObject.SetActive(isVisible);
        _cardScoreText.gameObject.SetActive(isVisible);
        _cardTypeText.gameObject.SetActive(isVisible);
        _cardQuantityText.gameObject.SetActive(isVisible);

    }
}