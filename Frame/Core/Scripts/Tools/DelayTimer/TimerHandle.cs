using System;
using UnityEngine;

namespace Frame.Core.Tools
{
/// <summary>
/// 定时器句柄 —— 查询状态 + 控制定时器。
/// 缓存了创建参数，定时器结束后调用 Reset() 可自动重建。
/// </summary>
public sealed class TimerHandle
{
    private          int           _id;      // Reset / Restart 后会变
    private          float         _dur;     // Restart 后会变
    private readonly DelayTimerManager  _mgr;
    private readonly Action        _cb;
    private readonly bool          _loop;
    private readonly bool          _unscaled;

    internal TimerHandle(int id, DelayTimerManager mgr, float dur,
                         Action cb, bool loop, bool unscaled)
    {
        _id = id; _mgr = mgr; _dur = dur;
        _cb  = cb; _loop = loop; _unscaled = unscaled;
    }

    // ── 查询 ──────────────────────────────────────────

    /// <summary>定时器是否仍在运行。</summary>
    public bool  IsActive  => _mgr != null && _mgr.HasTimer(_id);

    /// <summary>剩余时间（秒），已结束返回 -1。</summary>
    public float Remaining => _mgr?.GetRemaining(_id) ?? -1f;

    /// <summary>已流逝时间（秒）。</summary>
    public float Elapsed   => _mgr?.GetElapsed(_id) ?? 0f;

    /// <summary>进度 0~1。</summary>
    public float Progress  => _dur > 0f ? Mathf.Clamp01(Elapsed / _dur) : 1f;

    /// <summary>总时长（秒）。</summary>
    public float Duration  => _dur;

    // ── 控制 ──────────────────────────────────────────

    /// <summary>
    /// 重置已用时间至 0。
    /// 若定时器已结束/被取消，以相同参数自动重建，句柄保持有效。
    /// </summary>
    public void Reset()
    {
        if (_mgr == null) return;
        if (_mgr.HasTimer(_id))
            _mgr.ResetTimer(_id);
        else
            _id = _mgr.CreateTimer(_dur, _cb, _loop, _unscaled);
    }

    /// <summary>
    /// 用新时长重新开始。旧定时器取消，新建一个。
    /// </summary>
    public void Restart(float newDuration)
    {
        if (_mgr == null) return;
        if (_mgr.HasTimer(_id)) _mgr.CancelTimer(_id);
        _dur = newDuration;
        _id  = _mgr.CreateTimer(_dur, _cb, _loop, _unscaled);
    }

    public void Pause()  => _mgr?.SetPaused(_id, true);
    public void Resume() => _mgr?.SetPaused(_id, false);
    public void Cancel() => _mgr?.CancelTimer(_id);
}
}
