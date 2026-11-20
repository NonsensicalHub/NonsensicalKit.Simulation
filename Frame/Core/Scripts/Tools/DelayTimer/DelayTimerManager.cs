using System;
using System.Collections.Generic;
using UnityEngine;

namespace Frame.Core.Tools
{
/// <summary>
/// 高性能集中式定时器 —— struct 数组 + Swap-Remove + 两阶段更新。
/// 单一 Update 驱动全部定时器，主线程专用。
/// </summary>
public sealed class DelayTimerManager : MonoBehaviour
{

    private static DelayTimerManager _sInstance;
    private static bool _sQuitting;

    private static void EnsureInstance()
    {
        if (_sInstance != null) return;
        var go = new GameObject("[TimerManager]")
        {
            hideFlags = HideFlags.HideInHierarchy
        };
        _sInstance = go.AddComponent<DelayTimerManager>();
        DontDestroyOnLoad(go);
    }

   
    private const byte FLoop = 1 << 0;
    private const byte FUnscaled = 1 << 1;
    private const byte FPaused = 1 << 2;

    private struct TimerSlot
    {
        public int Id;
        public float Duration;
        public float Elapsed;
        public byte Flags;
        public Action Callback;
    }


    private TimerSlot[] _slots;
    private int _count;
    private readonly Dictionary<int, int> _idToIndex = new(64);
    private int _nextId;
    private int[] _timeoutIds = new int[16];

 
    /// <summary>创建定时器，返回句柄用于查询和控制。</summary>
    public static TimerHandle Schedule(
        float duration,
        Action onTimeout,
        bool loop = false,
        bool useUnscaledTime = false)
    {
        if (_sQuitting) return null;
        EnsureInstance();

        var inst = _sInstance;
        int id = ++inst._nextId;
        byte flags = 0;
        if (loop) flags |= FLoop;
        if (useUnscaledTime) flags |= FUnscaled;

        inst.AddSlot(id, duration, onTimeout, flags);
        return new TimerHandle(id, inst, duration, onTimeout, loop, useUnscaledTime);
    }

    /// <summary>清除全部活跃定时器（场景切换时调用）。</summary>
    public static void CancelAll()
    {
        if (_sInstance == null) return;
        var inst = _sInstance;
        for (int i = 0; i < inst._count; i++)
            inst._slots[i] = default;
        inst._count = 0;
        inst._idToIndex.Clear();
    }

    /// <summary>当前活跃定时器数量。</summary>
    public static int ActiveCount => _sInstance?._count ?? 0;

    // ======================== 内部 API（TimerHandle 调用） ========================

    internal int CreateTimer(float dur, Action cb, bool loop, bool unscaled)
    {
        int id = ++_nextId;
        byte flags = 0;
        if (loop) flags |= FLoop;
        if (unscaled) flags |= FUnscaled;
        AddSlot(id, dur, cb, flags);
        return id;
    }

    internal bool HasTimer(int id) => _idToIndex.ContainsKey(id);

    internal float GetRemaining(int id) =>
        _idToIndex.TryGetValue(id, out int idx)
            ? Mathf.Max(0f, _slots[idx].Duration - _slots[idx].Elapsed)
            : -1f;

    internal float GetElapsed(int id) =>
        _idToIndex.TryGetValue(id, out int idx) ? _slots[idx].Elapsed : -1f;

    internal void ResetTimer(int id)
    {
        if (_idToIndex.TryGetValue(id, out int idx))
            _slots[idx].Elapsed = 0f;
    }

    internal void SetPaused(int id, bool paused)
    {
        if (!_idToIndex.TryGetValue(id, out int idx)) return;
        if (paused) _slots[idx].Flags |= FPaused;
        else _slots[idx].Flags &= unchecked((byte)~FPaused);
    }

    internal void CancelTimer(int id)
    {
        if (_idToIndex.TryGetValue(id, out int idx))
            RemoveSlot(idx);
    }

    // ======================== 数组管理 ========================

    private void AddSlot(int id, float dur, Action cb, byte flags)
    {
        if (_slots == null)
            _slots = new TimerSlot[64];
        else if (_count == _slots.Length)
            Array.Resize(ref _slots, _slots.Length * 2);

        ref var slot = ref _slots[_count];
        slot.Id = id;
        slot.Duration = dur;
        slot.Elapsed = 0f;
        slot.Flags = flags;
        slot.Callback = cb;
        _idToIndex[id] = _count++;
    }

    /// <summary>Swap-Remove：用末尾元素填补空洞，保持数组紧凑。</summary>
    private void RemoveSlot(int index)
    {
        int id = _slots[index].Id;
        _count--;

        if (index < _count)
        {
            _slots[index] = _slots[_count]; // 末尾元素前移
            _idToIndex[_slots[index].Id] = index; // 更新被移动元素的索引
        }

        _slots[_count] = default; // 清理末尾残留
        _idToIndex.Remove(id); // 移除映射
    }


    #region 生命周期

    private void Update()
    {
        if (_count == 0) return;

        float dt = Time.deltaTime;
        float udt = Time.unscaledDeltaTime;
        int timeoutCount = 0;

        // ── Phase 1: 线性扫描 ──────────────────────────
        // 纯遍历，无回调，无分支跳表，CPU 缓存命中率最高。
        // 这是每帧的热路径。

        for (int i = 0; i < _count; i++)
        {
            ref var slot = ref _slots[i];
            if ((slot.Flags & FPaused) != 0) continue;

            slot.Elapsed += (slot.Flags & FUnscaled) != 0 ? udt : dt;

            if (slot.Elapsed >= slot.Duration)
            {
                if (timeoutCount >= _timeoutIds.Length)
                    Array.Resize(ref _timeoutIds, _timeoutIds.Length * 2);
                _timeoutIds[timeoutCount++] = slot.Id;
            }
        }

        // ── Phase 2: 处理超时 ──────────────────────────
        // 逐个触发回调。回调中可能增删任意定时器，
        // 因此每次通过 _idToIndex 查找当前索引。

        for (int i = 0; i < timeoutCount; i++)
        {
            int id = _timeoutIds[i];
            if (!_idToIndex.TryGetValue(id, out int idx)) continue; // 已被前序回调移除

            ref var slot = ref _slots[idx];

            // 二次确认：前序回调可能已重置了这个定时器
            if (slot.Elapsed < slot.Duration) continue;

            Action cb = slot.Callback;

            try { cb?.Invoke(); }
            catch (Exception ex) { Debug.LogException(ex); }

            // 回调可能已经移除/重置了这个定时器
            if (!_idToIndex.TryGetValue(id, out idx)) continue;

            slot = ref _slots[idx];
            if (slot.Elapsed < slot.Duration) continue; // 回调中已重置

            if ((slot.Flags & FLoop) != 0)
                slot.Elapsed -= slot.Duration; // 用减法避免帧误差累积
            else
                RemoveSlot(idx);
        }
    }


    private void OnDestroy()
    {
        if (_sInstance == this) _sInstance = null;
    }

    private void OnApplicationQuit() { _sQuitting = true; }

    #endregion
}

/* 使用示例 */

/*****延迟执行 + 循环 Tick*********/
///////////////////////////////////////
//延迟 2 秒后执行一次
/*DelayTimerManager.Schedule(2f, () => Debug.Log("2 秒后触发"));

// 每 0.5 秒执行一次（血量回复等）
var regen = DelayTimerManager.Schedule(0.5f, () =>
{
    hp = Mathf.Min(hp + 1, maxHp);
    if (hp >= maxHp) regen.Cancel();   // 满血自动停止
}, loop: true);

// 暂停 / 恢复
regen.Pause();
regen.Resume(); */
/*************************************/

/*********技能冷却***********/
/*
public class SkillSystem : MonoBehaviour
{
    private TimerHandle _cooldown;

    public void CastSkill()
    {
        if (_cooldown != null && _cooldown.IsActive) return; // 冷却中

        Debug.Log("释放技能！");
        _cooldown = DelayTimerManager.Schedule(3f, OnCooldownEnd);
    }

    private void OnCooldownEnd()
    {
        Debug.Log("冷却结束，可再次释放");
    }
} */

/****************空闲检测**********************/

/*public class SignalReceiver : MonoBehaviour
{
    [SerializeField] private float idleTimeout = 5f;

    private TimerHandle _idleTimer;
    private int _currentState;

    void OnEnable()
    {
        // 启动空闲计时器：5 秒内无信号则触发
        _idleTimer = DelayTimerManager.Schedule(idleTimeout, OnBecomeIdle);
    }

    void OnDisable()
    {
        _idleTimer?.Cancel();
    }

    /// <summary>
    /// 外部调用：收到信号时更新状态并重置计时器
    /// </summary>
    public void ReceiveSignal(int newState)
    {
        if (_currentState == newState) return;
        _currentState = newState;

        // 状态变了，重置空闲计时
        _idleTimer.Reset();

        // ... 其他状态变更逻辑 ...
    }

    private void OnBecomeIdle()
    {
        Debug.Log($"{name}: 超过 {idleTimeout}s 未收到信号，执行自定义函数");

        // 如果需要持续检测（每 N 秒触发一次），用 loop = true：
        // _idleTimer = DelayTimerManager.Schedule(idleTimeout, OnBecomeIdle, loop: true);
    }
}*/
}
