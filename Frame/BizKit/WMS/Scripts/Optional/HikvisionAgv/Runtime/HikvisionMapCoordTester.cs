using NaughtyAttributes;
using UnityEngine;

namespace HikvisionAgv
{
    /// <summary>
    /// 用海康平面坐标在场景里打点，或从场景位置反推海康坐标。
    /// NaughtyAttributes 按钮在 Inspector 编辑模式可直接点，不必进 Play。
    /// </summary>
    [AddComponentMenu("海康AGV/坐标测试 (HikvisionMapCoordTester)")]
    public sealed class HikvisionMapCoordTester : MonoBehaviour
    {
        [SerializeField] HikvisionMapFrame m_frame;

        [Header("海康坐标")]
        [SerializeField] float m_mapX;
        [SerializeField] float m_mapY;

        [Header("场景采样")]
        [Tooltip("反推时读取的场景点；为空则用本物体当前位置。")]
        [SerializeField] Transform m_sceneSample;

        [Header("Gizmo")]
        [SerializeField] Color m_gizmoColor = new Color(1f, 0.4f, 0.1f, 0.95f);
        [SerializeField] float m_gizmoRadius = 0.35f;

        [SerializeField, ReadOnly] Vector3 m_lastWorld;
        [SerializeField, ReadOnly] Vector2 m_lastMap;

        HikvisionMapFrame ResolveFrame()
        {
            if (m_frame != null)
                return m_frame;
            m_frame = GetComponent<HikvisionMapFrame>()
                      ?? GetComponentInParent<HikvisionMapFrame>();
            return m_frame;
        }

        [Button("海康坐标 → 场景标记")]
        void MarkFromHikvision()
        {
            var frame = ResolveFrame();
            if (frame == null)
            {
                Debug.LogWarning($"{name} 未指定 HikvisionMapFrame。", this);
                return;
            }

            m_lastWorld = frame.MapToWorld(m_mapX, m_mapY);
            m_lastMap = new Vector2(m_mapX, m_mapY);
            transform.position = m_lastWorld;
        }

        [Button("场景位置 → 海康坐标")]
        void SampleToHikvision()
        {
            var frame = ResolveFrame();
            if (frame == null)
            {
                Debug.LogWarning($"{name} 未指定 HikvisionMapFrame。", this);
                return;
            }

            Vector3 src = m_sceneSample != null ? m_sceneSample.position : transform.position;
            m_lastWorld = src;
            m_lastMap = frame.WorldToMap(src);
            m_mapX = m_lastMap.x;
            m_mapY = m_lastMap.y;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = m_gizmoColor;
            Gizmos.DrawSphere(transform.position, m_gizmoRadius);
            Gizmos.DrawLine(transform.position, transform.position + Vector3.up * (m_gizmoRadius * 4f));
        }
    }
}
