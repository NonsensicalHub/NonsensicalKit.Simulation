using NaughtyAttributes;
using NonsensicalKit.Core;
using NonsensicalKit.Core.Service;
using NonsensicalKit.Core.Service.Config;
using NonsensicalKit.Tools.CameraTool;
using UnityEngine;

public class ConfigurableCamera : NonsensicalCamera
{
    [HorizontalLine(color: EColor.Blue)]
    [SerializeField, Label("自动写入配置项")] private bool m_autoSetConfig;

    [Label("相机ID"), Tooltip("用于打包后匹配配置文件中的相机信息")]
    [SerializeField] private string CameraID;

    [Expandable] public CameraControl m_config;
    [SerializeField, Label("保存到IOCC容器中")] private bool m_setIocCamera;

    [SerializeField, Label("保存名称"), ShowIf("m_setIocCamera")]
    private string m_setIocCameraName;

    private Transform _crtFocusTarget;

    protected override void Awake()
    {
        base.Awake();
        ServiceCore.SafeGet<ConfigService>(OnLoadCompleted);
        if (!m_setIocCamera) return;
        if (string.IsNullOrEmpty(m_setIocCameraName))
        {
            Debug.LogError("保存到容器中时，请填写保存名称,");
            m_setIocCameraName = "DefaultCamera";
        }

        IOCC.Set<ConfigurableCamera>(m_setIocCameraName, this);
    }

    protected override void Update()
    {
        base.Update();

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            ExitFocus();
        }

        if (_crtFocusTarget != null)
        {
            Focus(_crtFocusTarget.transform.position);
        }
    }

    public void StartFocus(Transform target, float focusDistance = -1f)
    {
        _crtFocusTarget = target;
        if (!Mathf.Approximately(focusDistance, -1f))
            TargetDistance = focusDistance;
    }

    private void ExitFocus()
    {
        if (_crtFocusTarget != null)
        {
            _crtFocusTarget = null;
            ResetState();
            IOCC.Publish("ExitFocus");
        }
    }

    private void OnLoadCompleted(ConfigService config)
    {
        if (config.TryGetConfig<CameraControlData>(out var v))
        {
            CameraConfig a = v.cameraConfigs.Find(x => x.CameraID == CameraID);
            if (a != null)
            {
                m_minPitch = a.minPitch;
                m_maxPitch = a.maxPitch;
                m_minDistance = a.minDistance;
                m_maxDistance = a.maxDistance;
                m_moveSpeedMinZoom = a.moveSpeedMin;
                m_moveSpeedMaxZoom = a.moveSpeedMax;
                m_rotationSpeed = a.rotationSpeed;
                m_zoomSpeed = a.zoomSpeed;
                m_dragZoomSpeed = a.dragZoomSpeed;
                m_checkUI = a.checkUI;
            }
        }
    }

    private void OnValidate()
    {
        if (m_config == null) return;
        CameraConfig a = m_config.data.cameraConfigs.Find(x => x.CameraID == CameraID);
        if (a != null)
        {
            a.minPitch = m_minPitch;
            a.maxPitch = m_maxPitch;
            a.minDistance = m_minDistance;
            a.maxDistance = m_maxDistance;
            a.moveSpeedMin = m_moveSpeedMinZoom;
            a.moveSpeedMax = m_moveSpeedMaxZoom;
            a.rotationSpeed = m_rotationSpeed;
            a.zoomSpeed = m_zoomSpeed;
            a.dragZoomSpeed = m_dragZoomSpeed;
            a.checkUI = m_checkUI;
        }
        else
        {
            if (m_autoSetConfig == false) return;
            CameraConfig temp = new CameraConfig();
            temp.SetDefault();
            temp.CameraID = CameraID;
            m_config.data.cameraConfigs.Add(temp);
        }
    }
}
