using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 唯一进度源。播放、滑条、重置都只改 Progress，再同步喷嘴与膜带。
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10)]
    public class WrapProgressController : MonoBehaviour
    {
        [Header("驱动对象")]
        [InspectorLabel("缠绕路径")]
        [SerializeField] private WrapPath m_path;

        [InspectorLabel("喷嘴驱动")]
        [SerializeField] private WrapNozzleDriver m_nozzle;

        [InspectorLabel("膜带网格")]
        [SerializeField] private WrapRibbonMesher m_mesher;

        [Header("播放")]
        [InspectorLabel("开始时自动播放")]
        [SerializeField] private bool m_playOnStart = true;

        [InspectorLabel("开始时启用缠膜")]
        [SerializeField] private bool m_wrapOnStart = true;

        [InspectorLabel("一轮耗时(秒")]
        [SerializeField] private float m_duration = 14f;

        [InspectorLabel("循环")]
        [SerializeField] private bool m_loop = true;

        private float _progress;
        private bool _playing;
        private bool _wrappingEnabled = true;

        public float Progress => _progress;
        public bool IsPlaying => _playing;
        public bool WrappingEnabled
        {
            get => _wrappingEnabled;
            set
            {
                if (_wrappingEnabled == value)
                    return;
                _wrappingEnabled = value;
                ApplyProgress();
            }
        }

        public WrapPath Path => m_path;
        public WrapRibbonMesher Mesher => m_mesher;

        private void Awake()
        {
            _wrappingEnabled = m_wrapOnStart;
        }

        private void Start()
        {
            if (m_path != null)
                m_path.EnsureBaked();

            _playing = m_playOnStart;
            ApplyProgress();
        }

        private void Update()
        {
            if (!_playing)
                return;

            float speed = m_duration > 0.01f ? 1f / m_duration : 1f;
            _progress += Time.deltaTime * speed;
            if (_progress >= 1f)
            {
                if (m_loop)
                {
                    _progress -= 1f;
                    if (_progress >= 1f)
                        _progress = 0f;
                }
                else
                {
                    _progress = 1f;
                    _playing = false;
                }
            }

            ApplyProgress();
        }

        public void SetPlaying(bool playing) => _playing = playing;

        public void TogglePlay() => _playing = !_playing;

        public void SetProgress(float progress)
        {
            _progress = Mathf.Clamp01(progress);
            ApplyProgress();
        }

        public void ResetProgress()
        {
            _progress = 0f;
            _wrappingEnabled = m_wrapOnStart;
            _playing = m_playOnStart;
            ApplyProgress();
        }

        public void ToggleWrapping() => WrappingEnabled = !_wrappingEnabled;

        private void ApplyProgress()
        {
            if (m_path != null)
                m_path.EnsureBaked();

            WrapSample sample = m_path != null ? m_path.Evaluate(_progress) : default;
            float radius = m_path != null ? m_path.OrbitRadius : 1f;

            if (m_nozzle != null)
                m_nozzle.Apply(sample.nozzleWorld, _progress, radius);

            if (m_mesher != null)
                m_mesher.BuildUntil(_wrappingEnabled ? _progress : 0f);
        }
    }
}
