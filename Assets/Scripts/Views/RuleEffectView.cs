using Assets.Libraries.ScaryTales;
using Assets.Libraries.ScaryTales.Abstractions;
using ScaryTales;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Assets.Scripts.Views
{
    public class RuleEffectView : MonoBehaviour, IPointerClickHandler
    {
        public event Action<IRuleEffect> OnRuleEffectClicked;

        public TMP_Text ruleEffectDescription;
        public Image background;

        private IRuleEffect _ruleEffect;

        /// <summary>Эффект, который показывает этот вид.</summary>
        public IRuleEffect Effect => _ruleEffect;

        // Исходный цвет фона из префаба. Запоминаем, чтобы «не подсвечено»
        // означало ровно тот вид, который нарисовал дизайнер, а не белый
        // прямоугольник, придуманный кодом.
        private Color _baseColor;
        private bool _baseColorCaptured;

        public void Initialize(IRuleEffect ruleEffect)
        {
            _ruleEffect = ruleEffect;
            EnsureBackground();
            DisplayRuleEffect();
        }

        /// <summary>
        /// Находит или создаёт то, что можно подсветить.
        ///
        /// <para>В префабе <c>RuleEffectPrefab</c> поля <c>background</c> нет
        /// не по забывчивости: там вообще нет ни одного <c>Image</c> — строка
        /// правила это только текст. Поэтому подсветка сама заводит себе фон,
        /// и работает без правки префаба.</para>
        ///
        /// <para>Созданный фон <b>прозрачен</b>, а не белый: пока правило не
        /// подсвечено, он не должен быть виден вовсе. И <c>raycastTarget</c>
        /// у него выключен, чтобы не менять то, чем строка ловит клики
        /// сегодня.</para>
        ///
        /// <para>Если фон в префабе однажды привяжут вручную, используется
        /// он, и его исходный цвет становится видом «не подсвечено».</para>
        /// </summary>
        private void EnsureBackground()
        {
            if (background == null) background = GetComponent<Image>();

            if (background == null)
            {
                background = gameObject.AddComponent<Image>();
                background.color = new Color(1f, 1f, 1f, 0f);
                background.raycastTarget = false;
            }

            if (!_baseColorCaptured)
            {
                _baseColor = background.color;
                _baseColorCaptured = true;
            }
        }

        /// <summary>
        /// Подсветить правило как доступное прямо сейчас — или вернуть
        /// исходный вид.
        /// </summary>
        public void SetAvailable(bool available, Color highlight)
        {
            if (background == null) return;
            background.color = available ? highlight : _baseColor;
        }
        public void OnPointerClick(PointerEventData eventData)
        {
            Debug.Log($"Ты нажал на эффект №{_ruleEffect.Id}");
            OnRuleEffectClicked?.Invoke(_ruleEffect);
        }
        public void DisplayRuleEffect()
        {
            ruleEffectDescription.text = _ruleEffect.Description;
        }

    }
}
