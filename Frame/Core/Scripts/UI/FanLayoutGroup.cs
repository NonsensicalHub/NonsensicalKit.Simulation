using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 将子节点沿扇形弧线均匀分布的布局组件。
/// 角度约定：0° 朝上（+Y），正角度默认逆时针。
/// </summary>
[AddComponentMenu("Layout/Fan Layout Group")]
[DisallowMultipleComponent]
public class FanLayoutGroup : LayoutGroup
{
    public enum ChildRotationMode
    {
        [Tooltip("不修改子节点旋转，保留原有角度")]
        KeepOriginal = 0,
        [Tooltip("子节点顶部朝向扇形外侧")]
        FaceOutward = 1,
        [Tooltip("子节点顶部朝向扇形内侧")]
        FaceInward = 2,
    }

    [SerializeField]
    [Tooltip("扇形半径（子节点中心到布局中心的距离）")]
    private float m_Radius = 200f;

    [SerializeField]
    [Tooltip("起始角度（度）。0 为上方，正值逆时针")]
    private float m_StartAngle = -60f;

    [SerializeField]
    [Tooltip("扇形张开总角度（度）")]
    private float m_SpreadAngle = 120f;

    [SerializeField]
    [Tooltip("相对父节点中心的偏移")]
    private Vector2 m_CenterOffset = Vector2.zero;

    [SerializeField]
    [Tooltip("子节点旋转方式：KeepOriginal 不改变旋转")]
    private ChildRotationMode m_ChildRotationMode = ChildRotationMode.FaceOutward;

    [SerializeField]
    [Tooltip("子节点额外旋转偏移（度），KeepOriginal 时无效")]
    private float m_ChildRotationOffset;

    [SerializeField]
    [Tooltip("为 true 时按顺时针排布")]
    private bool m_Clockwise;

    [SerializeField]
    [Tooltip("仅一个子节点时，是否放在扇形中点")]
    private bool m_CenterSingleChild = true;

    public float radius
    {
        get => m_Radius;
        set => SetProperty(ref m_Radius, value);
    }

    public float startAngle
    {
        get => m_StartAngle;
        set => SetProperty(ref m_StartAngle, value);
    }

    public float spreadAngle
    {
        get => m_SpreadAngle;
        set => SetProperty(ref m_SpreadAngle, value);
    }

    public Vector2 centerOffset
    {
        get => m_CenterOffset;
        set => SetProperty(ref m_CenterOffset, value);
    }

    public ChildRotationMode childRotationMode
    {
        get => m_ChildRotationMode;
        set => SetProperty(ref m_ChildRotationMode, value);
    }

    public float childRotationOffset
    {
        get => m_ChildRotationOffset;
        set => SetProperty(ref m_ChildRotationOffset, value);
    }

    private bool DrivesChildRotation =>
        m_ChildRotationMode != ChildRotationMode.KeepOriginal;

    public bool clockwise
    {
        get => m_Clockwise;
        set => SetProperty(ref m_Clockwise, value);
    }

    public bool centerSingleChild
    {
        get => m_CenterSingleChild;
        set => SetProperty(ref m_CenterSingleChild, value);
    }

    public override void CalculateLayoutInputHorizontal()
    {
        base.CalculateLayoutInputHorizontal();
        float size = GetPreferredFanExtent(0);
        SetLayoutInputForAxis(size, size, -1, 0);
    }

    public override void CalculateLayoutInputVertical()
    {
        float size = GetPreferredFanExtent(1);
        SetLayoutInputForAxis(size, size, -1, 1);
    }

    public override void SetLayoutHorizontal()
    {
        SetChildrenAlongFan();
    }

    public override void SetLayoutVertical()
    {
        SetChildrenAlongFan();
    }

    private float GetPreferredFanExtent(int axis)
    {
        float maxChild = 0f;
        for (int i = 0; i < rectChildren.Count; i++)
        {
            RectTransform child = rectChildren[i];
            float preferred = axis == 0
                ? LayoutUtility.GetPreferredSize(child, 0)
                : LayoutUtility.GetPreferredSize(child, 1);
            if (preferred <= 0f)
                preferred = axis == 0 ? child.rect.width : child.rect.height;
            if (preferred > maxChild)
                maxChild = preferred;
        }

        // 粗略包围：直径 + 最大子尺寸 + padding，保证 ContentSizeFitter 可用
        float diameter = Mathf.Abs(m_Radius) * 2f + maxChild;
        return diameter + (axis == 0 ? padding.horizontal : padding.vertical);
    }

    private void SetChildrenAlongFan()
    {
        int count = rectChildren.Count;
        if (count == 0)
            return;

        Vector2 origin = GetLayoutOrigin();

        for (int i = 0; i < count; i++)
        {
            RectTransform child = rectChildren[i];
            float angle = GetChildAngle(i, count);
            float rad = angle * Mathf.Deg2Rad;

            // 0° = +Y，正角度逆时针
            Vector2 direction = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
            Vector2 position = origin + direction * m_Radius;

            // 不驱动 SizeDelta，保留子节点可手动调整的宽高
            DrivenTransformProperties driven =
                DrivenTransformProperties.Anchors |
                DrivenTransformProperties.AnchoredPosition |
                DrivenTransformProperties.Pivot;
            if (DrivesChildRotation)
                driven |= DrivenTransformProperties.Rotation;
            m_Tracker.Add(this, child, driven);

            child.anchorMin = child.anchorMax = new Vector2(0.5f, 0.5f);
            child.pivot = new Vector2(0.5f, 0.5f);
            child.anchoredPosition = position;

            if (DrivesChildRotation)
            {
                float z = m_ChildRotationMode == ChildRotationMode.FaceInward
                    ? -angle + 180f + m_ChildRotationOffset
                    : -angle + m_ChildRotationOffset;
                child.localEulerAngles = new Vector3(0f, 0f, z);
            }
        }
    }

    private Vector2 GetLayoutOrigin()
    {
        Rect rect = rectTransform.rect;
        float x = Mathf.Lerp(rect.xMin + padding.left, rect.xMax - padding.right, 0.5f);
        float y = Mathf.Lerp(rect.yMin + padding.bottom, rect.yMax - padding.top, 0.5f);

        // 父节点以中心为锚点时，anchoredPosition 相对中心；这里换算为相对中心的偏移
        Vector2 centerLocal = new Vector2(rect.center.x, rect.center.y);
        return new Vector2(x, y) - centerLocal + m_CenterOffset;
    }

    private float GetChildAngle(int index, int count)
    {
        float spread = m_SpreadAngle;
        float start = m_StartAngle;

        if (m_Clockwise)
            spread = -spread;

        if (count == 1)
            return m_CenterSingleChild ? start + spread * 0.5f : start;

        float t = index / (float)(count - 1);
        return start + spread * t;
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        m_Radius = Mathf.Max(0f, m_Radius);
    }
#endif
}
