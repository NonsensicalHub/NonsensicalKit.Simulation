using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 每隔一段时间切换目标显隐，执行指定次数后停止。
    /// 建议挂在父物体上，通过「目标」控制子物体；若目标为本物体，隐藏时改用 Renderer.enabled，避免 SetActive 停掉本组件。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Script Animation/间隔显隐切换")]
    public class BlinkVisibility : MonoBehaviour
    {
        [Header("目标")]
        [Tooltip("被切换显隐的对象；为空则使用自身。")]
        [InspectorLabel("目标")]
        [SerializeField] private GameObject m_target;

        [Header("参数")]
        [Min(0f)]
        [Tooltip("两次切换之间的间隔秒数")]
        [InspectorLabel("间隔（秒）")]
        [SerializeField] private float m_interval = 0.5f;

        [Min(0)]
        [Tooltip("切换次数；0 表示不切换")]
        [InspectorLabel("切换次数")]
        [SerializeField] private int m_toggleCount = 4;

        [Min(0f)]
        [Tooltip("开始播放后、第一次切换前的等待秒数")]
        [InspectorLabel("初始延迟（秒）")]
        [SerializeField] private float m_initialDelay;

        [Tooltip("播放开始时是否先设为可见")]
        [InspectorLabel("起始可见")]
        [SerializeField] private bool m_startVisible = true;

        [Tooltip("启用时在 Start 自动播放")]
        [InspectorLabel("开始时播放")]
        [SerializeField] private bool m_playOnStart = true;

        [Header("运行时")]
        [InspectorLabel("播放中")]
        [SerializeField] private bool m_isPlaying;

        [InspectorLabel("已切换次数")]
        [SerializeField] private int m_toggledCount;

        float m_timer;
        bool m_visible;

        /// <summary>目标（未指定时为自身）。</summary>
        public GameObject Target => m_target != null ? m_target : gameObject;

        public float Interval => m_interval;
        public int ToggleCount => m_toggleCount;
        public bool IsPlaying => m_isPlaying;
        public int ToggledCount => m_toggledCount;
        public bool IsVisible => m_visible;

        private void Start()
        {
            if (m_playOnStart)
                Play();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            m_interval = Mathf.Max(0f, m_interval);
            m_toggleCount = Mathf.Max(0, m_toggleCount);
            m_initialDelay = Mathf.Max(0f, m_initialDelay);
        }
#endif

        private void Update()
        {
            if (!m_isPlaying)
                return;

            m_timer -= Time.deltaTime;
            if (m_timer > 0f)
                return;

            ToggleOnce();
            if (m_isPlaying)
                m_timer = m_interval;
        }

        /// <summary>按当前参数从头播放。</summary>
        [ContextMenu("播放")]
        public void Play()
        {
            m_toggledCount = 0;
            m_isPlaying = m_toggleCount > 0;
            SetVisible(m_startVisible);

            if (!m_isPlaying)
                return;

            // 有初始延迟则先等延迟再切；否则等满一个间隔再切
            m_timer = m_initialDelay > 0f ? m_initialDelay : m_interval;
        }

        /// <summary>停止切换，保留当前显隐状态。</summary>
        [ContextMenu("停止")]
        public void Stop()
        {
            m_isPlaying = false;
            m_timer = 0f;
        }

        void ToggleOnce()
        {
            SetVisible(!m_visible);
            m_toggledCount++;
            if (m_toggledCount >= m_toggleCount)
                Stop();
        }

        void SetVisible(bool visible)
        {
            m_visible = visible;
            GameObject go = Target;
            if (go == null)
                return;

            // 对本物体 SetActive(false) 会停掉 Update，改用 Renderer
            if (go == gameObject)
            {
                var renderers = go.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i] != null)
                        renderers[i].enabled = visible;
                }
                return;
            }

            if (go.activeSelf != visible)
                go.SetActive(visible);
        }
    }
}
