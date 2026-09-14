using NonsensicalKit.ScriptAnimation;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace NonsensicalKit.ScriptAnimation.Demo
{
    /// <summary>
    /// 简易轨道相机，便于观察缠膜过程（WebGL 可用）。
    /// </summary>
    public class FilmWrappingOrbitCamera : MonoBehaviour
    {
        [Header("观察目标")]
        [InspectorLabel("注视目标")]
        [Tooltip("轨道相机注视目标，一般指向货物 Cargo")]
        [SerializeField] private Transform m_target;

        [Header("距离")]
        [InspectorLabel("当前距离")]
        [Tooltip("当前相机到目标的距离（米）")]
        [SerializeField] private float m_distance = 7f;
        [InspectorLabel("最近距离")]
        [Tooltip("滚轮拉近的最近距离")]
        [SerializeField] private float m_minDistance = 3f;
        [InspectorLabel("最远距离")]
        [Tooltip("滚轮拉远的最远距离")]
        [SerializeField] private float m_maxDistance = 14f;

        [Header("角度")]
        [InspectorLabel("水平方位角")]
        [Tooltip("水平方位角（度），右键左右拖动改变")]
        [SerializeField] private float m_yaw = 35f;
        [InspectorLabel("俯仰角")]
        [Tooltip("俯仰角（度），右键上下拖动改变")]
        [SerializeField] private float m_pitch = 25f;

        [Header("手感")]
        [InspectorLabel("旋转灵敏度")]
        [Tooltip("右键旋转灵敏度")]
        [SerializeField] private float m_rotateSpeed = 0.15f;
        [InspectorLabel("缩放速度")]
        [Tooltip("滚轮缩放速度")]
        [SerializeField] private float m_zoomSpeed = 2f;

        public void SetTarget(Transform target) => m_target = target;

        private void LateUpdate()
        {
            if (m_target == null)
                return;

#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            if (mouse != null)
            {
                if (mouse.rightButton.isPressed)
                {
                    Vector2 delta = mouse.delta.ReadValue();
                    m_yaw += delta.x * m_rotateSpeed;
                    m_pitch -= delta.y * m_rotateSpeed;
                }

                float scroll = mouse.scroll.ReadValue().y * 0.01f;
                m_distance -= scroll * m_zoomSpeed;
            }
#else
            if (Input.GetMouseButton(1))
            {
                m_yaw += Input.GetAxis("Mouse X") * m_rotateSpeed * 20f;
                m_pitch -= Input.GetAxis("Mouse Y") * m_rotateSpeed * 20f;
            }

            m_distance -= Input.GetAxis("Mouse ScrollWheel") * m_zoomSpeed * 10f;
#endif

            m_pitch = Mathf.Clamp(m_pitch, 5f, 80f);
            m_distance = Mathf.Clamp(m_distance, m_minDistance, m_maxDistance);

            Quaternion rot = Quaternion.Euler(m_pitch, m_yaw, 0f);
            Vector3 targetPos = m_target.position + Vector3.up * 0.8f;
            transform.position = targetPos + rot * (Vector3.back * m_distance);
            transform.LookAt(targetPos);
        }
    }
}
