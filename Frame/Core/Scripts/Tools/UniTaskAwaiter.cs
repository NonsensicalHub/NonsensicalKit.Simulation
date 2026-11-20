using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Frame.Core.Tools
{
/// <summary>
/// 基于 UniTask 的条件等待执行器。
/// 提供每帧/间隔轮询、超时保护、MonoBehaviour 生命周期绑定及零 GC 状态传递。
/// 所有公开方法均为 Fire-and-Forget，内部自动 Forget()。
/// </summary>
/// <remarks>
/// - 超时计时使用 Time.realtimeSinceStartup，不受 timeScale 影响。
/// - TState 泛型重载可避免闭包分配，适用于 Update 级高频调用。
/// - 取消时静默退出，不抛出异常。
/// </remarks>

public static class UniTaskAwaiter
{
    #region 基础调用

    /// <summary> 每帧检查，条件满足后执行同步方法 </summary>
    public static void WaitAndExecute(
        Func<bool> condition, Action action,
        float timeout = -1f, CancellationToken ct = default)
    {
        WaitPerFrame(condition, action, timeout, ct).Forget();
    }

    /// <summary> 间隔检查（毫秒），条件满足后执行同步方法 </summary>
    public static void WaitAndExecute(
        Func<bool> condition, Action action,
        int checkIntervalMs, float timeout = -1f, CancellationToken ct = default)
    {
        WaitWithDelay(condition, action, checkIntervalMs, timeout, ct).Forget();
    }

    /// <summary> 每帧检查，条件满足后执行异步方法 </summary>
    public static void WaitAndExecuteAsync(
        Func<bool> condition, Func<UniTask> asyncAction,
        float timeout = -1f, CancellationToken ct = default)
    {
        WaitPerFrameAsync(condition, asyncAction, timeout, ct).Forget();
    }

    #endregion

    #region 带状态 API（热路径零闭包）

    /// <summary> 每帧检查 + 状态传递，避免闭包 GC </summary>
    public static void WaitAndExecute<TState>(
        TState state, Func<TState, bool> condition, Action<TState> action,
        float timeout = -1f, CancellationToken ct = default) where TState : class
    {
        WaitPerFrameState(state, condition, action, timeout, ct).Forget();
    }

    /// <summary> 间隔检查 + 状态传递，避免闭包 GC </summary>
    public static void WaitAndExecute<TState>(
        TState state, Func<TState, bool> condition, Action<TState> action,
        int checkIntervalMs, float timeout = -1f, CancellationToken ct = default) where TState : class
    {
        WaitWithDelayState(state, condition, action, checkIntervalMs, timeout, ct).Forget();
    }

    #endregion

    #region MonoBehaviour 绑定

    public static void WaitAndExecute(
        MonoBehaviour owner, Func<bool> condition, Action action,
        float timeout = -1f)
    {
        WaitPerFrame(condition, action, timeout, owner.GetCancellationTokenOnDestroy()).Forget();
    }

    public static void WaitAndExecute(
        MonoBehaviour owner, Func<bool> condition, Action action,
        int checkIntervalMs, float timeout = -1f)
    {
        WaitWithDelay(condition, action, checkIntervalMs, timeout,
            owner.GetCancellationTokenOnDestroy()).Forget();
    }

    public static void WaitAndExecuteAsync(
        MonoBehaviour owner, Func<bool> condition, Func<UniTask> asyncAction,
        float timeout = -1f)
    {
        WaitPerFrameAsync(condition, asyncAction, timeout,
            owner.GetCancellationTokenOnDestroy()).Forget();
    }

    public static void WaitAndExecute<TState>(
        MonoBehaviour owner, TState state,
        Func<TState, bool> condition, Action<TState> action,
        float timeout = -1f) where TState : class
    {
        WaitPerFrameState(state, condition, action, timeout,
            owner.GetCancellationTokenOnDestroy()).Forget();
    }

    public static void WaitAndExecute<TState>(
        MonoBehaviour owner, TState state,
        Func<TState, bool> condition, Action<TState> action,
        int checkIntervalMs, float timeout = -1f) where TState : class
    {
        WaitWithDelayState(state, condition, action, checkIntervalMs, timeout,
            owner.GetCancellationTokenOnDestroy()).Forget();
    }

    #endregion

    #region 内部实现 — 每帧检查

    // ── UniTask.WaitUntil：内部使用池化的 IUniTaskSource，
    //    首次调用后零 GC 分配 ──

    private static async UniTaskVoid WaitPerFrame(
        Func<bool> condition, Action action, float timeout, CancellationToken ct)
    {
        if (condition()) { action?.Invoke(); return; }

        if (timeout > 0f)
        {
            if (!await WaitUntilWithTimeout(condition, timeout, ct)) return;
        }
        else
        {
            await UniTask.WaitUntil(condition, PlayerLoopTiming.Update, ct);
        }

        if (!ct.IsCancellationRequested) action?.Invoke();
    }

    private static async UniTaskVoid WaitPerFrameAsync(
        Func<bool> condition, Func<UniTask> action, float timeout, CancellationToken ct)
    {
        if (condition()) { if (action != null) await action(); return; }

        if (timeout > 0f)
        {
            if (!await WaitUntilWithTimeout(condition, timeout, ct)) return;
        }
        else
        {
            await UniTask.WaitUntil(condition, PlayerLoopTiming.Update, ct);
        }

        if (!ct.IsCancellationRequested && action != null) await action();
    }

    // 带状态：使用 WaitUntil(Func<bool>)，兼容未提供 TState 重载的 UniTask 版本
    private static async UniTaskVoid WaitPerFrameState<TState>(
        TState state, Func<TState, bool> condition, Action<TState> action,
        float timeout, CancellationToken ct) where TState : class
    {
        if (condition(state)) { action?.Invoke(state); return; }

        if (timeout > 0f)
        {
            if (!await WaitUntilWithTimeout(state, condition, timeout, ct)) return;
        }
        else
        {
            await UniTask.WaitUntil(() => condition(state), PlayerLoopTiming.Update, ct);
        }

        if (!ct.IsCancellationRequested) action?.Invoke(state);
    }

    #endregion

    #region 内部实现 — 间隔检查

    // ── UniTask.Delay(int, ...) 使用 int 毫秒参数，
    //    避免 TimeSpan 堆分配 ──

    private static async UniTaskVoid WaitWithDelay(
        Func<bool> condition, Action action, int intervalMs, float timeout, CancellationToken ct)
    {
        if (condition()) { action?.Invoke(); return; }

        float startTime = Time.realtimeSinceStartup;

        while (!condition())
        {
            if (ct.IsCancellationRequested) return;

            if (timeout > 0f && (Time.realtimeSinceStartup - startTime) >= timeout)
            {
                //Debug.LogWarning($"[UniTaskAwaiter] 超时 {timeout}s，条件未满足");
                return;
            }

            // try-catch 替代 SuppressCancellationThrow，兼容所有 UniTask 版本
            try
            {
                await UniTask.Delay(intervalMs, DelayType.DeltaTime,
                    PlayerLoopTiming.Update, ct);
            }
            catch (OperationCanceledException) { return; }
        }

        if (!ct.IsCancellationRequested) action?.Invoke();
    }

    private static async UniTaskVoid WaitWithDelayState<TState>(
        TState state, Func<TState, bool> condition, Action<TState> action,
        int intervalMs, float timeout, CancellationToken ct) where TState : class
    {
        if (condition(state)) { action?.Invoke(state); return; }

        float startTime = Time.realtimeSinceStartup;

        while (!condition(state))
        {
            if (ct.IsCancellationRequested) return;

            if (timeout > 0f && (Time.realtimeSinceStartup - startTime) >= timeout)
            {
                //Debug.LogWarning($"[UniTaskAwaiter] 超时 {timeout}s，条件未满足");
                return;
            }

            try
            {
                await UniTask.Delay(intervalMs, DelayType.DeltaTime,
                    PlayerLoopTiming.Update, ct);
            }
            catch (OperationCanceledException) { return; }
        }

        if (!ct.IsCancellationRequested) action?.Invoke(state);
    }

    #endregion

    #region 超时辅助

    // ── 带超时的 WaitUntil，用 UniTask.WaitUntil + CancellationTokenSource
    //    实现超时，不依赖 .Timeout() 扩展方法 ──

    private static async UniTask<bool> WaitUntilWithTimeout(
        Func<bool> condition, float timeout, CancellationToken ct)
    {
        float startTime = Time.realtimeSinceStartup;

        while (!condition())
        {
            if (ct.IsCancellationRequested) return false;

            if ((Time.realtimeSinceStartup - startTime) >= timeout)
            {
                //Debug.LogWarning($"[UniTaskAwaiter] 超时 {timeout}s，条件未满足");
                return false;
            }

            // 等一帧再检查
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }

        return true;
    }

    private static async UniTask<bool> WaitUntilWithTimeout<TState>(
        TState state, Func<TState, bool> condition, float timeout, CancellationToken ct)
    {
        float startTime = Time.realtimeSinceStartup;

        while (!condition(state))
        {
            if (ct.IsCancellationRequested) return false;

            if ((Time.realtimeSinceStartup - startTime) >= timeout)
            {
                //Debug.LogWarning($"[UniTaskAwaiter] 超时 {timeout}s，条件未满足");
                return false;
            }

            await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }

        return true;
    }

    #endregion
}
/*
using Cysharp.Threading.Tasks;
using UnityEngine;

public class Example : MonoBehaviour
{
    private bool isLoaded;
    private int score;

    void Start()
    {
        // 1. 最简调用
        UniTaskAwaiter.WaitAndExecute(
            () => isLoaded,
            () => Debug.Log("加载完成"));

        // 2. 绑定生命周期
        UniTaskAwaiter.WaitAndExecute(this,
            () => Input.GetKeyDown(KeyCode.Space),
            () => Debug.Log("按下空格"));

        // 3. 间隔检查 + 超时
        UniTaskAwaiter.WaitAndExecute(
            () => score >= 100,
            () => Debug.Log("达标！"),
            checkIntervalMs: 500,
            timeout: 30f);

        // 4. 带状态传递（热路径零闭包）
        UniTaskAwaiter.WaitAndExecute(
            state: this,
            condition: self => self.isLoaded && self.score > 0,
            action: self => self.OnReady());

        // 5. 带状态 + MonoBehaviour + 间隔
        UniTaskAwaiter.WaitAndExecute(
            owner: this,
            state: transform,
            condition: t => t.position.y > 10f,
            action: t => Debug.Log($"{t.name} 高度超过10"),
            checkIntervalMs: 200);

        // 6. 异步方法
        UniTaskAwaiter.WaitAndExecuteAsync(
            () => isLoaded,
            async () =>
            {
                await UniTask.Delay(1000);
                Debug.Log("异步完成");
            });
    }

    void OnReady() { }
}

 */}
