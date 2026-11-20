using System.Collections.Generic;
using NaughtyAttributes;
#if UNITY_2022_3_OR_NEWER
using Unity.Cinemachine;
#else
using Cinemachine;
#endif
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>
/// 测试用：关掉 Timeline 的 Cinemachine 轨输出，但保留 CinemachineBrain，
/// 方便播放时改用自己的相机 / 虚拟相机。
/// </summary>
public class TimelineCameraTrackToggle : MonoBehaviour
{
    [SerializeField] private PlayableDirector m_director;

    [Tooltip("为空则匹配所有 CinemachineTrack；可填「Cinemachine Track」。")]
    [SerializeField] private string m_trackName = "Cinemachine Track";

    private readonly List<TrackAsset> _cameraTracks = new List<TrackAsset>();
    private readonly Dictionary<TrackAsset, Object> _savedBindings = new Dictionary<TrackAsset, Object>();
    private bool _timelineCameraEnabled = true;

    private void Awake()
    {
        if (m_director == null)
            m_director = GetComponent<PlayableDirector>();
    }

    [Button("禁用 Timeline 相机轨"), ContextMenu("禁用 Timeline 相机轨")]
    public void DisableCameraTrack() => SetCameraTrackEnabled(false);

    [Button("启用 Timeline 相机轨"), ContextMenu("启用 Timeline 相机轨")]
    public void EnableCameraTrack() => SetCameraTrackEnabled(true);

    [Button("切换 Timeline 相机轨"), ContextMenu("切换 Timeline 相机轨")]
    public void ToggleCameraTrack() => SetCameraTrackEnabled(!_timelineCameraEnabled);

    /// <summary>
    /// enabled=false：解除 Timeline Cinemachine 轨对 Brain 的绑定，并 mute 轨道。
    /// Brain 本身不关，你自己的虚拟相机仍可通过 Brain 控制画面。
    /// 会 RebuildGraph，但会恢复当前 time / 播放状态。
    /// </summary>
    public void SetCameraTrackEnabled(bool enabled)
    {
        if (!TryCollectCameraTracks())
            return;

        if (enabled)
        {
            for (int i = 0; i < _cameraTracks.Count; i++)
            {
                TrackAsset track = _cameraTracks[i];
                track.muted = false;
                if (_savedBindings.TryGetValue(track, out Object binding) && binding != null)
                    m_director.SetGenericBinding(track, binding);
            }
        }
        else
        {
            _savedBindings.Clear();
            for (int i = 0; i < _cameraTracks.Count; i++)
            {
                TrackAsset track = _cameraTracks[i];
                Object binding = m_director.GetGenericBinding(track);
                if (binding != null)
                    _savedBindings[track] = binding;

                track.muted = true;
                m_director.SetGenericBinding(track, null);
            }
        }
        RebuildGraphPreserveTime();
        _timelineCameraEnabled = enabled;

        Debug.Log(
            $"[TimelineCameraTrackToggle] Timeline 相机轨已{(enabled ? "启用" : "禁用")}。" +
            " CinemachineBrain 保持可用，可用自己的相机。",
            this);
    }

    public bool IsCameraTrackEnabled() => _timelineCameraEnabled;

    private void RebuildGraphPreserveTime()
    {
        if (m_director == null || !m_director.playableGraph.IsValid())
            return;

        double time = m_director.time;
        PlayState state = m_director.state;

        m_director.RebuildGraph();
        m_director.time = time;

        if (state == PlayState.Playing)
            m_director.Play();
        else
            m_director.Evaluate();
    }

    private bool TryCollectCameraTracks()
    {
        _cameraTracks.Clear();

        if (m_director == null)
        {
            Debug.LogWarning("[TimelineCameraTrackToggle] 未绑定 PlayableDirector。", this);
            return false;
        }

        var timeline = m_director.playableAsset as TimelineAsset;
        if (timeline == null)
        {
            Debug.LogWarning("[TimelineCameraTrackToggle] playableAsset 不是 TimelineAsset。", this);
            return false;
        }

        foreach (var track in timeline.GetOutputTracks())
        {
            if (track is not CinemachineTrack)
                continue;

            if (!string.IsNullOrWhiteSpace(m_trackName) &&
                track.name != m_trackName &&
                !track.name.Contains(m_trackName))
                continue;

            _cameraTracks.Add(track);
        }

        if (_cameraTracks.Count == 0)
        {
            Debug.LogWarning(
                $"[TimelineCameraTrackToggle] 未找到 Cinemachine 轨道（筛选名: {m_trackName}）。",
                this);
            return false;
        }

        return true;
    }
}
