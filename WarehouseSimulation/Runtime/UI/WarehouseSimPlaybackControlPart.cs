using NaughtyAttributes;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;
using UnityEngine.UI;
using NonsensicalKit.Simulation.WarehouseSimulation.Playback;
using NonsensicalKit.UGUI;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Runtime
{
    /// <summary>
    /// 绑定 <see cref="WarehouseSimPlaybackController"/>，提供播放/暂停/停止、从事件源加载与倍速调节。
    /// </summary>
    public sealed class WarehouseSimPlaybackControlPart : MonoBehaviour
    {
        [SerializeField, Label("回放控制器")]
        private WarehouseSimPlaybackController m_playback;

        [Header("可为空的配置项")]
        [SerializeField] private ToggleButton m_btn_play;
        [SerializeField, FormerlySerializedAs("m_btn_playFromSource")]
        private Button m_btn_loadFromSource;
        [SerializeField] private Button m_btn_stop;
        [SerializeField] private Slider m_sld_speed;
        [SerializeField] private TextMeshProUGUI m_speedText;
        [SerializeField] private TextMeshProUGUI m_statusText;
        [SerializeField, Min(0.1f)] private float m_speedMin = 1f;
        [SerializeField, Min(0.1f)] private float m_speedMax = 120f;

        [SerializeField] private UnityEvent<WarehouseSimPlaybackState> m_playbackStateChanged;

        private readonly WarehouseSimPlaybackState _state = new();
        private bool _syncingUi;
        private bool _lastIsPlaying;
        private bool _lastHasTimeline;
        private float _lastPlaybackSpeed;

        public UnityEvent<WarehouseSimPlaybackState> OnPlaybackStateChanged => m_playbackStateChanged;

        private void Awake()
        {
            ConfigureSpeedSlider();
            m_btn_play?.m_OnValueChanged.AddListener(OnPlayToggleChanged);
            m_btn_loadFromSource?.onClick.AddListener(OnLoadFromSourceClicked);
            m_btn_stop?.onClick.AddListener(OnStopClicked);
            m_sld_speed?.onValueChanged.AddListener(OnSpeedChanged);
        }

        private void OnDestroy()
        {
            m_btn_play?.m_OnValueChanged.RemoveListener(OnPlayToggleChanged);
            m_btn_loadFromSource?.onClick.RemoveListener(OnLoadFromSourceClicked);
            m_btn_stop?.onClick.RemoveListener(OnStopClicked);
            m_sld_speed?.onValueChanged.RemoveListener(OnSpeedChanged);
        }

        private void Update()
        {
            if (m_playback == null)
            {
                return;
            }

            SyncStateFromPlayback();
        }

        private void OnPlayToggleChanged(bool isPlaying)
        {
            if (_syncingUi || m_playback == null)
            {
                return;
            }

            if (isPlaying)
            {
                m_playback.Play();
            }
            else
            {
                m_playback.Pause();
            }
        }

        private void OnLoadFromSourceClicked()
        {
            m_playback?.LoadFromEventSource();
        }

        private void OnStopClicked()
        {
            m_playback?.Stop();
        }

        private void OnSpeedChanged(float speed)
        {
            if (_syncingUi || m_playback == null)
            {
                return;
            }

            m_playback.SetPlaybackSpeed(speed);
            RefreshSpeedText(speed);
        }

        private void SyncStateFromPlayback()
        {
            _state.IsPlaying = m_playback.IsPlaying;
            _state.HasTimeline = m_playback.SubTasks != null && m_playback.SubTasks.Count > 0;
            _state.PlaybackSpeed = m_playback.PlaybackSpeed;

            _syncingUi = true;
            m_btn_play?.SetState(_state.IsPlaying);

            if (m_sld_speed != null && !Mathf.Approximately(m_sld_speed.value, _state.PlaybackSpeed))
            {
                m_sld_speed.SetValueWithoutNotify(_state.PlaybackSpeed);
                RefreshSpeedText(_state.PlaybackSpeed);
            }

            _syncingUi = false;

            var hasTimeline = _state.HasTimeline;
            if (m_btn_play != null)
            {
                m_btn_play.gameObject.SetActive(hasTimeline);
            }

            if (m_btn_stop != null)
            {
                m_btn_stop.interactable = hasTimeline;
            }

            if (m_btn_loadFromSource != null)
            {
                m_btn_loadFromSource.interactable = m_playback.EventSource != null && !_state.IsPlaying;
            }

            if (m_statusText != null)
            {
                m_statusText.text = hasTimeline
                    ? (_state.IsPlaying ? "回放中" : "回放已暂停")
                    : "无时间轴，请先运行仿真并加载。";
            }

            if (_lastIsPlaying == _state.IsPlaying
                && _lastHasTimeline == _state.HasTimeline
                && Mathf.Approximately(_lastPlaybackSpeed, _state.PlaybackSpeed))
            {
                return;
            }

            _lastIsPlaying = _state.IsPlaying;
            _lastHasTimeline = _state.HasTimeline;
            _lastPlaybackSpeed = _state.PlaybackSpeed;
            m_playbackStateChanged?.Invoke(_state);
        }

        private void RefreshSpeedText(float speed)
        {
            if (m_speedText == null)
            {
                return;
            }

            m_speedText.text = $"{speed:0.#}x";
        }

        private void ConfigureSpeedSlider()
        {
            if (m_sld_speed == null)
            {
                return;
            }

            if (m_speedMax < m_speedMin)
            {
                (m_speedMin, m_speedMax) = (m_speedMax, m_speedMin);
            }

            m_sld_speed.minValue = m_speedMin;
            m_sld_speed.maxValue = m_speedMax;

            if (m_playback != null)
            {
                m_sld_speed.SetValueWithoutNotify(Mathf.Clamp(m_playback.PlaybackSpeed, m_speedMin, m_speedMax));
                RefreshSpeedText(m_sld_speed.value);
            }
        }
    }
}
