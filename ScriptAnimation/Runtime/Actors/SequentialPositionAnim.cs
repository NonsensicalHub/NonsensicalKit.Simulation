using System;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 依次瞬移换位：路点间隔/控制对象均在本组件配置，用 <see cref="SequentialPositionClip"/> 驱动。
    /// Clip 播放前结束后对控制对象隐藏；播放中显示并跳到对应路点。
    /// </summary>
    [AddComponentMenu("ScriptAnimation/依次换位 (SequentialPositionAnim)")]
    public class SequentialPositionAnim : ScriptAnimActor
    {
        [Header("控制对象")]
        [Tooltip("被移动且 SetActive 显隐的对象；为空则使用自身。")]
        [InspectorLabel("控制对象")]
        [SerializeField] private Transform m_controlTarget;

        [Header("路点")]
        [Tooltip("依次瞬移到的场景路点；次数列表长度")]
        [InspectorLabel("路点列表")]
        [SerializeField] private Transform[] m_waypoints = Array.Empty<Transform>();

        [Header("间隔")]
        [Tooltip("每个路点的停留秒数；不足时用「默认间隔」补齐，多余忽略")]
        [InspectorLabel("间隔列表（秒）")]
        [SerializeField] private float[] m_intervals = Array.Empty<float>();

        [Min(0f)]
        [Tooltip("Intervals 缺项时使用的停留秒数")]
        [InspectorLabel("默认间隔（秒）")]
        [SerializeField] private float m_defaultInterval = 1f;

        [Header("运行时")]
        [InspectorLabel("当前可见")]
        [SerializeField] private bool m_visible;

        Transform m_drivenControl;
        bool m_restVisible;
        bool m_hasRest;

        /// <summary>控制对象（未指定时为自身）。</summary>
        public Transform ControlTarget => m_controlTarget != null ? m_controlTarget : transform;

        public Transform[] Waypoints => m_waypoints;
        public float[] Intervals => m_intervals;
        public float DefaultInterval => m_defaultInterval;
        public bool IsVisible => m_visible;

        public void CaptureRestIfNeeded()
        {
            if (m_hasRest)
                return;
            m_restVisible = m_visible;
            m_hasRest = true;
        }

        public void RevertToRest()
        {
            if (!m_hasRest)
                return;
            SamplePose(m_restVisible, null);
            m_hasRest = false;
        }

        public void ClearRest() => m_hasRest = false;

        /// <summary>按当前路点间隔估算的总时长（秒）；未配置或全 0 时为 0。</summary>
        public float EstimatedDuration => SequentialPositionSampler.EstimateDuration(this);

        private void Awake()
        {
            Hide();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            m_defaultInterval = Mathf.Max(0f, m_defaultInterval);
        }
#endif

        /// <summary>
        /// 采样写入：切换控制对象时先隐藏上一个；visible 时显示并可选写入世界坐标。
    /// </summary>
        public void SamplePose(bool visible, Vector3? worldPosition)
        {
            Transform control = ControlTarget;
            if (control == null)
                return;

            if (m_drivenControl != null && m_drivenControl != control)
                SetActive(m_drivenControl.gameObject, false);

            m_drivenControl = control;
            SetActive(control.gameObject, visible);
            m_visible = visible;

            if (visible && worldPosition.HasValue)
                control.position = worldPosition.Value;
        }

        /// <summary>隐藏当前（或默认）控制对象。</summary>
        public void Hide()
        {
            if (m_drivenControl != null)
            {
                SetActive(m_drivenControl.gameObject, false);
                m_drivenControl = null;
            }
            else
            {
                Transform control = ControlTarget;
                if (control != null)
                    SetActive(control.gameObject, false);
            }

            m_visible = false;
        }

        public void SetVisible(bool visible)
        {
            SamplePose(visible, null);
        }

        public void SetWorldPosition(Vector3 worldPosition)
        {
            Transform control = ControlTarget;
            if (control != null)
                control.position = worldPosition;
        }

        static void SetActive(GameObject target, bool visible)
        {
            if (target != null && target.activeSelf != visible)
                target.SetActive(visible);
        }
    }
}
