using UnityEngine;
using UnityEngine.EventSystems;

public class SoloCameraControl : MonoBehaviour
{
    [SerializeField, InspectorName("旋转速度"), Min(0.01f)]
    private float m_rotateSpeed = 2.5f;

    [SerializeField, InspectorName("旋转阻尼"), Min(0f)]
    private float m_rotateDamp = 0.08f;

    [SerializeField, InspectorName("平移速度"), Min(0.01f)]
    private float m_panSpeed = 0.12f;

    [SerializeField, InspectorName("缩放速度"), Min(0.01f)]
    private float m_zoomSpeed = 0.18f;

    [SerializeField, InspectorName("飞行速度"), Min(0.01f)]
    private float m_flySpeed = 20f;

    [SerializeField, InspectorName("加速倍率"), Min(1f)]
    private float m_boost = 3.5f;

    [SerializeField, InspectorName("默认注视距离"), Min(0.1f)]
    private float m_defaultDistance = 20f;

    [SerializeField, InspectorName("最小距离"), Min(0.01f)]
    private float m_minDistance = 0.5f;

    [SerializeField, InspectorName("最大距离"), Min(1f)]
    private float m_maxDistance = 10000f;

    [SerializeField, InspectorName("忽略UI上的操作")]
    private bool m_checkUI = true;

    private float m_yaw;
    private float m_pitch;
    private float m_targetYaw;
    private float m_targetPitch;
    private float m_yawVel;
    private float m_pitchVel;
    private float m_distance;
    private Vector3 m_pivot;
    private bool m_lookRotate;

    private void OnEnable()
    {
        CaptureState();
    }

    private void Update()
    {
        float mx = Input.GetAxis("Mouse X");
        float my = Input.GetAxis("Mouse Y");
        float scroll = Input.mouseScrollDelta.y;
        bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
        float boost = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? m_boost : 1f;
        bool overUI = m_checkUI && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        float dt = Time.unscaledDeltaTime;

        if (!overUI && scroll != 0f)
        {
            if (Input.GetMouseButton(1) && !alt)
                m_flySpeed = Mathf.Clamp(m_flySpeed * (scroll > 0f ? 1.1f : 1f / 1.1f), 0.1f, 500f);
            else
                Zoom(scroll * boost);
        }

        if (!overUI && alt && Input.GetMouseButton(0))
        {
            AddRotation(mx, my);
            m_lookRotate = false;
        }
        else if (!overUI && Input.GetMouseButton(2))
        {
            Pan(mx, my, boost);
        }
        else if (!overUI && alt && Input.GetMouseButton(1))
        {
            Zoom(my * 0.2f * boost);
        }
        else if (!overUI && Input.GetMouseButton(1))
        {
            AddRotation(mx, my);
            m_lookRotate = true;
        }

        DampRotation(dt);

        if (m_lookRotate)
        {
            Quaternion rot = Quaternion.Euler(m_pitch, m_yaw, 0f);
            transform.SetPositionAndRotation(transform.position, rot);
            m_pivot = transform.position + rot * Vector3.forward * m_distance;
        }
        else
        {
            ApplyOrbit();
        }

        Vector3 local = Vector3.zero;
        if (Input.GetKey(KeyCode.W)) local.z += 1f;
        if (Input.GetKey(KeyCode.S)) local.z -= 1f;
        if (Input.GetKey(KeyCode.D)) local.x += 1f;
        if (Input.GetKey(KeyCode.A)) local.x -= 1f;
        if (Input.GetKey(KeyCode.E)) local.y += 1f;
        if (Input.GetKey(KeyCode.Q)) local.y -= 1f;
        if (local.sqrMagnitude > 0f)
        {
            Vector3 move = Quaternion.Euler(m_pitch, m_yaw, 0f) * local.normalized * (m_flySpeed * boost * dt);
            transform.position += move;
            m_pivot += move;
        }
    }

    private void CaptureState()
    {
        Vector3 euler = transform.eulerAngles;
        m_pitch = m_targetPitch = euler.x > 180f ? euler.x - 360f : euler.x;
        m_yaw = m_targetYaw = euler.y;
        m_yawVel = 0f;
        m_pitchVel = 0f;
        m_lookRotate = false;

        if (Physics.Raycast(transform.position, transform.forward, out RaycastHit hit, m_maxDistance, ~0, QueryTriggerInteraction.Ignore))
        {
            m_pivot = hit.point;
            m_distance = Mathf.Clamp(hit.distance, m_minDistance, m_maxDistance);
        }
        else
        {
            m_distance = Mathf.Clamp(m_defaultDistance, m_minDistance, m_maxDistance);
            m_pivot = transform.position + transform.forward * m_distance;
        }
    }

    private void AddRotation(float mx, float my)
    {
        m_targetYaw += mx * m_rotateSpeed;
        m_targetPitch = Mathf.Clamp(m_targetPitch - my * m_rotateSpeed, -89.5f, 89.5f);
    }

    private void DampRotation(float dt)
    {
        if (m_rotateDamp <= 0f)
        {
            m_yaw = m_targetYaw;
            m_pitch = m_targetPitch;
            return;
        }

        m_yaw = Mathf.SmoothDampAngle(m_yaw, m_targetYaw, ref m_yawVel, m_rotateDamp, Mathf.Infinity, dt);
        m_pitch = Mathf.SmoothDamp(m_pitch, m_targetPitch, ref m_pitchVel, m_rotateDamp, Mathf.Infinity, dt);
    }

    private void Pan(float mx, float my, float boost)
    {
        Quaternion rot = Quaternion.Euler(m_pitch, m_yaw, 0f);
        m_pivot += (rot * Vector3.right * -mx + rot * Vector3.up * -my) * (m_distance * m_panSpeed * boost);
        ApplyOrbit();
    }

    private void Zoom(float delta)
    {
        m_distance = Mathf.Clamp(m_distance * Mathf.Exp(-delta * m_zoomSpeed), m_minDistance, m_maxDistance);
        ApplyOrbit();
    }

    private void ApplyOrbit()
    {
        Quaternion rot = Quaternion.Euler(m_pitch, m_yaw, 0f);
        transform.SetPositionAndRotation(m_pivot - rot * Vector3.forward * m_distance, rot);
    }
}
