using NonsensicalKit.ScriptAnimation;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation.Demo
{
    /// <summary>
    /// 五轴示例：IK 目标在路径点间平滑移动，配合 <see cref="RobotArm5Anim"/> 运行时跟随。
    /// </summary>
    [AddComponentMenu("")]
    public class RobotArm5RigDemoDriver : MonoBehaviour
    {
        [SerializeField] private RobotArm5Anim m_arm;
        [SerializeField] private Transform[] m_waypoints;
        [SerializeField] private float m_moveSpeed = 1.0f;
        [SerializeField] private float m_pauseAtWaypoint = 0.4f;

        private int m_fromIndex;
        private int m_toIndex;
        private float m_segmentT;
        private float m_pauseTimer;

        private void Start()
        {
            if (m_arm == null || m_waypoints == null || m_waypoints.Length < 2)
                return;

            if (!m_arm.EnsureRuntimeSetup())
                return;

            if (m_arm.IkTarget == null)
                return;

            m_fromIndex = 0;
            m_toIndex = 1;
            m_segmentT = 0f;
            m_arm.IkTarget.position = m_waypoints[0].position;
            m_arm.TrySolveIkToTarget();
        }

        private void Update()
        {
            if (m_arm == null || m_arm.IkTarget == null || m_waypoints == null || m_waypoints.Length < 2)
                return;

            if (m_pauseTimer > 0f)
            {
                m_pauseTimer -= Time.deltaTime;
                return;
            }

            Transform from = m_waypoints[m_fromIndex];
            Transform to = m_waypoints[m_toIndex];
            if (from == null || to == null)
                return;

            float dist = Vector3.Distance(from.position, to.position);
            float step = dist > 1e-4f ? (m_moveSpeed * Time.deltaTime) / dist : 1f;
            m_segmentT = Mathf.Clamp01(m_segmentT + step);
            m_arm.IkTarget.position = Vector3.Lerp(from.position, to.position, m_segmentT);

            if (m_segmentT < 1f)
                return;

            m_segmentT = 0f;
            m_pauseTimer = m_pauseAtWaypoint;
            m_fromIndex = m_toIndex;
            m_toIndex = (m_toIndex + 1) % m_waypoints.Length;
        }
    }
}
