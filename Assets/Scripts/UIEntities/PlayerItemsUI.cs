using Assets.Scripts.Network;
using ScaryTales;
using ScaryTales.Enums;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Assets.Scripts.UIEntities
{
    /// <summary>
    /// Сумки игроков прямо на столе: полосой под подписями места —
    /// у себя под очками в пустом левом низу, у соперников под их ником.
    ///
    /// <para><b>Четыре слота, а не список предметов.</b> Типов всего четыре,
    /// то есть сумка это не перечень, а четыре счётчика. Фиксированные слоты
    /// занимают одну и ту же ширину при любом их числе — иначе полоса в 53 px
    /// у соперника переполнилась бы на шестом предмете, — и место каждого типа
    /// запоминается глазом.</para>
    ///
    /// <para><b>Работ в сцене не требует.</b> Панели собираются кодом и
    /// вешаются детьми на <see cref="Seat"/>, ради чего тот и существует:
    /// один трансформ переносит всё место целиком, и раскладка мест
    /// продолжает ничего не знать про сумки.</para>
    /// </summary>
    public class PlayerItemsUI : MonoBehaviour
    {
        /// <summary>
        /// Порядок слотов. Фиксирован намеренно: место типа должно быть
        /// на одном и том же месте у всех игроков и во всех партиях.
        /// </summary>
        private static readonly ItemType[] SlotOrder =
        {
            ItemType.Coin, ItemType.Sword, ItemType.Armor, ItemType.MagicStick,
        };

        [Header("Своя сумка (под очками, левый низ)")]
        [SerializeField] private Vector2 _localSlotSize = new Vector2(46f, 44f);
        [SerializeField] private float _localIconSize = 32f;
        [SerializeField] private float _localFontSize = 18f;
        [SerializeField] private float _localGap = 6f;

        [Header("Сумка соперника (под его подписями)")]
        [Tooltip("Полоса намеренно низкая: при двух игроках под очками соперника " +
                 "остаётся 28 px до верха колоды, и это самый тесный случай из трёх.")]
        [SerializeField] private Vector2 _opponentSlotSize = new Vector2(32f, 24f);
        [SerializeField] private float _opponentIconSize = 20f;
        [SerializeField] private float _opponentFontSize = 12f;
        [SerializeField] private float _opponentGap = 3f;

        [Tooltip("Запасное смещение от центра места — только если подписей у места нет вовсе.")]
        [SerializeField] private Vector2 _fallbackOffset = new Vector2(0f, 11f);

        [Header("Вид")]
        [Tooltip("Прозрачность иконки типа, которого у игрока нет.")]
        [SerializeField] private float _emptyIconAlpha = 0.22f;
        [SerializeField] private Color _countColor = new Color(0.96f, 0.91f, 0.78f, 1f);

        private ClientGameView _view;

        /// <summary>Слоты одного игрока: иконка и число по типу предмета.</summary>
        private class Bag
        {
            public readonly Dictionary<ItemType, Image> Icons = new();
            public readonly Dictionary<ItemType, TMP_Text> Counts = new();
        }

        private readonly Dictionary<Player, Bag> _bags = new();

        /// <summary>Имя предмета по типу — из того же каталога, что и сама игра.</summary>
        private static readonly Dictionary<ItemType, string> ItemNames =
            GameBuilder.MakeItemTemplates().ToDictionary(i => i.Type, i => i.Name);

        public void Initialize(ClientGameView view, SeatLayout seatLayout)
        {
            Unsubscribe();
            Clear();

            _view = view;
            if (_view == null || seatLayout == null) return;

            var localSeat = seatLayout.LocalSeat;
            if (localSeat != null)
                BuildBag(_view.LocalPlayer, localSeat, isLocal: true);

            for (int i = 0; i < _view.Opponents.Count; i++)
            {
                var seat = seatLayout.GetOpponentSeat(i);
                if (seat == null) continue;
                BuildBag(_view.Opponents[i], seat, isLocal: false);
            }

            _view.OnItemAddToPlayer += HandleItemChanged;
            _view.OnItemRemovedFromPlayer += HandleItemChanged;
        }

        private void OnDestroy() => Unsubscribe();

        private void Unsubscribe()
        {
            if (_view == null) return;
            _view.OnItemAddToPlayer -= HandleItemChanged;
            _view.OnItemRemovedFromPlayer -= HandleItemChanged;
        }

        private void HandleItemChanged(Item item, Player player) => Refresh(player);

        /// <summary>
        /// Перечитать сумку игрока. Считаем по самой сумке, а не по событию:
        /// событие говорит «стало на один больше», а показать надо, сколько их
        /// теперь — и после пропущенного события число всё равно сойдётся.
        /// </summary>
        public void Refresh(Player player)
        {
            if (player == null || !_bags.TryGetValue(player, out var bag)) return;

            foreach (var type in SlotOrder)
            {
                int amount = player.ItemAmount(type);

                if (bag.Counts.TryGetValue(type, out var text) && text != null)
                    text.text = amount > 0 ? amount.ToString() : string.Empty;

                if (bag.Icons.TryGetValue(type, out var icon) && icon != null)
                {
                    var c = icon.color;
                    // Пустой слот гасим, но оставляем на месте: исчезни он —
                    // соседние съехали бы, и место типа перестало бы быть
                    // постоянным.
                    c.a = amount > 0 ? 1f : _emptyIconAlpha;
                    icon.color = c;
                }
            }
        }

        private void Clear()
        {
            foreach (var pair in _bags)
            {
                foreach (var icon in pair.Value.Icons.Values)
                    if (icon != null && icon.transform.parent != null)
                        Destroy(icon.transform.parent.gameObject);
            }
            _bags.Clear();
        }

        private void BuildBag(Player player, Seat seat, bool isLocal)
        {
            if (player == null || seat == null) return;
            if (!(seat.transform is RectTransform seatRect)) return;

            Vector2 slotSize = isLocal ? _localSlotSize : _opponentSlotSize;
            Vector2 panelSize = new Vector2(slotSize.x * SlotOrder.Length, slotSize.y);
            Vector2 offset = PlaceUnderLabels(seat, panelSize, isLocal ? _localGap : _opponentGap);

            var panel = new GameObject($"Items ({player.Name})", typeof(RectTransform));
            var panelRect = (RectTransform)panel.transform;
            panelRect.SetParent(seatRect, false);

            // Те же якоря по центру и то же «смещение от центра места», по
            // которым SeatLayout кладёт руку, стол и подписи. Иначе одно и то
            // же число значило бы у разных мест разное.
            panelRect.anchorMin = panelRect.anchorMax = panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition = offset;
            panelRect.sizeDelta = panelSize;
            panelRect.localScale = Vector3.one;
            panelRect.localRotation = Quaternion.identity;
            // Последним ребёнком: рука соперника развёрнута на 180 и подписи
            // поднимают над ней, сумка должна быть там же, наверху.
            panelRect.SetAsLastSibling();

            var bag = new Bag();

            for (int i = 0; i < SlotOrder.Length; i++)
            {
                var slotPos = new Vector2(-panelSize.x / 2f + slotSize.x * (i + 0.5f), 0f);
                BuildSlot(panelRect, SlotOrder[i], slotPos, slotSize, isLocal, bag);
            }

            _bags[player] = bag;
            Refresh(player);
        }

        /// <summary>
        /// Куда положить полосу: под нижнюю из подписей места, по иксу ника.
        ///
        /// <para>Одно правило на три разные раскладки, и потому оно смотрит на
        /// сами подписи, а не на числа из <see cref="SeatLayout"/>. У себя ник
        /// и очки стоят столбиком в левом жёлобе — полоса ложится под очки, в
        /// пустой левый низ. У соперников при трёх-четырёх игроках подписи
        /// стоят в строку по половинам места — полоса ложится под них обе. А
        /// при двух игроках единственный соперник получает жёлоб, как у себя,
        /// и снова выходит «под очками».</para>
        ///
        /// <para>Икс берётся у ника: при подписях в строку центр места
        /// приходится на промежуток между ником и очками, и полоса там висела
        /// бы сама по себе, а не под чьим-то именем.</para>
        /// </summary>
        private Vector2 PlaceUnderLabels(Seat seat, Vector2 panelSize, float gap)
        {
            var label = seat.NameText != null ? seat.NameText : seat.ScoreText;
            if (label == null) return _fallbackOffset;

            var nameRect = seat.NameText != null ? seat.NameText.rectTransform : null;
            var scoreRect = seat.ScoreText != null ? seat.ScoreText.rectTransform : null;

            float bottom = float.MaxValue;
            if (nameRect != null) bottom = Mathf.Min(bottom, BottomOf(nameRect));
            if (scoreRect != null) bottom = Mathf.Min(bottom, BottomOf(scoreRect));

            return new Vector2(AnchorX(label, panelSize.x), bottom - gap - panelSize.y / 2f);
        }

        /// <summary>
        /// Икс полосы — по <b>видимому</b> краю подписи, а не по центру её ректа.
        ///
        /// <para>Подписи в сцене выровнены по левому краю, а рект под ними
        /// вдвое шире самого имени (у соперника при трёх игроках — 300 px на
        /// ник в несколько букв). Привязка к центру ректа уводила полосу на
        /// полторы сотни пикселей правее имени, к центру места, где лежат
        /// карты. Совпадает это только у подписи, выровненной по центру, —
        /// и потому здесь спрашивается само выравнивание.</para>
        /// </summary>
        private static float AnchorX(TMP_Text label, float panelWidth)
        {
            var rect = label.rectTransform;
            float centre = rect.anchoredPosition.x;
            float half = rect.sizeDelta.x / 2f;
            var align = label.horizontalAlignment;

            if ((align & HorizontalAlignmentOptions.Right) != 0)
                return centre + half - label.margin.z - panelWidth / 2f;

            if ((align & HorizontalAlignmentOptions.Center) != 0)
                return centre;

            // Left и всё, что не названо явно: левый край ректа плюс отступ TMP.
            return centre - half + label.margin.x + panelWidth / 2f;
        }

        private static float BottomOf(RectTransform rect) =>
            rect.anchoredPosition.y - rect.sizeDelta.y / 2f;

        private void BuildSlot(RectTransform panel, ItemType type, Vector2 position,
                               Vector2 size, bool isLocal, Bag bag)
        {
            var slot = new GameObject(type.ToString(), typeof(RectTransform));
            var slotRect = (RectTransform)slot.transform;
            slotRect.SetParent(panel, false);
            slotRect.anchorMin = slotRect.anchorMax = slotRect.pivot = new Vector2(0.5f, 0.5f);
            slotRect.anchoredPosition = position;
            slotRect.sizeDelta = size;

            float iconSize = isLocal ? _localIconSize : _opponentIconSize;

            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var iconRect = (RectTransform)iconGo.transform;
            iconRect.SetParent(slotRect, false);
            iconRect.anchorMin = iconRect.anchorMax = iconRect.pivot = new Vector2(0.5f, 0.5f);
            // Иконка по центру слота, число поверх её нижнего угла. Рядом они
            // не встают: у соперника на слот всего 32 px по ширине.
            iconRect.anchoredPosition = Vector2.zero;
            iconRect.sizeDelta = new Vector2(iconSize, iconSize);

            var icon = iconGo.GetComponent<Image>();
            icon.raycastTarget = false;
            icon.preserveAspect = true;
            if (ItemNames.TryGetValue(type, out var itemName))
            {
                icon.sprite = Resources.Load<Sprite>($"ItemsImages/{itemName}");
                if (icon.sprite == null)
                    Debug.LogWarning($"[PlayerItemsUI] Нет картинки предмета «{itemName}» — " +
                                     $"ожидается Assets/Resources/ItemsImages/{itemName}.PNG.");
            }

            var textGo = new GameObject("Count", typeof(RectTransform));
            var textRect = (RectTransform)textGo.transform;
            textRect.SetParent(slotRect, false);
            textRect.anchorMin = textRect.anchorMax = textRect.pivot = new Vector2(0.5f, 0.5f);
            float badge = isLocal ? 22f : 16f;
            textRect.anchoredPosition = new Vector2(size.x / 2f - badge / 2f, -size.y / 2f + badge / 2f);
            textRect.sizeDelta = new Vector2(badge, badge);

            var text = textGo.AddComponent<TextMeshProUGUI>();
            text.fontSize = isLocal ? _localFontSize : _opponentFontSize;
            text.color = _countColor;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            text.enableWordWrapping = false;
            // Обводка: число у соперника лежит на иконке, и без неё светлая
            // цифра пропадает на светлом предмете.
            text.fontStyle = FontStyles.Bold;
            text.outlineWidth = 0.25f;
            text.outlineColor = new Color32(0, 0, 0, 255);

            bag.Icons[type] = icon;
            bag.Counts[type] = text;
        }
    }
}
