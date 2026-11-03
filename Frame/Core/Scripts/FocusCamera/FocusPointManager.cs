using System;
using System.Collections.Generic;
using NaughtyAttributes;
using NonsensicalKit.Core;
using NonsensicalKit.Core.DagLogicNode;
using NonsensicalKit.Tools;
#if UNITY_2022_3_OR_NEWER
using Unity.Cinemachine;
#else
using Cinemachine;
#endif
using UnityEngine;

/// <summary>
/// 注视焦点管理。注视相机在 Inspector 配置；高亮由外部注入 <see cref="HighlightHandler"/>（未赋值则跳过）。
/// </summary>
public class FocusPointManager : NonsensicalMono
{
    [SerializeField] private bool m_autoExit;
    [SerializeField, ShowIf("m_autoExit")] private string m_exitNodeName;
    [SerializeField] private FocusPointConfig[] m_points;

    [Tooltip("注视相机预制体（需含 ConfigurableCamera）。运行时 Instantiate；也可用下方 m_camera 直接指定场景实例。")]
    [SerializeField] private GameObject m_focusCameraPrefab;
    [Tooltip("可选：直接指定场景中的 ConfigurableCamera；非空时不再实例化预制体。")]
    [SerializeField] private ConfigurableCamera m_camera;

    /// <summary>
    /// 外部注入：对目标物体开启高亮，返回关闭时调用的清理委托；未赋值或返回 null 则跳过该目标。
    /// Sample 的 HighlightPlusFocusHighlight 会在 OnEnable 时自动赋值。
    /// </summary>
    public Func<GameObject, Action> HighlightHandler { get; set; }

    private bool _hasFocus;
    private CinemachineVirtualCameraBase _cinemachineCamera;
    private FocusPointConfig _focusedPoint;
    private Dictionary<string, FocusPointConfig> _pointLookup;
    private Coroutine _closeCameraRoutine;
    private bool _destroyed;
    private static bool _missingHighlightWarned;

    [Button("计算数据")]
    private void CalculationParameters()
    {
        if (m_points == null) return;
        foreach (var point in m_points)
            point?.CalculationCenter(true);
    }

    private void Awake()
    {
        if (m_autoExit)
            Subscribe((int)DagLogicNodeEnum.NodeExit, m_exitNodeName, AutoExit);

        Subscribe<string>("focusEquipment", FocusEquipment);
        Subscribe("ExitFocus", ExitFocus);

        if (!EnsureCamera())
            return;

        _cinemachineCamera = m_camera.GetComponent<CinemachineVirtualCameraBase>();
        BuildPointLookup();
    }

    protected override void OnDestroy()
    {
        _destroyed = true;
        CancelCloseCameraSchedule();
        HideAllHighlights();
        base.OnDestroy();
    }

    private void BuildPointLookup()
    {
        int count = m_points?.Length ?? 0;
        _pointLookup = new Dictionary<string, FocusPointConfig>(count);
        if (m_points == null) return;

        foreach (var point in m_points)
        {
            if (point == null) continue;

            point.CalculationCenter(false);

            if (string.IsNullOrEmpty(point.m_ID))
            {
                Debug.LogWarning("[FocusPointManager] 存在未配置 ID 的注视点，已跳过字典注册。", this);
                continue;
            }

            if (_pointLookup.ContainsKey(point.m_ID))
            {
                Debug.LogWarning($"[FocusPointManager] 重复的注视点 ID「{point.m_ID}」，后出现的将覆盖先前项。", this);
            }

            _pointLookup[point.m_ID] = point;
        }
    }

    /// <summary>解析或实例化注视相机；失败时 Warning 并返回 false（后续注视 no-op）。</summary>
    private bool EnsureCamera()
    {
        if (m_camera != null)
            return true;

        if (m_focusCameraPrefab == null)
        {
            Debug.LogWarning(
                "[FocusPointManager] 未配置注视相机（m_focusCameraPrefab / m_camera），注视功能将跳过。",
                this);
            return false;
        }

        var instance = Instantiate(m_focusCameraPrefab);
        instance.name = m_focusCameraPrefab.name;
        m_camera = instance.GetComponentInChildren<ConfigurableCamera>(true);
        if (m_camera == null)
        {
            Debug.LogWarning(
                "[FocusPointManager] 注视相机预制体上未找到 ConfigurableCamera，注视功能将跳过。",
                this);
            Destroy(instance);
            return false;
        }

        m_camera.gameObject.SetActive(false);
        return true;
    }

    private void FocusEquipment(string id)
    {
        if (m_camera == null)
            return;
        if (string.IsNullOrEmpty(id) || _pointLookup == null || !_pointLookup.TryGetValue(id, out FocusPointConfig temp))
            return;

        if (_focusedPoint != null && _focusedPoint != temp)
            _focusedPoint.ClearHighlights();

        _hasFocus = true;
        _focusedPoint = temp;

        ApplyHighlight(temp);

        m_camera.gameObject.SetActive(true);
        m_camera.StartFocus(temp.m_FocusCenter, temp.m_FocusDistance);
    }

    private void ApplyHighlight(FocusPointConfig point)
    {
        if (point == null || !point.m_GetHighlightEffect)
            return;

        if (HighlightHandler == null)
        {
            WarnMissingHighlight();
            return;
        }

        point.ApplyHighlights(HighlightHandler);
    }

    private void WarnMissingHighlight()
    {
        if (_missingHighlightWarned)
            return;
        _missingHighlightWarned = true;
        Debug.LogWarning(
            "[FocusPointManager] HighlightHandler 未赋值，高亮将跳过。" +
            "请在外部赋值，或挂 Sample 的 HighlightPlusFocusHighlight 自动注入。", this);
    }

    private void AutoExit()
    {
        m_camera?.gameObject.SetActive(false);
        _hasFocus = false;
        CloseFocusCamera();
        HideAllHighlights();
        Publish("RestoreEnvironmentObj");
    }

    private void ExitFocus()
    {
        CloseFocusCamera();
        _hasFocus = false;
        HideAllHighlights();
        Publish("RestoreEnvironmentObj");
    }

    private void CloseFocusCamera()
    {
        if (!_hasFocus) return;

        if (_cinemachineCamera != null)
            _cinemachineCamera.Priority = -5;

        CancelCloseCameraSchedule();
        if (isActiveAndEnabled)
            _closeCameraRoutine = StartCoroutine(CloseCameraDelayed());
    }

    private System.Collections.IEnumerator CloseCameraDelayed()
    {
        yield return new WaitForSeconds(0.5f);
        _closeCameraRoutine = null;
        if (_destroyed)
            yield break;

        m_camera?.gameObject.SetActive(false);
        if (_cinemachineCamera != null)
            _cinemachineCamera.Priority = 10;
    }

    private void CancelCloseCameraSchedule()
    {
        if (_closeCameraRoutine == null)
            return;

        StopCoroutine(_closeCameraRoutine);
        _closeCameraRoutine = null;
    }

    private void HideAllHighlights()
    {
        if (m_points != null)
        {
            foreach (var point in m_points)
                point?.ClearHighlights();
        }

        _focusedPoint = null;
    }
}

[Serializable]
public class FocusPointConfig
{
    public string m_ID;
    public bool m_NeedCalculationCenter;
    public bool m_GetHighlightEffect;
    public Transform[] m_FocusPoints;
    public Vector3 m_Offset;
    [HideInInspector] public Transform m_FocusCenter;
    [HideInInspector] public float m_FocusDistance = -1f;
    [HideInInspector] public Bounds m_Bounds;

    [NonSerialized] private List<Action> _highlightUndos;

    /// <summary>对每个注视点物体调用 handler，缓存返回的清理委托。</summary>
    public void ApplyHighlights(Func<GameObject, Action> handler)
    {
        ClearHighlights();
        if (!m_GetHighlightEffect || handler == null || m_FocusPoints == null)
            return;

        _highlightUndos = new List<Action>(m_FocusPoints.Length);
        for (int i = 0; i < m_FocusPoints.Length; i++)
        {
            var t = m_FocusPoints[i];
            if (t == null) continue;
            var undo = handler(t.gameObject);
            if (undo != null)
                _highlightUndos.Add(undo);
        }
    }

    public void ClearHighlights()
    {
        if (_highlightUndos == null) return;
        for (int i = 0; i < _highlightUndos.Count; i++)
        {
            try { _highlightUndos[i]?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
        }
        _highlightUndos = null;
    }

    public void CalculationCenter(bool forceCalculationCenter = false)
    {
        if (m_FocusPoints == null || m_FocusPoints.Length == 0) return;

        bool hasBounds = false;
        m_Bounds = default;
        foreach (var child in m_FocusPoints)
        {
            if (child == null) continue;

            Bounds box = child.BoundingBox();
            if (!hasBounds)
            {
                m_Bounds = box;
                hasBounds = true;
            }
            else
            {
                m_Bounds.Encapsulate(box);
            }
        }

        if (!hasBounds) return;

        if (m_NeedCalculationCenter || forceCalculationCenter)
        {
            Camera mainCam = Camera.main;
            if (mainCam != null)
            {
                float fov = mainCam.fieldOfView * Mathf.Deg2Rad;
                if (Screen.width < Screen.height)
                    fov = Camera.VerticalToHorizontalFieldOfView(fov, mainCam.aspect);

                Vector3 size = m_Bounds.size;
                float diagonal = Mathf.Sqrt(size.x * size.x + size.y * size.y + size.z * size.z);
                m_FocusDistance = (0.6f * diagonal) / Mathf.Tan(fov * 0.5f);
            }

            var np = new GameObject("CalculationCenter")
            {
                transform =
                {
                    position = m_Bounds.center + m_Offset
                }
            };
            np.transform.SetParent(m_FocusPoints[0], false);
            m_FocusCenter = np.transform;
        }
        else
        {
            m_FocusCenter = m_FocusPoints[0];
        }
    }
}
