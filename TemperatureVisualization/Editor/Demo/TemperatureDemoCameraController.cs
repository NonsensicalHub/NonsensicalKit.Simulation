using UnityEngine;

namespace TemperatureVisualization.Demo
{
    /// <summary>
    /// Demo 摄像机轨道控制，右键旋转、滚轮缩放。
    /// </summary>
    public class TemperatureDemoCameraController : MonoBehaviour
    {
        [SerializeField] private Transform m_Target;
        [SerializeField] private float m_Distance = 14f;
        [SerializeField] private float m_MinDistance = 4f;
        [SerializeField] private float m_MaxDistance = 30f;
        [SerializeField] private float m_RotateSpeed = 4f;
        [SerializeField] private float m_ZoomSpeed = 2f;
        [SerializeField] private float m_PitchMin = 5f;
        [SerializeField] private float m_PitchMax = 85f;

        private float m_Yaw = 20f;
        private float m_Pitch = 20f;

        private void LateUpdate()
        {
            if (m_Target == null) return;

            if (Input.GetMouseButton(1))
            {
                m_Yaw += Input.GetAxis("Mouse X") * m_RotateSpeed;
                m_Pitch -= Input.GetAxis("Mouse Y") * m_RotateSpeed;
                m_Pitch = Mathf.Clamp(m_Pitch, m_PitchMin, m_PitchMax);
            }

            m_Distance -= Input.mouseScrollDelta.y * m_ZoomSpeed;
            m_Distance = Mathf.Clamp(m_Distance, m_MinDistance, m_MaxDistance);

            Quaternion rotation = Quaternion.Euler(m_Pitch, m_Yaw, 0f);
            Vector3 offset = rotation * new Vector3(0f, 0f, -m_Distance);
            transform.position = m_Target.position + offset;
            transform.rotation = rotation;
        }

        public void SetTarget(Transform target)
        {
            m_Target = target;
        }
    }
}
