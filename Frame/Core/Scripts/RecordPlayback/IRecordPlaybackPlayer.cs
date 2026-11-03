using System;
using System.Collections.Generic;

/// <summary>输送线 / AGV 等离线回放器共用接口，供 RecordDataPlayerUI 绑定。</summary>
public interface IRecordPlaybackPlayer
{
    bool IsPlaying { get; }
    bool IsPaused { get; }
    string PlayTime { get; }
    int PlayIndex { get; }
    int LineCount { get; }
    float Progress { get; }
    int DataFileCount { get; }
    int SelectedIndex { get; }
    float TimeScale { get; set; }
    bool Loop { get; set; }

    event Action StateChanged;

    List<string> GetDisplayNames();
    void SetSelectedIndex(int index);
    void Play();
    void Stop();
    void TogglePause();
}
