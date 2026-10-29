using System.Collections;
using System.Collections.Generic;
using NaughtyAttributes;
using UnityEngine;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Playback
{
    /// <summary>
    /// 按子任务时间轴回放，支持倍速、暂停与进度跳转。
    /// 播放循环通过 <see cref="SimPlaybackEventHandlerBehaviour.OnPlaybackEvaluate"/> 驱动
    /// （堆垛机/输送/货位等同帧求值，保证状态一致）。
    /// </summary>
    public class WarehouseSimPlaybackController : MonoBehaviour
    {
        [SerializeField, Label("事件源")]
        private SimPlaybackEventSource m_eventSource;
        [SerializeField, Label("回放倍速")]
        private float m_playbackSpeed = 60f;
        [SerializeField, Label("事件处理器")]
        private SimPlaybackEventHandlerBehaviour[] m_handlers;

        private IReadOnlyList<SimPlaybackEvent> _events = System.Array.Empty<SimPlaybackEvent>();
        private IReadOnlyList<SimSubTask> _subTasks = System.Array.Empty<SimSubTask>();
        private SimPlaybackEventHandlerBehaviour[] _handlerCache = System.Array.Empty<SimPlaybackEventHandlerBehaviour>();
        private Coroutine _playCoroutine;

        private double _anchorSimTime;
        private float _anchorWallTime;
        private bool _isPlaying;
        private bool _resetVisualsOnNextPlay;

        public bool IsPlaying => _isPlaying;
        public float PlaybackSpeed
        {
            get => m_playbackSpeed;
            set => SetPlaybackSpeed(value);
        }

        public double StartSimTime { get; private set; }
        public double EndSimTime { get; private set; }

        /// <summary>当前加载的子任务时间轴（供校验组件读取）。</summary>
        public IReadOnlyList<SimSubTask> SubTasks => _subTasks;

        /// <summary>配置的事件源（仿真运行器写入的目标）。</summary>
        public SimPlaybackEventSource EventSource => m_eventSource;

        public double CurrentSimTime
        {
            get
            {
                if (_subTasks.Count == 0)
                {
                    return 0;
                }

                if (!_isPlaying)
                {
                    return _anchorSimTime;
                }

                var speed = m_playbackSpeed > 0f ? m_playbackSpeed : 1f;
                return _anchorSimTime + (Time.time - _anchorWallTime) * speed;
            }
        }

        public float NormalizedTime
        {
            get
            {
                var span = EndSimTime - StartSimTime;
                if (span <= 0)
                {
                    return 0f;
                }

                return Mathf.Clamp01((float)((CurrentSimTime - StartSimTime) / span));
            }
            set => SeekNormalized(value);
        }

        /// <summary>从配置的 <see cref="SimPlaybackEventSource"/> 加载时间轴到控制器。</summary>
        [Button("从事件源加载")]
        public bool LoadFromEventSource()
        {
            if (m_eventSource == null)
            {
                return false;
            }

            var sourceSubTaskCount = m_eventSource.SubTasks?.Count ?? 0;
            if (sourceSubTaskCount == 0)
            {
                return false;
            }

            LoadTimeline(
                m_eventSource.Events,
                m_eventSource.SubTasks,
                m_eventSource.TotalSimSeconds);
            return _subTasks.Count > 0;
        }

        public void LoadTimeline(
            IReadOnlyList<SimPlaybackEvent> events,
            IReadOnlyList<SimSubTask> subTasks,
            double totalSimSeconds = 0d)
        {
            var list = events != null
                ? new List<SimPlaybackEvent>(events)
                : new List<SimPlaybackEvent>();
            list.Sort((a, b) =>
            {
                var timeCompare = a.SimTime.CompareTo(b.SimTime);
                if (timeCompare != 0)
                {
                    return timeCompare;
                }

                var phaseCompare = a.Phase.CompareTo(b.Phase);
                return phaseCompare != 0 ? phaseCompare : a.JobId.CompareTo(b.JobId);
            });
            _events = list;
            _subTasks = subTasks ?? System.Array.Empty<SimSubTask>();
            if (_subTasks.Count == 0)
            {
                StartSimTime = EndSimTime = 0;
                _anchorSimTime = 0;
                return;
            }

            StartSimTime = SimSubTaskQuery.GetTimelineStart(_subTasks);
            if (_events.Count > 0)
            {
                StartSimTime = System.Math.Min(StartSimTime, _events[0].SimTime);
            }

            EndSimTime = SimSubTaskQuery.GetTimelineEnd(_subTasks);
            if (_events.Count > 0)
            {
                EndSimTime = System.Math.Max(EndSimTime, _events[^1].SimTime);
            }

            if (totalSimSeconds > EndSimTime + 1e-9)
            {
                EndSimTime = totalSimSeconds;
            }

            _anchorSimTime = StartSimTime;
            _resetVisualsOnNextPlay = true;
        }

        [Button]
        public void Play()
        {
            if (_subTasks.Count == 0)
            {
                return;
            }

            RefreshHandlerCache();
            if (_resetVisualsOnNextPlay)
            {
                ResetHandlerStates();
                _resetVisualsOnNextPlay = false;
                if (_subTasks.Count > 0)
                {
                    EvaluateAt(StartSimTime);
                }
            }

            _isPlaying = true;
            ResetWallAnchor();
            RestartCoroutine();
        }

        [Button]
        public void Pause()
        {
            if (!_isPlaying)
            {
                return;
            }

            _anchorSimTime = CurrentSimTime;
            _isPlaying = false;
            StopCoroutineIfRunning();
        }

        [Button]
        public void Stop()
        {
            _isPlaying = false;
            StopCoroutineIfRunning();
            _anchorSimTime = StartSimTime;
            _resetVisualsOnNextPlay = true;
        }

        /// <summary>跳转到指定仿真时刻，并重放该时刻之前的全部事件以恢复场景状态。</summary>
        public void SeekSimTime(double simTime)
        {
            if (_subTasks.Count == 0)
            {
                return;
            }

            simTime = System.Math.Clamp(simTime, StartSimTime, EndSimTime);
            var wasPlaying = _isPlaying;
            if (_isPlaying)
            {
                Pause();
            }

            RefreshHandlerCache();
            EvaluateAt(simTime);
            _anchorSimTime = simTime;
            ResetWallAnchor();

            if (wasPlaying)
            {
                Play();
            }
        }

        public void SeekNormalized(float normalized)
        {
            var span = EndSimTime - StartSimTime;
            if (span <= 0)
            {
                SeekSimTime(StartSimTime);
                return;
            }

            SeekSimTime(StartSimTime + span * Mathf.Clamp01(normalized));
        }

        public void SetPlaybackSpeed(float speed)
        {
            if (speed <= 0f)
            {
                speed = 1f;
            }

            if (Mathf.Approximately(m_playbackSpeed, speed))
            {
                return;
            }

            if (_isPlaying)
            {
                _anchorSimTime = CurrentSimTime;
                ResetWallAnchor();
            }

            m_playbackSpeed = speed;
        }

        private void RestartCoroutine()
        {
            StopCoroutineIfRunning();
            _playCoroutine = StartCoroutine(PlayCoroutine());
        }

        private void StopCoroutineIfRunning()
        {
            if (_playCoroutine != null)
            {
                StopCoroutine(_playCoroutine);
                _playCoroutine = null;
            }
        }

        private void ResetWallAnchor()
        {
            _anchorWallTime = Time.time;
        }

        private IEnumerator PlayCoroutine()
        {
            while (_isPlaying)
            {
                var simTime = CurrentSimTime;
                EvaluateAt(simTime);

                if (simTime >= EndSimTime - 1e-9)
                {
                    break;
                }

                yield return null;
            }

            if (_isPlaying)
            {
                EvaluateAt(EndSimTime);
            }

            _isPlaying = false;
            _playCoroutine = null;
        }

        private void EvaluateAt(double simTime)
        {
            if (_handlerCache.Length == 0)
            {
                return;
            }

            for (var i = 0; i < _handlerCache.Length; i++)
            {
                var handler = _handlerCache[i];
                if (handler == null)
                {
                    continue;
                }

                handler.OnPlaybackEvaluate(simTime, _subTasks);
            }
        }

        private void ResetHandlerStates()
        {
            if (_handlerCache.Length == 0)
            {
                return;
            }

            for (var i = 0; i < _handlerCache.Length; i++)
            {
                _handlerCache[i]?.ResetPlaybackState();
            }
        }

        private void RefreshHandlerCache()
        {
            if (m_handlers == null || m_handlers.Length == 0)
            {
                _handlerCache = System.Array.Empty<SimPlaybackEventHandlerBehaviour>();
                return;
            }

            var list = new List<SimPlaybackEventHandlerBehaviour>(m_handlers.Length);
            for (var i = 0; i < m_handlers.Length; i++)
            {
                if (m_handlers[i] != null)
                {
                    list.Add(m_handlers[i]);
                }
            }

            _handlerCache = list.ToArray();
        }
    }
}
