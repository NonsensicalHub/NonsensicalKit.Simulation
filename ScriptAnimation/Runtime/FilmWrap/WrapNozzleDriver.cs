using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>只根据路径求值写手臂 / 滑架 / 膜卷，不生成膜。</summary>
    [DisallowMultipleComponent]
    public class WrapNozzleDriver : MonoBehaviour
    {
        [Header("驱动对象")]
        [InspectorLabel("货物中心")]
        [SerializeField] private Transform m_cargo;

        [InspectorLabel("转臂枢轴")]
        [SerializeField] private Transform m_armPivot;

        [InspectorLabel("升降滑架")]
        [SerializeField] private Transform m_carriage;

        [InspectorLabel("膜卷")]
        [SerializeField] private Transform m_filmRoll;

        public void SetCargo(Transform cargo) => m_cargo = cargo;

        public void Apply(Vector3 nozzleWorld, float progress, float filmRollRadius)
        {
            Vector3 center = m_cargo != null ? m_cargo.position : Vector3.zero;

            if (m_armPivot != null)
            {
                Vector3 flat = nozzleWorld - center;
                flat.y = 0f;
                if (flat.sqrMagnitude > 1e-6f)
                {
                    float yaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
                    m_armPivot.rotation = Quaternion.Euler(0f, yaw, 0f);
                }
            }

            if (m_carriage != null)
            {
                if (m_armPivot != null && m_carriage.IsChildOf(m_armPivot))
                {
                    Vector3 lp = m_carriage.localPosition;
                    lp.y = nozzleWorld.y - m_armPivot.position.y;
                    m_carriage.localPosition = lp;
                }
                else
                {
                    m_carriage.position = nozzleWorld;
                }
            }

            if (m_filmRoll != null)
            {
                float r = filmRollRadius > 0.01f ? filmRollRadius : 1f;
                m_filmRoll.localRotation = Quaternion.Euler(progress * 360f * r * 3f, 0f, 0f);
            }
        }
    }
}
