using UnityEngine;

namespace Assets.Scripts.UIEntities.Components
{
    /// <summary>
    /// When a card lands on the board, its inherited rotation from a
    /// fanned hand makes it look weird — we want played cards to sit
    /// upright on the table. So when this panel's children list changes
    /// (DOTween reparented a card in, etc.), we reset rotation and scale
    /// on any CardView children. Non-card children are left alone — that
    /// guards against accidentally stomping seats or other UI that
    /// happens to live under this panel.
    /// </summary>
    public class GameBoardPanel : MonoBehaviour
    {
        private void OnTransformChildrenChanged()
        {
            foreach (Transform child in transform)
            {
                // Only normalize CardViews; never touch other UI.
                if (child.GetComponent<CardView>() == null) continue;
                child.localRotation = Quaternion.identity;
                child.localScale = Vector3.one;
            }
        }
    }
}
