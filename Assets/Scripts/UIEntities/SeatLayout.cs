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

        [Tooltip("Card scale in this seat's hand. Opponents have to shrink: the strip above the common table is ~355px and a card is 250px tall.")]
        public float CardScale;

        [Tooltip("Card scale in the before-player area. Separate from CardScale because the local player's hand is enlarged (1.3) while the cards he puts down are not.")]
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
        // the original (known-good) bottom bands for the local player:
        //   local hand    y [-540,-324]
        //   local table   y [-322, -72]
        //
        // These are a calculated starting point, not a pixel-perfect layout —
        // use the context menus below to materialize and then nudge them.

        private static SeatSlot LocalSlot() => new()
        {
            Label = "local (bottom)",
            Position = new Vector2(0f, -430f),
            HandSize = new Vector2(1500f, 220f),
            HandOffset = Vector2.zero,
            FlipHand = false,
            // The local fan reaches up into his own before-player band; it
            // must cover those cards, not hide behind them.
            HandBehindTable = false,
            FanAngle = 50f,
            FanVerticalOffset = 0f,
            TableSize = new Vector2(1200f, 250f),
            TableOffset = new Vector2(0f, 235f),
            NameOffset = new Vector2(-850f, 60f),
            ScoreOffset = new Vector2(-850f, 5f),
            LabelSize = new Vector2(200f, 50f),
            // The hand is enlarged; the cards he puts down are not — that is
            // how the known-good bottom bands looked.
            CardScale = 1.3f,
            TableCardScale = 1f,
        };

        /// <summary>
        /// A seat along the top edge. The strip runs from y=185 (just above
        /// the common table) to y=540 — 355px for labels, hand and the
        /// before-player area.
        ///
        /// The fan is drawn *behind* the before-player cards, so the two are
        /// allowed to overlap: the hand only has to show enough card backs
        /// to read the hand size, while the played cards — the informative
        /// ones — get the lion's share of the strip at near full size.
        ///
        /// Labels are derived from the seat width so two neighbouring seats
        /// can never write over each other.
        /// </summary>
        private static SeatSlot TopSlot(string label, float x, float width,
                                        float handScale, float tableScale) => new()
        {
            Label = label,
            Position = new Vector2(x, 456f),          // hand centre
            HandSize = new Vector2(width, 115f),
            HandOffset = Vector2.zero,
            FlipHand = true,
            HandBehindTable = true,
            FanAngle = 45f,
            FanVerticalOffset = 0f,                   // 0 == centred, see FanLayoutGroup
            TableSize = new Vector2(width, 215f),     // y [185, 400]
            TableOffset = new Vector2(0f, -164f),
            NameOffset = new Vector2(-width / 4f, 60f),    // y [494, 537]
            ScoreOffset = new Vector2(width / 4f, 60f),
            LabelSize = new Vector2(width / 2f - 10f, 43f),
            CardScale = handScale,
            TableCardScale = tableScale,
        };

        private static SeatSlot[] DefaultSlots(int playerCount)
        {
            switch (playerCount)
            {
                case 2:
                    // One opponent gets the full width. Its labels move to
                    // the left gutter, mirroring the local seat, since there
                    // is no neighbour to collide with.
                    var across = TopSlot("opponent (top)", 0f, 1500f, 0.6f, 0.85f);
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
                        TopSlot("opponent (top-right)", 450f, 620f, 0.6f, 0.85f),
                        TopSlot("opponent (top-left)", -450f, 620f, 0.6f, 0.85f),
                    };

                case 4:
                    // Narrower seats, so the table scale drops to keep a few
                    // distinct cards side by side within 480px.
                    return new[]
                    {
                        LocalSlot(),
                        TopSlot("opponent (top-right)", 520f, 480f, 0.5f, 0.75f),
                        TopSlot("opponent (top-centre)", 0f, 480f, 0.5f, 0.75f),
                        TopSlot("opponent (top-left)", -520f, 480f, 0.5f, 0.75f),
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
