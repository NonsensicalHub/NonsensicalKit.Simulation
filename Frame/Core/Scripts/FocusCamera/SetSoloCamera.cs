using NonsensicalKit.Core;
using UnityEngine;
#if CINEMACHINE_3
using Unity.Cinemachine;
#elif CINEMACHINE_2
using Cinemachine;
#endif


namespace Frame.Core.FocusCamera
{
#if !CINEMACHINE_2 && !CINEMACHINE_3
[DisallowMultipleComponent]
public class SetSoloCamera : MonoBehaviour
{
}
#else
[DefaultExecutionOrder(32000)]
[DisallowMultipleComponent]
public class SetSoloCamera : NonsensicalMono
{
    [SerializeField, InspectorName("自由相机")]
    private CinemachineVirtualCameraBase m_soloCamera;

    [SerializeField, InspectorName("切入时长"), Min(0f)]
    private float m_blendInDuration = 0.5f;

    [SerializeField, InspectorName("切出时长"), Min(0f)]
    private float m_blendOutDuration = 0.5f;

    [SerializeField, InspectorName("切换按键")]
    private KeyCode m_toggleKey = KeyCode.V;

    private Camera m_output;
    private SoloCameraControl m_control;
    private int m_mode;
    private float m_elapsed;
    private Vector3 m_fromPos;
    private Quaternion m_fromRot;
    private float m_fromFov;

    private void Awake()
    {
        if (m_soloCamera == null)
            m_soloCamera = GetComponent<CinemachineVirtualCameraBase>();
        m_control = m_soloCamera != null
            ? m_soloCamera.GetComponent<SoloCameraControl>()
            : GetComponent<SoloCameraControl>();
        if (m_control != null)
            m_control.enabled = false;
        Subscribe("SwitchSoloCamera", Switch);
        SyncActiveStatus();
    }

    private void OnDisable()
    {
        if (m_control != null)
            m_control.enabled = false;
        ClearSoloIfCurrent();
        m_mode = 0;
        SyncActiveStatus();
    }

    private void Update()
    {
        if (m_toggleKey != KeyCode.None && Input.GetKeyDown(m_toggleKey))
            Switch();
    }

    public void Switch()
    {
        if (m_mode == 1 || m_mode == 2)
            Exit();
        else
            Enter();
    }

    public void Enter()
    {
        if (m_soloCamera == null || m_mode == 1 || m_mode == 2 || !TryGetOutput())
            return;

        m_soloCamera.gameObject.SetActive(true);
        m_fromPos = m_soloCamera.transform.position;
        m_fromRot = m_soloCamera.transform.rotation;
        m_fromFov = GetSoloFov();
        m_elapsed = 0f;
        m_mode = 1;
        SyncActiveStatus();
        if (m_blendInDuration <= 0f)
            FinishEnter();
    }

    public void Exit()
    {
        if (m_mode == 0 || m_mode == 3 || !TryGetOutput())
            return;

        m_fromPos = m_output.transform.position;
        m_fromRot = m_output.transform.rotation;
        m_fromFov = m_output.fieldOfView;

        if (m_control != null)
            m_control.enabled = false;
        ClearSoloIfCurrent();

        m_elapsed = 0f;
        m_mode = 3;
        if (m_blendOutDuration <= 0f)
            m_mode = 0;
        SyncActiveStatus();
    }

    private void LateUpdate()
    {
        if (m_output == null || m_mode == 0 || m_mode == 2)
            return;

        if (m_mode == 1)
        {
            m_elapsed += Time.unscaledDeltaTime;
            float w = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(m_elapsed / m_blendInDuration));
            AlignSolo(
                Vector3.Lerp(m_fromPos, m_output.transform.position, w),
                Quaternion.Slerp(m_fromRot, m_output.transform.rotation, w),
                Mathf.Lerp(m_fromFov, m_output.fieldOfView, w));
            if (w >= 1f)
                FinishEnter();
        }
        else
        {
            m_elapsed += Time.unscaledDeltaTime;
            float w = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(m_elapsed / m_blendOutDuration));
            m_output.transform.SetPositionAndRotation(
                Vector3.Lerp(m_fromPos, m_output.transform.position, w),
                Quaternion.Slerp(m_fromRot, m_output.transform.rotation, w));
            m_output.fieldOfView = Mathf.Lerp(m_fromFov, m_output.fieldOfView, w);
            if (w >= 1f)
            {
                m_mode = 0;
                SyncActiveStatus();
            }
        }
    }

    private void FinishEnter()
    {
        AlignSolo(m_output.transform.position, m_output.transform.rotation, m_output.fieldOfView);
        SetSoloCameraFunc(m_soloCamera);
        m_mode = 2;
        SyncActiveStatus();
        if (m_control != null)
            m_control.enabled = true;
    }

    private void SyncActiveStatus()
    {
        // mode 1/2：切入中或已开启；mode 0/3：关闭或切出中
        IOCC.Set<bool>("SoloCameraActive", m_mode == 1 || m_mode == 2);
    }

    private void ClearSoloIfCurrent()
    {
#if CINEMACHINE_3
        if (m_soloCamera != null && ReferenceEquals(CinemachineCore.SoloCamera, m_soloCamera))
            CinemachineCore.SoloCamera = null;
#else
        if (m_soloCamera != null && ReferenceEquals(CinemachineBrain.SoloCamera, m_soloCamera))
            CinemachineBrain.SoloCamera = null;
#endif
    }

    private static void SetSoloCameraFunc(CinemachineVirtualCameraBase camera)
    {
#if CINEMACHINE_3
        CinemachineCore.SoloCamera = camera;
#else
        CinemachineBrain.SoloCamera = camera;
#endif
    }

    private void AlignSolo(Vector3 pos, Quaternion rot, float fov)
    {
        m_soloCamera.ForceCameraPosition(pos, rot);
#if CINEMACHINE_3
        if (m_soloCamera is CinemachineCamera vcam)
        {
            LensSettings lens = vcam.Lens;
            lens.FieldOfView = fov;
            vcam.Lens = lens;
        }
#else
        if (m_soloCamera is CinemachineVirtualCamera vcam)
        {
            LensSettings lens = vcam.m_Lens;
            lens.FieldOfView = fov;
            vcam.m_Lens = lens;
        }
#endif
    }

    private float GetSoloFov()
    {
#if CINEMACHINE_3
        return m_soloCamera is CinemachineCamera vcam
            ? vcam.Lens.FieldOfView
            : m_soloCamera.State.Lens.FieldOfView;
#else
        return m_soloCamera is CinemachineVirtualCamera vcam
            ? vcam.m_Lens.FieldOfView
            : m_soloCamera.State.Lens.FieldOfView;
#endif
    }

    private bool TryGetOutput()
    {
        if (m_output == null)
        {
            Camera main = Camera.main;
            CinemachineBrain brain = main != null ? main.GetComponent<CinemachineBrain>() : null;
            m_output = brain != null ? brain.OutputCamera : main;
        }

        return m_output != null;
    }
}
#endif
}