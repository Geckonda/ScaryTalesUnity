using Assets.Scripts.UIEntities.Components;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Assets.Scripts.UIEntities
{
    /// <summary>
    /// One seat's screen layout: where the seat sits and how its own widgets
    /// are arranged inside it. Everything is an explicit number rather than
    /// something derived from a formula — a polar/circular model does not
    /// survive contact with a 16:9 screen where the deck, discard pile and
    /// common table already own fixed rectangles.
    ///
    /// Coordinates are canvas pixels relative to the table centre, and the
    /// child offsets are relative to <see cref="Position"/>.
    /// </summary>
    [Serializable]
    public class SeatSlot
    {
        [Tooltip("Inspector readability only — never used by the layout.")]
        public string Label;

        [Tooltip("Seat position, in canvas pixels, relative to the table centre.")]
        public Vector2 Position;

        public Vector2 HandSize;
        public Vector2 HandOffset;

        [Tooltip("Rotate the hand fan 180 degrees. True for seats along the top edge.")]
        public bool FlipHand;

        [Tooltip("Draw the hand behind the before-player cards. False for the local seat, where the fan should cover the cards he has put down; true for opponents, whose played cards are the informative ones.")]
        public bool HandBehindTable;

        public float FanAngle;
        public float FanVerticalOffset;

        [Tooltip("The before-player area, where cards this player has put down live.")]
        public Vector2 TableSize;
        public Vector2 TableOffset;

        public Vector2 NameOffset;
        public Vector2 ScoreOffset;
        public Vector2 LabelSize;

        [Tooltip("Масштаб карт в руке этого места. Берётся из SeatLayout._cardScale и одинаков у всех мест: рука оппонента должна выглядеть как рука, а не как карты на столе. В полосу сверху она помещается за счёт того, что часть карты уходит за край экрана.")]
        public float CardScale;

        [Tooltip("Масштаб карт в зоне перед игроком. Тот же SeatLayout._cardScale — на столе все карты одного размера, будь то колода, общий стол, рука или баффы перед игроком.")]
        public float TableCardScale;
    }

    /// <summary>
    /// Activates the first N seats and writes their complete layout from a
    /// slot table — one table per player count.
    ///
    /// Only the hand panel is ever rotated (180 degrees for seats along the
    /// top edge). The seat root, the before-player area and the name/score
    /// labels stay unrotated, which is what keeps the labels where the slot
    /// says they are instead of scattering them around the seat.
    ///
    /// Whatever the seat GameObjects carry in the scene is overwritten at
    /// Apply() time — deliberately, since the scene values had drifted apart
    /// (opponent seats were clones with name and score stacked on the same
    /// pixel, the local seat still carried pre-Seat absolute offsets).
    ///
    /// Leave the inspector arrays empty to use the built-in defaults; fill
    /// one (via the "Fill defaults" context menu) to take over that count.
    /// </summary>
    public class SeatLayout : MonoBehaviour
    {
        [Tooltip("All seat slots, seat-index order. Slot 0 = local player.")]
        [SerializeField] private Seat[] _seats = new Seat[4];

        [Header("Slot tables — leave empty to use built-in defaults")]
        [SerializeField] private SeatSlot[] _slots2P;
        [SerializeField] private SeatSlot[] _slots3P;
        [SerializeField] private SeatSlot[] _slots4P;

        [Header("Общее для всех мест")]
        [Tooltip("Масштаб КАЖДОЙ карты у мест: и в руках, и в зонах перед игроками. Колода, слот дня/ночи, сброс и общий стол рисуют карту один к одному, поэтому 1 означает «на столе всё одного размера» — ради этого поле и одно на всех. Числа раскладки посчитаны под 1; выше ~1.08 карты перед игроком дотянутся до общего стола. Крутится живьём: поменять и вызвать Preview N players из контекстного меню. Внимание: число попадает во встроенные умолчания; если массивы слотов выше уже заполнены через «Fill defaults», правьте их, а не это поле.")]
        [Range(0.3f, 2f)]
        [SerializeField] private float _cardScale = 1f;

        [Tooltip("Писать в лог фактический размер карты в руке каждого места при старте партии. Включайте, когда кажется, что у кого-то карты крупнее: строка отвечает на это числами, а глазу веер из наложенных карт легко врёт.")]
        [SerializeField] private bool _logCardSizes;

        // Размер карты в префабе. Используется только для диагностической
        // строки в логе; сама раскладка читает его из rect карты.
        private static readonly Vector2 CardSize = new(150f, 250f);

        private readonly List<Seat> _activeOpponents = new();
        private static readonly Vector2 Centered = new(0.5f, 0.5f);

        public Seat LocalSeat => _seats != null && _seats.Length > 0 ? _seats[0] : null;

        public Seat GetOpponentSeat(int opponentIndex)
        {
            int seatIndex = opponentIndex + 1;
            if (_seats == null || seatIndex < 0 || seatIndex >= _seats.Length) return null;
            return _seats[seatIndex];
        }

        public IReadOnlyList<Seat> ActiveOpponentSeats => _activeOpponents;

        public void Apply(int playerCount)
        {
            _activeOpponents.Clear();
            if (_seats == null || _seats.Length == 0 || playerCount <= 0) return;

            var slots = SlotsFor(playerCount);

            for (int i = 0; i < _seats.Length; i++)
            {
                var seat = _seats[i];
                if (seat == null) continue;

                bool active = i < playerCount;
                seat.gameObject.SetActive(active);
                if (!active) continue;
                if (i > 0) _activeOpponents.Add(seat);

                if (slots != null && i < slots.Length && slots[i] != null)
                    LayOutSeat(seat, slots[i]);
            }

            if (_logCardSizes) LogCardSizes(playerCount, slots);
        }

        /// <summary>
        /// Пишет фактический размер карты в руке каждого места — так, как её
        /// увидит игрок: размер префаба * масштаб раскладки * общий масштаб
        /// цепочки родителей (lossyScale, в него входит и CanvasScaler).
        ///
        /// Нужна ровно затем, чтобы «рука оппонента крупнее моей» решалось
        /// числами, а не на глаз: если строки для своего места и для
        /// оппонента разойдутся, разница где-то в цепочке родителей, и видно
        /// будет сразу, у кого именно.
        /// </summary>
        private void LogCardSizes(int playerCount, SeatSlot[] slots)
        {
            if (slots == null) return;

            var report = new System.Text.StringBuilder("[SeatLayout] Размер карты в руке: ");
            for (int i = 0; i < _seats.Length && i < playerCount && i < slots.Length; i++)
            {
                var seat = _seats[i];
                var slot = slots[i];
                if (seat?.HandPanel == null || slot == null) continue;

                float chain = seat.HandPanel.lossyScale.x;
                float w = CardSize.x * slot.CardScale * chain;
                float h = CardSize.y * slot.CardScale * chain;
                report.Append(i == 0 ? "своё " : $"{seat.name} ");
                report.Append($"{w:0}x{h:0} (scale {slot.CardScale:0.##}, цепочка {chain:0.###}); ");
            }
            Debug.Log(report.ToString());
        }

        /// <summary>
        /// Inspector wins when it has been filled in; otherwise fall back to
        /// the built-in table. The fallback matters because Unity leaves a
        /// newly added array field null on components that were serialized
        /// before the field existed.
        /// </summary>
        private SeatSlot[] SlotsFor(int playerCount)
        {
            var configured = playerCount switch
            {
                2 => _slots2P,
                3 => _slots3P,
                4 => _slots4P,
                _ => null,
            };
            if (configured != null && configured.Length >= playerCount)
                return configured;

            return DefaultSlots(playerCount);
        }

        private void LayOutSeat(Seat seat, SeatSlot slot)
        {
            if (seat.transform is RectTransform seatRect)
            {
                seatRect.anchorMin = Centered;
                seatRect.anchorMax = Centered;
                seatRect.pivot = Centered;
                seatRect.anchoredPosition = slot.Position;
                // The seat root never rotates. Rotating it is what threw the
                // labels to a different place on every seat.
                seatRect.localRotation = Quaternion.identity;
                seatRect.localScale = Vector3.one;
            }

            Place(seat.HandPanel, slot.HandOffset, slot.HandSize, slot.FlipHand ? 180f : 0f);
            Place(seat.BeforePlayerTable, slot.TableOffset, slot.TableSize, 0f);
            Place(seat.NameText != null ? seat.NameText.rectTransform : null,
                  slot.NameOffset, slot.LabelSize, 0f);
            Place(seat.ScoreText != null ? seat.ScoreText.rectTransform : null,
                  slot.ScoreOffset, slot.LabelSize, 0f);

            ConfigureHand(seat.HandPanel, slot);
            EnsureCardTable(seat.BeforePlayerTable, slot.TableCardScale);
            ApplyDrawOrder(seat, slot);

            // The layout groups cache rect.width, and we just changed it.
            Rebuild(seat.HandPanel);
            Rebuild(seat.BeforePlayerTable);
        }

        /// <summary>
        /// Pins a seat widget to the seat centre and writes its full local
        /// transform. Re-anchoring matters: the scene had children anchored
        /// to different seat corners (0,0 on the local seat, 0,1 on the
        /// opponents), which made the same offset mean different things.
        /// </summary>
        private static void Place(RectTransform rt, Vector2 offset, Vector2 size, float localZ)
        {
            if (rt == null) return;

            rt.anchorMin = Centered;
            rt.anchorMax = Centered;
            rt.pivot = Centered;
            rt.anchoredPosition = offset;
            rt.sizeDelta = size;
            // Scale lives on the layout group, never on the panel transform,
            // so it can't compound with itself.
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.Euler(0f, 0f, localZ);
        }

        /// <summary>
        /// uGUI draws siblings in order, so the last child wins. The scene
        /// happened to list BeforePlayerTable after HandPanel, which put the
        /// cards a player has put down on top of his own fan.
        ///
        /// Successive SetAsLastSibling calls spell out the final order, and
        /// the labels go last so nothing can ever cover them.
        /// </summary>
        private static void ApplyDrawOrder(Seat seat, SeatSlot slot)
        {
            if (slot.HandBehindTable)
            {
                Raise(seat.HandPanel);
                Raise(seat.BeforePlayerTable);
            }
            else
            {
                Raise(seat.BeforePlayerTable);
                Raise(seat.HandPanel);
            }

            Raise(seat.NameText != null ? seat.NameText.rectTransform : null);
            Raise(seat.ScoreText != null ? seat.ScoreText.rectTransform : null);
        }

        private static void Raise(RectTransform rt)
        {
            if (rt != null) rt.SetAsLastSibling();
        }

        private static void ConfigureHand(RectTransform handPanel, SeatSlot slot)
        {
            if (handPanel == null) return;

            var fan = handPanel.GetComponent<FanLayoutGroup>();
            if (fan != null)
            {
                fan.angle = slot.FanAngle;
                fan.verticalOffset = slot.FanVerticalOffset;
                fan.scale = slot.CardScale;
                // The panel itself carries the 180 degree flip, so the fan
                // always curves the same way in its own local space.
                fan.fanUpwards = true;
            }

            // The local seat uses the flat row layout instead of a fan.
            var row = handPanel.GetComponent<HandLayoutGroup>();
            if (row != null)
                row.scale = slot.CardScale;
        }

        /// <summary>
        /// The before-player area uses the same layout as the common table:
        /// duplicate cards stacked, groups centred, card rotation and scale
        /// normalized — none of which GridLayoutGroup did. Cards arrive from
        /// a fanned hand still carrying its rotation.
        ///
        /// The seats carry CardTableLayout in the scene; the grid removal
        /// below only self-heals a seat that was wired before that swap.
        /// It has to be DestroyImmediate: plain Destroy lands at end of
        /// frame, and AddComponent refuses a second LayoutGroup for as long
        /// as the old one is alive — disabling it is not enough.
        /// </summary>
        private static void EnsureCardTable(RectTransform table, float cardScale)
        {
            if (table == null) return;

            var grid = table.GetComponent<GridLayoutGroup>();
            if (grid != null) DestroyImmediate(grid);

            var cardTable = table.GetComponent<CardTableLayout>();
            if (cardTable == null)
                cardTable = table.gameObject.AddComponent<CardTableLayout>();

            if (cardTable == null)
            {
                Debug.LogError($"[SeatLayout] '{table.name}' already carries another " +
                               "LayoutGroup, so CardTableLayout could not be added. " +
                               "Remove it in the scene.");
                return;
            }

            cardTable.scale = cardScale;
        }

        private static void Rebuild(RectTransform rt)
        {
            if (rt != null) LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
        }

        // ---- Built-in defaults ----
        //
        // Derived from the scene's fixed furniture, in canvas pixels relative
        // to the table centre (x within +-960, y within +-540):
        //   deck          x [-935,-785]  y [ 155, 405]
        //   time-of-day   x [-935,-785]  y [-290, -40]
        //   discard       x [ 775, 925]  y [-290, -40]
        //   common table  x [-525, 525]  y [ -69, 181]
        // which leaves a ~355px strip along the top for the opponents, and
        // the original (known-good) bottom bands for the local player.
        //
        // Карта — 150x250, pivot внизу по центру. ВСЕ зоны рисуют её одного
        // размера, и это требование, а не совпадение: колода (DeckSlot),
        // слот дня/ночи и общий стол (GameBoardPanel) держат карту один к
        // одному, сброс её и вовсе уничтожает по прилёте, а руки и зоны
        // перед игроками берут общий _cardScale. При s=1 везде 150x250.
        //
        // Раньше руки были 1.3, а зоны перед игроками 0.7-0.85 — стол читался
        // как несколько разных колод. Уравнено 2026-08-31 по просьбе владельца.
        //
        // Как считается веер. FanLayoutGroup ставит карту так, что её pivot
        // (нижняя кромка) оказывается на 125*s выше центра панели — панель
        // перевёрнута на 180, и карты свисают от pivot ВНИЗ на 250*s. Значит
        // веер занимает [центр панели - 125*s, центр панели + 125*s], и
        // видно из него ровно то, что попало ниже верхнего края экрана.
        //
        // ВАЖНО для любых пересчётов: повёрнутая карта тянется от своего
        // pivot дальше, чем прямая — её дальний угол уходит на
        // 250*s*cos(угол) + 75*s*sin(угол). В веере (22.5 градуса) это 260
        // против 250. Пренебрежение этой десяткой и было причиной того, что
        // карты перед игроком налегали на руку при, казалось бы, сходящейся
        // арифметике.
        //
        // Верхнюю полосу делят двое, и делят её плотно: от карт общего стола
        // (верх 181) до края экрана 359 px, из них 250 забирает карта перед
        // игроком и по 10 уходит на зазоры — вееру достаётся 89. Поэтому он
        // и уведён за край сильнее, чем на половину.
        //
        // Бюджет верхней полосы при s=1 (не пересекается ни с чем):
        //   веер оппонента      y [451, 540]   видно 89 из 250 px (35%)
        //   ник и очки          y [494, 537]   поверх веера — так было и раньше
        //   карты перед игроком y [191, 441]   карта 150x250, по 10 px зазора
        //   общий стол          y [-69, 181]
        //
        // Внизу так же: своя рука дотягивается до -340, зона перед своим
        // игроком идёт от -330, между ними те же 10 px.
        //
        // Предел роста _cardScale — примерно 1.04: дальше зазоры съедаются и
        // карты перед игроком упираются в общий стол. Вниз запас только растёт.
        //
        // Стопка одинаковых карт растёт ВВЕРХ от нижней кромки зоны (см.
        // CardTableLayout: каждая следующая на 20*scale выше), поэтому нижняя
        // граница зоны не зависит от числа карт — вниз, к общему столу, она
        // не расползается ни при каком их количестве.
        //
        // These are a calculated starting point, not a pixel-perfect layout —
        // use the context menus below to materialize and then nudge them.

        private SeatSlot LocalSlot() => new()
        {
            Label = "local (bottom)",
            Position = new Vector2(0f, -430f),
            HandSize = new Vector2(1500f, 220f),
            // Рука опущена на 20 px: без этого её верх приходился на y=-320,
            // а зона перед игроком доходит до -330 — свои же баффы уходили
            // под собственные карты. Теперь верх руки на -340, между ними
            // 10 px. Ценой того, что низ карт уходит за нижний край сильнее,
            // но низ карты — самая бесполезная её часть.
            HandOffset = new Vector2(0f, -20f),
            FlipHand = false,
            HandBehindTable = false,
            FanAngle = 50f,
            FanVerticalOffset = 0f,
            TableSize = new Vector2(1200f, 250f),
            // Зона висит на 10 px ниже, чем раньше: её карты начинались на
            // y=-70, а карты общего стола заканчиваются на y=-69, то есть
            // ряды соприкасались. Теперь между ними 11 px.
            TableOffset = new Vector2(0f, 225f),
            NameOffset = new Vector2(-850f, 60f),
            ScoreOffset = new Vector2(-850f, 5f),
            LabelSize = new Vector2(200f, 50f),
            // Оба масштаба — общий _cardScale. В этом весь смысл одного поля:
            // «на столе все карты одного размера» становится свойством кода,
            // а не совпадением нескольких чисел в разных местах.
            CardScale = _cardScale,
            TableCardScale = _cardScale,
        };

        /// <summary>
        /// A seat along the top edge. The strip runs from y=185 (just above
        /// the common table) to y=540 — 355px for labels, hand and the
        /// before-player area.
        ///
        /// <para><b>Рука оппонента — того же размера, что все прочие карты,
        /// и на две трети за краем экрана.</b> Полосы на полный веер и на
        /// карты перед игроком одновременно не хватает: карта 250 px, вся
        /// полоса 355. Раньше это решали уменьшением веера до 0.6 — и он
        /// переставал читаться как рука; потом — уводом половины карты за
        /// край, но тогда картам перед игроком доставалось лишь 175 px, и
        /// они были мельче остальных. Раз размер должен быть общим, платит
        /// веер: рука узнаётся и по трети карты, а вот бафф, нарисованный
        /// не в масштабе, читается как другая карта.</para>
        ///
        /// <para>Ник и очки лежат ПОВЕРХ веера (их поднимает ApplyDrawOrder).
        /// Так было и до этой правки — старый веер занимал y [381,531] и
        /// точно так же заходил под подписи. Развести их по вертикали
        /// невозможно: в 355 px не помещаются три полосы, а по горизонтали
        /// веер (330 px при s=1) не оставляет места по краям ни при 3, ни при
        /// 4 игроках.</para>
        ///
        /// Labels are derived from the seat width so two neighbouring seats
        /// can never write over each other.
        /// </summary>
        private SeatSlot TopSlot(string label, float x, float width) => new()
        {
            Label = label,
            // Точка отсчёта места; сама рука сдвинута от неё вверх HandOffset.
            Position = new Vector2(x, 456f),
            HandSize = new Vector2(width, 115f),
            // Центр панели на y=586, то есть на 46 px ВЫШЕ верхнего края
            // экрана. Карта висит на pivot, который на 125*s выше центра
            // панели (711), и свисает от него вниз.
            //
            // Вниз она тянется НЕ на 250*s, а дальше: карта в вееере
            // повёрнута, и её дальний угол уходит на 250*cos + 75*sin — при
            // 22.5 градусах это 260 против 250. Те самые 10 px разницы и
            // были причиной жалобы «карты перед игроком налегают на руку»:
            // расчёт по прямой карте их не видел. Нижняя точка веера — 451.
            //
            // Двигаем панель, а не FanVerticalOffset, чтобы сохранить его
            // смысл «0 == веер по центру панели»; на самой панели ничего не
            // нарисовано, только раскладка.
            HandOffset = new Vector2(0f, 130f),
            FlipHand = true,
            HandBehindTable = true,
            FanAngle = 45f,
            FanVerticalOffset = 0f,                   // 0 == centred, see FanLayoutGroup
            // Зона ровно в карту высотой: верх на y=441, низ на 191 — по
            // 10 px и до веера сверху, и до карт общего стола снизу.
            TableSize = new Vector2(width, 250f),
            TableOffset = new Vector2(0f, -140f),
            NameOffset = new Vector2(-width / 4f, 60f),    // y [494, 537]
            ScoreOffset = new Vector2(width / 4f, 60f),
            LabelSize = new Vector2(width / 2f - 10f, 43f),
            CardScale = _cardScale,
            TableCardScale = _cardScale,
        };

        private SeatSlot[] DefaultSlots(int playerCount)
        {
            switch (playerCount)
            {
                case 2:
                    // One opponent gets the full width. Its labels move to
                    // the left gutter, mirroring the local seat, since there
                    // is no neighbour to collide with — и заодно единственный
                    // случай, где подписи вообще не задевают веер.
                    var across = TopSlot("opponent (top)", 0f, 1500f);
                    // Taller labels than the corner seats, so they sit a bit
                    // lower to stay inside the canvas (top edge is y=540).
                    across.NameOffset = new Vector2(-850f, 57f);   // y [488, 538]
                    across.ScoreOffset = new Vector2(-850f, 2f);   // y [433, 483]
                    across.LabelSize = new Vector2(200f, 50f);
                    return new[] { LocalSlot(), across };

                case 3:
                    // x +-450 with width 620 spans [-760,-140] and [140,760],
                    // clear of the deck (x <= -785) and discard (x >= 775).
                    return new[]
                    {
                        LocalSlot(),
                        TopSlot("opponent (top-right)", 450f, 620f),
                        TopSlot("opponent (top-left)", -450f, 620f),
                    };

                case 4:
                    // Места уже, а веер — нет: при radius=0 все карты растут
                    // из одной точки, так что его ширина (330 px при s=1) от
                    // числа игроков не зависит. Веера у x=-520/0/520 занимают
                    // [-685,-355], [-165,165], [355,685] — между ними по 190 px.
                    // Зоне перед игроком 480 px хватает на три разные карты в
                    // полный размер (3*150 + 2*5 = 460); с четвёртой
                    // CardTableLayout сжимает их внутри зоны, а не вываливает
                    // наружу — это плата за общий размер карты.
                    return new[]
                    {
                        LocalSlot(),
                        TopSlot("opponent (top-right)", 520f, 480f),
                        TopSlot("opponent (top-centre)", 0f, 480f),
                        TopSlot("opponent (top-left)", -520f, 480f),
                    };

                default:
                    return null;
            }
        }

#if UNITY_EDITOR
        // Apply now — works in Play Mode, so the visual pass doesn't need a
        // session restart per tweak.
        [ContextMenu("Preview 2 players")] private void Preview2() => Apply(2);
        [ContextMenu("Preview 3 players")] private void Preview3() => Apply(3);
        [ContextMenu("Preview 4 players")] private void Preview4() => Apply(4);

        [ContextMenu("Fill defaults into inspector (2 players)")]
        private void FillDefaults2() { _slots2P = DefaultSlots(2); MarkDirty(); }

        [ContextMenu("Fill defaults into inspector (3 players)")]
        private void FillDefaults3() { _slots3P = DefaultSlots(3); MarkDirty(); }

        [ContextMenu("Fill defaults into inspector (4 players)")]
        private void FillDefaults4() { _slots4P = DefaultSlots(4); MarkDirty(); }

        private void MarkDirty()
        {
            UnityEditor.EditorUtility.SetDirty(this);
            if (!Application.isPlaying)
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
        }
#endif
    }
}
