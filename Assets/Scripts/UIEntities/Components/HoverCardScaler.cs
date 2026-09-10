using Assets.Scripts.Services;
using ScaryTales;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Assets.Scripts.UIEntities.Components
{
    /// <summary>
    /// Карта под указателем показывает себя крупно.
    ///
    /// <para>Само превью компонент не держит и не создаёт: этим занят
    /// <see cref="CardPreviewService"/>, единственный владелец. Здесь остались
    /// только два факта — «на меня навели» и «с меня ушли», — потому что
    /// хранение превью на карте и было причиной фантомов: владельцев
    /// оказывалось столько же, сколько карт.</para>
    /// </summary>
    public class HoverCardScaler : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private CardView _cardView;

        private CardView View => _cardView != null ? _cardView : _cardView = GetComponent<CardView>();

        public void OnPointerEnter(PointerEventData eventData)
        {
            var view = View;
            // Рубашка крупно не показывается: у чужой карты показывать нечего.
            if (view == null || !view.IsFacedUp) return;

            CardPreviewService.Instance.Show(view._card);
        }

        public void OnPointerExit(PointerEventData eventData) => HidePreview();

        /// <summary>
        /// Карту уничтожили или выключили, пока она была под указателем.
        ///
        /// <para>Без этого превью оставалось на экране навсегда: уход с карты
        /// не наступает, если карты больше нет, — а разыгранная карта покидает
        /// руку именно так, из-под курсора. Раньше это и был самый частый
        /// фантом на мышке.</para>
        /// </summary>
        private void OnDisable() => HidePreview();

        private void HidePreview()
        {
            var view = _cardView;
            if (view == null) return;

            // По своей карте, а не безусловно: на тач-вводе указателей
            // несколько, и уход с одной карты не должен гасить превью,
            // показанное другой.
            CardPreviewService.Instance.Hide(view._card);
        }
    }
}
