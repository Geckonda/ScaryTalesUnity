using TMPro;
using UnityEngine;

namespace Assets.Scripts.UIEntities
{
    /// <summary>
    /// One player's UI bundle. Bundling means a single transform repositions
    /// the whole seat (hand + name/score + before-player area) as a unit, so
    /// SeatLayout can drop seats into computed positions without each
    /// downstream UI component having to know its own coordinate.
    ///
    /// Fields are nullable — partial wiring is acceptable while iterating
    /// on scene layout. Components that consume Seat skip null fields.
    /// </summary>
    public class Seat : MonoBehaviour
    {
        public RectTransform HandPanel;
        public TMP_Text NameText;
        public TMP_Text ScoreText;
        public RectTransform BeforePlayerTable;
    }
}
