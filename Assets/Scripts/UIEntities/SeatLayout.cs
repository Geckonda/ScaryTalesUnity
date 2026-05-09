using Assets.Scripts.UIEntities.Components;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Assets.Scripts.UIEntities
{
    /// <summary>
    /// Per-opponent layout values, opt-in via <see cref="Override"/>.
    /// Default Override=false leaves the seat alone (whatever the inspector
    /// has stays). Set Override=true to apply rotation and FanLayoutGroup
    /// tuning at game start.
    /// </summary>
    [Serializable]
    public class OpponentSeatConfig
    {
        [Tooltip("MUST BE TRUE to apply the values below. Default false leaves this opponent's HandPanel/FanLayout completely alone.")]
        public bool Override = false;

        [Tooltip("Z rotation (degrees) applied to HandPanel and BeforePlayerTable when Override is true.")]
        public float HandPanelZRotation;

        [Tooltip("FanLayoutGroup.angle when Override is true (only applied if HandPanel has FanLayoutGroup).")]
        public float FanAngle = 50f;

        [Tooltip("FanLayoutGroup.verticalOffset when Override is true (only applied if FanLayoutGroup is present).")]
        public float FanVerticalOffset = -70f;
    }

    /// <summary>
    /// Activates the first N seats based on player count, places them on a
    /// circle, and (optionally, opt-in) applies per-opponent rotation /
    /// FanLayout tuning from the inspector.
    ///
    /// Seat 0 is the local player and goes to south. Opponents fill the
    /// remaining angles counterclockwise. Seat positions are always
    /// computed (so seats aren't stuck at canvas origin); rotations and
    /// FanLayout values are only written when the matching config entry's
    /// Override toggle is true.
    /// </summary>
    public class SeatLayout : MonoBehaviour
    {
        [Tooltip("Distance (canvas pixels) from the seat container's anchor to each seat anchor. Tune until seats sit at the visual edge of your play area.")]
        [SerializeField] private float _radius = 400f;

        [Tooltip("All seat slots, seat-index order. Slot 0 = local player.")]
        [SerializeField] private Seat[] _seats = new Seat[4];

        [Tooltip("Layout values for the 1 opponent in 2-player. Leave Override=false to keep your inspector values.")]
        [SerializeField] private OpponentSeatConfig[] _opponents2P = new OpponentSeatConfig[1];

        [Tooltip("Layout values for the 2 opponents in 3-player. Set Override=true on each to apply 150/-150 etc.")]
        [SerializeField] private OpponentSeatConfig[] _opponents3P = new OpponentSeatConfig[2];

        [Tooltip("Layout values for the 3 opponents in 4-player. Leave Override=false until you've tuned 4-player visuals.")]
        [SerializeField] private OpponentSeatConfig[] _opponents4P = new OpponentSeatConfig[3];

        private int _activeCount;
        private List<Seat> _activeOpponents = new();

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
            _activeCount = playerCount;
            _activeOpponents.Clear();
            if (_seats == null || _seats.Length == 0) return;

            OpponentSeatConfig[] configs = playerCount switch
            {
                2 => _opponents2P,
                3 => _opponents3P,
                4 => _opponents4P,
                _ => null,
            };

            for (int i = 0; i < _seats.Length; i++)
            {
                var seat = _seats[i];
                if (seat == null) continue;

                bool active = i < playerCount;
                seat.gameObject.SetActive(active);
                if (!active) continue;
                if (i > 0) _activeOpponents.Add(seat);

                // Always position the seat on the circle so seats aren't
                // stuck at canvas origin. Seat 0 sits at south.
                float angle = -Mathf.PI / 2f + (Mathf.PI * 2f / playerCount) * i;
                float seatX = Mathf.Cos(angle) * _radius;
                float seatY = Mathf.Sin(angle) * _radius;
                var rt = seat.transform as RectTransform;
                if (rt != null)
                    rt.anchoredPosition = new Vector2(seatX, seatY);

                // Local seat: never receives Override values.
                if (i == 0) continue;

                int opIndex = i - 1;
                if (configs == null || opIndex >= configs.Length || configs[opIndex] == null)
                    continue;
                var cfg = configs[opIndex];
                if (!cfg.Override) continue; // explicit opt-in

                // Reset the seat root rotation so the HandPanel rotation
                // we're about to write doesn't compound with leftover
                // seat-level rotation (that's where "305° = 150° + 155°
                // pre-existing" bugs came from).
                if (rt != null) rt.localRotation = Quaternion.identity;

                if (seat.HandPanel != null)
                {
                    seat.HandPanel.localRotation = Quaternion.Euler(0, 0, cfg.HandPanelZRotation);

                    var fan = seat.HandPanel.GetComponent<FanLayoutGroup>();
                    if (fan != null)
                    {
                        fan.angle = cfg.FanAngle;
                        fan.verticalOffset = cfg.FanVerticalOffset;
                    }
                }

                if (seat.BeforePlayerTable != null)
                    seat.BeforePlayerTable.localRotation = Quaternion.Euler(0, 0, cfg.HandPanelZRotation);
            }
        }
    }
}
