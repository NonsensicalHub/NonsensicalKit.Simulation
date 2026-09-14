using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 脚本动画场景点位（取放货站、直线移动端点、机械臂途经点等）。
    /// 挂在空物体上，便于在 Scene 视图中用 Gizmo 看见并选中。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("ScriptAnimation/场景点位 (ScriptAnimPoint)")]
    public class ScriptAnimPoint : MonoBehaviour
    {
        [Header("场景 Gizmo")]
        [SerializeField] private bool m_showGizmo = true;
        [SerializeField] private bool m_showLabel = true;
        [SerializeField] private bool m_showForward = true;
        [SerializeField] private Color m_gizmoColor = new Color(1f, 0.55f, 0.15f, 0.95f);
        [SerializeField] private float m_gizmoRadius = 0.18f;
        [SerializeField] private float m_forwardLength = 0.45f;
        [SerializeField] private float m_labelHeight = 0.4f;

        public Vector3 Position => transform.position;
        public Quaternion Rotation => transform.rotation;
        public bool ShowGizmo => m_showGizmo;
        public bool ShowLabel => m_showLabel;
        public Color GizmoColor => m_gizmoColor;
        public float GizmoRadius => m_gizmoRadius;
        public float LabelHeight => m_labelHeight;

        private void OnDrawGizmos()
        {
            if (!m_showGizmo)
                return;

            Color color = m_gizmoColor;
            float radius = Mathf.Max(0.02f, m_gizmoRadius);

            Gizmos.color = color;
            Gizmos.DrawSphere(transform.position, radius);

            Gizmos.color = new Color(color.r, color.g, color.b, Mathf.Clamp01(color.a * 0.35f));
            Gizmos.DrawWireSphere(transform.position, radius * 1.35f);

            if (!m_showForward)
                return;

            Vector3 origin = transform.position;
            Vector3 tip = origin + transform.forward * Mathf.Max(0.05f, m_forwardLength);
            Gizmos.color = color;
            Gizmos.DrawLine(origin, tip);

            Vector3 dir = tip - origin;
            if (dir.sqrMagnitude <= 1e-6f)
                return;

            dir.Normalize();
            Vector3 right = Vector3.Cross(
                Mathf.Abs(Vector3.Dot(dir, Vector3.up)) > 0.95f ? Vector3.right : Vector3.up,
                dir).normalized;
            float head = Mathf.Min(0.14f, m_forwardLength * 0.35f);
            Gizmos.DrawLine(tip, tip - dir * head + right * head * 0.55f);
            Gizmos.DrawLine(tip, tip - dir * head - right * head * 0.55f);
        }
    }
}
