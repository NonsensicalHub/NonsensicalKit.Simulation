using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace NonsensicalKit.ScriptAnimation.Demo
{
    /// <summary>右键旋转，滚轮缩放。</summary>
    public class OpenBoxOrbitCamera : MonoBehaviour
    {
        [SerializeField] private Transform m_target;
        [SerializeField] private Vector3 m_targetOffset = new Vector3(0f, 0.6f, 0f);
        [SerializeField] private float m_distance = 3.2f;
        [SerializeField] private float m_minDistance = 1.5f;
        [SerializeField] private float m_maxDistance = 8f;
        [SerializeField] private float m_yaw = 35f;
        [SerializeField] private float m_pitch = 25f;
        [SerializeField] private float m_sensitivity = 0.2f;
        [SerializeField] private float m_zoomSpeed = 1.5f;

        private void LateUpdate()
        {
            if (TryOrbit(out Vector2 delta))
            {
                m_yaw += delta.x * m_sensitivity;
                m_pitch = Mathf.Clamp(m_pitch - delta.y * m_sensitivity, 5f, 80f);
            }

            float scroll = Scroll();
            if (scroll != 0f)
                m_distance = Mathf.Clamp(m_distance - scroll * m_zoomSpeed, m_minDistance, m_maxDistance);

            Vector3 focus = m_target != null ? m_target.position + m_targetOffset : m_targetOffset;
            Quaternion rot = Quaternion.Euler(m_pitch, m_yaw, 0f);
            transform.SetPositionAndRotation(focus + rot * (Vector3.back * m_distance), rot);
        }

        private static bool TryOrbit(out Vector2 delta)
        {
            delta = Vector2.zero;
#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            if (mouse == null || !mouse.rightButton.isPressed)
                return false;
            delta = mouse.delta.ReadValue();
            return true;
#else
            if (!Input.GetMouseButton(1))
                return false;
            delta = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 10f;
            return true;
#endif
        }

        private static float Scroll()
        {
#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            return mouse != null ? mouse.scroll.ReadValue().y * 0.01f : 0f;
#else
            return Input.GetAxis("Mouse ScrollWheel") * 10f;
#endif
        }
    }
}
