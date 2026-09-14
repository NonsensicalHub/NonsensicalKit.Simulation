using NonsensicalKit.ScriptAnimation;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace NonsensicalKit.ScriptAnimation.Demo
{
    /// <summary>
    /// Demo 面板：进度条直接驱动 WrapProgressController。
    /// 喷嘴位姿与膜带都是 progress 的纯函数，可来回 seek。
    /// </summary>
    public class FilmWrappingMachineDemo : MonoBehaviour
    {
        [Header("核心引用")]
        [InspectorLabel("进度控制器")]
        [Tooltip("唯一进度源；滑条/播放/重置都走 SetProgress")]
        [SerializeField] private WrapProgressController m_controller;

        [Header("界面 UI")]
        [InspectorLabel("状态文本")]
        [SerializeField] private Text m_statusText;
        [InspectorLabel("进度滑条")]
        [SerializeField] private Slider m_progressSlider;
        [InspectorLabel("播放/暂停")]
        [SerializeField] private Button m_playButton;
        [InspectorLabel("重置")]
        [SerializeField] private Button m_resetButton;

        private float _uiTimer;

        private void Awake()
        {
            if (m_playButton != null)
                m_playButton.onClick.AddListener(TogglePlay);
            if (m_resetButton != null)
                m_resetButton.onClick.AddListener(ResetAll);
            if (m_progressSlider != null)
            {
                m_progressSlider.minValue = 0f;
                m_progressSlider.maxValue = 1f;
                m_progressSlider.onValueChanged.AddListener(OnSliderChanged);
            }
        }

        private void Start()
        {
            RefreshUi();
        }

        private void Update()
        {
            if (WasPressed(KeyCode.Space))
                TogglePlay();
            if (WasPressed(KeyCode.R))
                ResetAll();
            if (WasPressed(KeyCode.W))
                ToggleWrapping();
        }

        private void LateUpdate()
        {
            if (m_progressSlider != null && m_controller != null)
                m_progressSlider.SetValueWithoutNotify(m_controller.Progress);

            _uiTimer += Time.unscaledDeltaTime;
            if (_uiTimer >= 0.2f)
            {
                _uiTimer = 0f;
                RefreshUi();
            }
        }

        public void TogglePlay()
        {
            if (m_controller != null)
                m_controller.TogglePlay();
            RefreshUi();
        }

        public void ToggleWrapping()
        {
            if (m_controller == null)
                return;
            m_controller.ToggleWrapping();
            RefreshUi();
        }

        public void ResetAll()
        {
            if (m_controller != null)
                m_controller.ResetProgress();
            RefreshUi();
        }

        private void OnSliderChanged(float value)
        {
            if (m_controller == null)
                return;

            m_controller.SetPlaying(false);
            m_controller.SetProgress(value);
            RefreshUi();
        }

        private void RefreshUi()
        {
            if (m_statusText == null)
                return;

            bool playing = m_controller != null && m_controller.IsPlaying;
            bool wrapping = m_controller != null && m_controller.WrappingEnabled;
            float progress = m_controller != null ? m_controller.Progress : 0f;
            int rings = m_controller != null && m_controller.Mesher != null
                ? m_controller.Mesher.RingCount
                : 0;

            string wrapState = wrapping ? "已启用" : "关闭";

            m_statusText.text =
                "缠膜机 Demo（进度驱动）\n" +
                "进度: " + (playing ? "播放" : "暂停") +
                "  " + (progress * 100f).ToString("0.0") + "%\n" +
                "缠膜: " + wrapState + "  段数=" + rings + "\n" +
                "[Space] 播放/暂停   [W] 开/关缠膜   [R] 重置\n" +
                "拖动进度条可来回控制喷嘴与膜带";
        }

        private static bool WasPressed(KeyCode key)
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return false;
            switch (key)
            {
                case KeyCode.Space: return keyboard.spaceKey.wasPressedThisFrame;
                case KeyCode.R: return keyboard.rKey.wasPressedThisFrame;
                case KeyCode.W: return keyboard.wKey.wasPressedThisFrame;
                default: return false;
            }
#else
            return Input.GetKeyDown(key);
#endif
        }
    }
}
