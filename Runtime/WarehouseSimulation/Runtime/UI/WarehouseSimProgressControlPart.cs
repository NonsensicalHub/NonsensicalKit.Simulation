using System;
using NaughtyAttributes;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using NonsensicalKit.Simulation.WarehouseSimulation.Playback;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Runtime.UI
{
    /// <summary>
    /// 将 UI 进度条绑定到 <see cref="WarehouseSimPlaybackController"/>，
    /// 支持拖拽跳转任意仿真时刻，并显示当前/总仿真时间。
    /// </summary>
    public class WarehouseSimProgressControlPart : MonoBehaviour,
        IPointerDownHandler, IBeginDragHandler, IEndDragHandler, IPointerUpHandler
    {
        [SerializeField, Label("回放控制器")]
        private WarehouseSimPlaybackController m_playback;

        [SerializeField, Label("进度条")]
        private Slider m_slider;

        [SerializeField, Label("时间文本")]
        private TextMeshProUGUI m_timeText;

        [SerializeField] private UnityEvent<bool> m_dragStateChanged;
        [SerializeField] private UnityEvent<WarehouseSimProgressState> m_progressStateChanged;

        private readonly WarehouseSimProgressState _state = new();
        private bool _dragging;
        private double _lastCurrentSimTime = double.MinValue;
        private double _lastEndSimTime = double.MinValue;
        private float _lastNormalizedTime = float.MinValue;

        public UnityEvent<bool> OnDragStateChanged => m_dragStateChanged;
        public UnityEvent<WarehouseSimProgressState> OnProgressStateChanged => m_progressStateChanged;
        public bool Dragging => _dragging;

        public WarehouseSimProgressState State => _state;

        private void Awake()
        {
            if (m_slider == null)
            {
                m_slider = GetComponentInChildren<Slider>();
            }

            if (m_slider != null)
            {
                m_slider.onValueChanged.AddListener(OnSliderValueChanged);
            }
        }

        private void OnDestroy()
        {
            if (m_slider != null)
            {
                m_slider.onValueChanged.RemoveListener(OnSliderValueChanged);
            }
        }

        private void Update()
        {
            if (m_playback == null)
            {
                return;
            }

            RefreshState();
            PublishProgressState();

            if (m_slider == null || _dragging)
            {
                return;
            }

            m_slider.SetValueWithoutNotify(_state.NormalizedTime);
        }

        public void OnBeginDrag(PointerEventData eventData) => SetDragging(true);

        public void OnEndDrag(PointerEventData eventData) => SetDragging(false);

        public void OnPointerDown(PointerEventData eventData) => SetDragging(true);

        public void OnPointerUp(PointerEventData eventData) => SetDragging(false);

        private void OnSliderValueChanged(float value)
        {
            if (m_playback == null)
            {
                return;
            }

            m_playback.SeekNormalized(value);
            RefreshState();
            PublishProgressState();
        }

        private void RefreshState()
        {
            _state.CurrentSimTime = m_playback.CurrentSimTime;
            _state.StartSimTime = m_playback.StartSimTime;
            _state.EndSimTime = m_playback.EndSimTime;
            _state.NormalizedTime = m_playback.NormalizedTime;

            if (m_timeText != null)
            {
                m_timeText.text =
                    $"{WarehouseSimSimTimeFormatting.Format(_state.CurrentSimTime)} / " +
                    $"{WarehouseSimSimTimeFormatting.Format(_state.EndSimTime)}";
            }
        }

        private void PublishProgressState()
        {
            if (Math.Abs(_lastCurrentSimTime - _state.CurrentSimTime) < 1e-6
                && Math.Abs(_lastEndSimTime - _state.EndSimTime) < 1e-6
                && Mathf.Approximately(_lastNormalizedTime, _state.NormalizedTime))
            {
                return;
            }

            _lastCurrentSimTime = _state.CurrentSimTime;
            _lastEndSimTime = _state.EndSimTime;
            _lastNormalizedTime = _state.NormalizedTime;
            m_progressStateChanged?.Invoke(_state);
        }

        private void SetDragging(bool dragging)
        {
            if (_dragging == dragging)
            {
                return;
            }

            _dragging = dragging;
            m_dragStateChanged?.Invoke(_dragging);

            if (!_dragging && m_playback != null && m_slider != null)
            {
                m_slider.SetValueWithoutNotify(m_playback.NormalizedTime);
                RefreshState();
                PublishProgressState();
            }
        }
    }
}
