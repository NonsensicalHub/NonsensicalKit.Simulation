using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Frame.Core.UI
{
/// <summary>
/// UI 连线：一端接到指定 Rect 的边缘（沿中心→目标方向裁掉中心到边缘段），
/// 另一端跟随世界物体在相机上的投影位置。
/// 挂在作为“线”的 RectTransform 上（建议配合 Image，Pivot 会在运行时设为 (0, 0.5)）。
/// </summary>
[ExecuteAlways]
[DefaultExecutionOrder(32000)] // 尽量晚于 Cinemachine，避免投影用到上一帧相机姿态
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
[AddComponentMenu("UI/UI World Line Connector")]
public class UIWorldLineConnector : MonoBehaviour
{
    [Header("锚点")]
    [SerializeField]
    [Tooltip("UI 端固定锚点（Rect）")]
    private RectTransform m_UiAnchor;

    [SerializeField]
    [Tooltip("世界端目标物体")]
    private Transform m_WorldTarget;

    [Header("相机 / 容器")]
    [SerializeField]
    [Tooltip("用于世界坐标转屏幕坐标的相机；为空时自动取 Canvas 相机 / Camera.main")]
    private Camera m_WorldCamera;

    [SerializeField]
    [Tooltip("连线所在坐标系的父 Rect；为空时使用自身父节点")]
    private RectTransform m_LineParent;

    [Header("外观")]
    [SerializeField]
    [Tooltip("线宽（RectTransform 高度）")]
    private float m_LineWidth = 2f;

    [SerializeField]
    [Tooltip("相对 UI Rect 几何中心的本地偏移")]
    private Vector2 m_UiAnchorOffset = Vector2.zero;

    [SerializeField]
    [Tooltip("相对世界目标的世界空间偏移")]
    private Vector3 m_WorldOffset = Vector3.zero;

    [SerializeField]
    [Tooltip("目标位于相机后方时隐藏连线")]
    private bool m_HideWhenBehindCamera = true;

    [SerializeField]
    [Tooltip("屏幕外时是否仍然绘制")]
    private bool m_DrawWhenOffscreen = true;

    private RectTransform _lineRect;
    private Canvas _rootCanvas;
    private Graphic _graphic;
    private bool _visible = true;

#if UNITY_EDITOR
    private bool _subscribedEditorUpdate;
#endif

    public RectTransform UiAnchor
    {
        get => m_UiAnchor;
        set => m_UiAnchor = value;
    }

    public Transform WorldTarget
    {
        get => m_WorldTarget;
        set => m_WorldTarget = value;
    }

    public Camera WorldCamera
    {
        get => m_WorldCamera;
        set => m_WorldCamera = value;
    }

    public float LineWidth
    {
        get => m_LineWidth;
        set => m_LineWidth = value;
    }

    private void Awake()
    {
        CacheRefs();
    }

    private void OnEnable()
    {
        CacheRefs();
        Refresh();

#if UNITY_EDITOR
        SubscribeEditorUpdate();
#endif
    }

    private void OnDisable()
    {
#if UNITY_EDITOR
        UnsubscribeEditorUpdate();
#endif
    }

    private void OnValidate()
    {
        CacheRefs();
        Refresh();
    }

    private void LateUpdate()
    {
        // 编辑模式下由 EditorApplication.update 驱动
        if (!Application.isPlaying)
        {
            return;
        }

        Refresh();
    }

#if UNITY_EDITOR
    private void SubscribeEditorUpdate()
    {
        if (Application.isPlaying || _subscribedEditorUpdate)
        {
            return;
        }

        EditorApplication.update += EditorUpdate;
        _subscribedEditorUpdate = true;
    }

    private void UnsubscribeEditorUpdate()
    {
        if (!_subscribedEditorUpdate)
        {
            return;
        }

        EditorApplication.update -= EditorUpdate;
        _subscribedEditorUpdate = false;
    }

    private void EditorUpdate()
    {
        if (this == null || Application.isPlaying || !isActiveAndEnabled)
        {
            return;
        }

        Refresh();
    }
#endif

    /// <summary>
    /// 运行时设置两端目标。
    /// </summary>
    public void SetTargets(RectTransform uiAnchor, Transform worldTarget)
    {
        m_UiAnchor = uiAnchor;
        m_WorldTarget = worldTarget;
        Refresh();
    }

    public void Refresh()
    {
        if (_lineRect == null)
        {
            CacheRefs();
        }

        if (_lineRect == null || m_UiAnchor == null || m_WorldTarget == null)
        {
            SetLineVisible(false);
            return;
        }

        RectTransform parent = m_LineParent != null
            ? m_LineParent
            : _lineRect.parent as RectTransform;
        if (parent == null)
        {
            SetLineVisible(false);
            return;
        }

        Camera worldCam = ResolveWorldCamera();
        if (worldCam == null)
        {
            SetLineVisible(false);
            return;
        }

        Camera uiCam = GetUiCamera();

        // 世界端：相机投影到屏幕，再落到 parent 平面
        Vector3 worldPos = m_WorldTarget.position + m_WorldOffset;
        Vector3 viewport = worldCam.WorldToViewportPoint(worldPos);

        bool behind = viewport.z <= 0f;
        if (behind && m_HideWhenBehindCamera)
        {
            SetLineVisible(false);
            return;
        }

        if (!m_DrawWhenOffscreen &&
            (viewport.x < 0f || viewport.x > 1f || viewport.y < 0f || viewport.y > 1f))
        {
            SetLineVisible(false);
            return;
        }

        Vector3 screenPos = worldCam.WorldToScreenPoint(worldPos);
        if (!RectTransformUtility.ScreenPointToWorldPointInRectangle(
                parent, screenPos, uiCam, out Vector3 endWorldOnCanvas))
        {
            SetLineVisible(false);
            return;
        }

        // UI 端：在 Rect 本地空间求「中心→目标」与矩形边缘的交点，从边缘起画（去掉中心到边缘那段）
        Vector2 uiCenterLocal = m_UiAnchor.rect.center + m_UiAnchorOffset;
        Vector3 endInUi3 = m_UiAnchor.InverseTransformPoint(endWorldOnCanvas);
        Vector2 endInUi = new Vector2(endInUi3.x, endInUi3.y);

        if (m_UiAnchor.rect.Contains(endInUi))
        {
            // 目标投影落在 Rect 内部，无需连线
            SetLineVisible(false);
            return;
        }

        if (!TryGetRayRectEdgePoint(m_UiAnchor.rect, uiCenterLocal, endInUi - uiCenterLocal, out Vector2 edgeInUi))
        {
            SetLineVisible(false);
            return;
        }

        Vector3 edgeWorld = m_UiAnchor.TransformPoint(edgeInUi);
        Vector3 startLocal3 = parent.InverseTransformPoint(edgeWorld);
        Vector2 startLocal = new Vector2(startLocal3.x, startLocal3.y);

        Vector3 endLocal3 = parent.InverseTransformPoint(endWorldOnCanvas);
        Vector2 endLocal = new Vector2(endLocal3.x, endLocal3.y);

        Vector2 delta = endLocal - startLocal;
        float length = delta.magnitude;
        if (length < 0.01f)
        {
            SetLineVisible(false);
            return;
        }

        // sizeDelta 在自身本地空间，需抵消 localScale，保证视觉长度等于 parent 本地距离
        float scaleX = Mathf.Abs(_lineRect.localScale.x);
        float scaleY = Mathf.Abs(_lineRect.localScale.y);
        if (scaleX < 1e-5f)
        {
            scaleX = 1f;
        }

        if (scaleY < 1e-5f)
        {
            scaleY = 1f;
        }

        SetLineVisible(true);
        _lineRect.pivot = new Vector2(0f, 0.5f);
        _lineRect.anchorMin = _lineRect.anchorMax = new Vector2(0.5f, 0.5f);
        // localPosition 相对父节点 Pivot，与 InverseTransformPoint 结果同一空间
        _lineRect.localPosition = new Vector3(startLocal.x, startLocal.y, startLocal3.z);
        _lineRect.sizeDelta = new Vector2(length / scaleX, m_LineWidth / scaleY);
        _lineRect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            Canvas.ForceUpdateCanvases();
            EditorApplication.QueuePlayerLoopUpdate();
        }
#endif
    }

    /// <summary>
    /// 从 origin 沿 direction 射出，取与轴对齐 Rect 边界的最近正向交点。
    /// </summary>
    private static bool TryGetRayRectEdgePoint(Rect rect, Vector2 origin, Vector2 direction, out Vector2 edgePoint)
    {
        edgePoint = origin;
        if (direction.sqrMagnitude < 1e-8f)
        {
            return false;
        }

        float tBest = float.PositiveInfinity;
        const float epsilon = 1e-4f;

        if (Mathf.Abs(direction.x) > epsilon)
        {
            TryEdgeX(rect.xMin);
            TryEdgeX(rect.xMax);
        }

        if (Mathf.Abs(direction.y) > epsilon)
        {
            TryEdgeY(rect.yMin);
            TryEdgeY(rect.yMax);
        }

        if (float.IsInfinity(tBest))
        {
            return false;
        }

        edgePoint = origin + direction * tBest;
        return true;

        void TryEdgeX(float x)
        {
            float t = (x - origin.x) / direction.x;
            if (t <= epsilon)
            {
                return;
            }

            float y = origin.y + t * direction.y;
            if (y < rect.yMin - epsilon || y > rect.yMax + epsilon)
            {
                return;
            }

            if (t < tBest)
            {
                tBest = t;
            }
        }

        void TryEdgeY(float y)
        {
            float t = (y - origin.y) / direction.y;
            if (t <= epsilon)
            {
                return;
            }

            float x = origin.x + t * direction.x;
            if (x < rect.xMin - epsilon || x > rect.xMax + epsilon)
            {
                return;
            }

            if (t < tBest)
            {
                tBest = t;
            }
        }
    }

    private Camera ResolveWorldCamera()
    {
        if (IsUsableCamera(m_WorldCamera))
        {
            return m_WorldCamera;
        }

        // Screen Space - Camera：优先与 Canvas 同一台相机，避免投影与 UI 平面不一致
        if (_rootCanvas == null)
        {
            CacheRefs();
        }

        if (_rootCanvas != null &&
            _rootCanvas.renderMode == RenderMode.ScreenSpaceCamera &&
            IsUsableCamera(_rootCanvas.worldCamera))
        {
            return _rootCanvas.worldCamera;
        }

        if (IsUsableCamera(Camera.main))
        {
            return Camera.main;
        }

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            // 仅在没有可用游戏相机时，才回退 Scene 视图相机
            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView != null && IsUsableCamera(sceneView.camera))
            {
                return sceneView.camera;
            }
        }
#endif

        return null;
    }

    private static bool IsUsableCamera(Camera cam)
    {
        return cam != null && cam.isActiveAndEnabled;
    }

    private void CacheRefs()
    {
        _lineRect = transform as RectTransform;
        if (_graphic == null)
        {
            _graphic = GetComponent<Graphic>();
        }

        if (_lineRect != null)
        {
            var canvas = _lineRect.GetComponentInParent<Canvas>();
            _rootCanvas = canvas != null ? canvas.rootCanvas : null;
        }
    }

    private Camera GetUiCamera()
    {
        if (_rootCanvas == null)
        {
            CacheRefs();
        }

        if (_rootCanvas == null || _rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
        {
            return null;
        }

        if (IsUsableCamera(_rootCanvas.worldCamera))
        {
            return _rootCanvas.worldCamera;
        }

        return ResolveWorldCamera();
    }

    private void SetLineVisible(bool visible)
    {
        if (_visible == visible)
        {
            return;
        }

        _visible = visible;

        if (_graphic != null)
        {
            _graphic.enabled = visible;
            return;
        }

        // 无 Graphic 时用缩放隐藏，避免 SetActive 导致脚本自身停止更新
        if (_lineRect != null)
        {
            _lineRect.localScale = visible ? Vector3.one : Vector3.zero;
        }
    }
}
}
