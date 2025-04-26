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
        //if(player != null)
        //{
        //    _background.color = player.Name == "Вова" ? Color.blue : Color.red;
        //}
        if(player == null)
            return;
        var localPlyaer = UnGameManager.Instance.LocalPlayer;
        _background.color = player.Id == localPlyaer.Id ? Color.blue : Color.red;
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

}