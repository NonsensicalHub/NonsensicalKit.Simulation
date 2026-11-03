using NonsensicalKit.Core;
using UnityEngine;
#if UNITY_2022_3_OR_NEWER
using Unity.Cinemachine;
#else
using Cinemachine;
#endif

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
    }

    private void OnDisable()
    {
        if (m_control != null)
            m_control.enabled = false;
        ClearSoloIfCurrent();
        m_mode = 0;
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
                m_mode = 0;
        }
    }

    private void FinishEnter()
    {
        AlignSolo(m_output.transform.position, m_output.transform.rotation, m_output.fieldOfView);
        SetSoloCameraFunc(m_soloCamera);
        m_mode = 2;
        if (m_control != null)
            m_control.enabled = true;
    }

    private void ClearSoloIfCurrent()
    {
        if (m_soloCamera != null && ReferenceEquals(CinemachineCore.SoloCamera, m_soloCamera))
            CinemachineCore.SoloCamera = null;
    }

    private static void SetSoloCameraFunc(CinemachineVirtualCameraBase camera)
    {
        CinemachineCore.SoloCamera = camera;
    }

    private void AlignSolo(Vector3 pos, Quaternion rot, float fov)
    {
        m_soloCamera.ForceCameraPosition(pos, rot);
#if UNITY_2022_3_OR_NEWER
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
#if UNITY_2022_3_OR_NEWER
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
