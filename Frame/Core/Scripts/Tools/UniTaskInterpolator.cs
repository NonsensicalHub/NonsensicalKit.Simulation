using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Frame.Core.Tools
{
/// <summary>
/// 基于 UniTask 的通用插值器。
/// 对某个值（float / Vector2 / Vector3 / Quaternion / Color）在指定时长内做插值，
/// 每帧通过回调把当前值写回目标对象。
/// 所有公开方法均为 Fire-and-Forget，内部自动 <c>Forget()</c>。
/// </summary>
/// <remarks>
/// <para><b>计时</b>：默认使用 <see cref="Time.deltaTime"/>；
/// 设置 <c>unscaledTime: true</c> 后改用 <see cref="Time.unscaledDeltaTime"/>，不受 timeScale 影响。</para>
/// <para><b>取消</b>：被取消时静默退出，不会把值跳到终点，也不触发 onComplete。</para>
/// <para><b>自动打断</b>：同一 cancelKey + <see cref="Channel"/> 的新插值会自动取消上一次，无需手动管理 CTS。</para>
/// <para><b>并行插值</b>：同目标上移动 / 旋转 / 缩放可并行（<see cref="Move"/> / <see cref="Rotate"/> / <see cref="Scale"/> 已内置不同 Channel）。</para>
/// <para><b>零闭包</b>：<c>To&lt;TState&gt;</c> 泛型重载将 state 作为第一个参数传入回调，
/// 配合静态方法可避免 lambda 闭包分配，适用于高频调用。</para>
/// </remarks>

public static class UniTaskInterpolator
{
    #region 缓动类型

    /// <summary> 缓动曲线类型 </summary>
    public enum Ease
    {
        /// <summary> 线性，匀速 </summary>
        Linear,
        /// <summary> 二次方缓入，先慢后快 </summary>
        InQuad,
        /// <summary> 二次方缓出，先快后慢 </summary>
        OutQuad,
        /// <summary> 二次方缓入缓出 </summary>
        InOutQuad,
        /// <summary> 三次方缓入 </summary>
        InCubic,
        /// <summary> 三次方缓出 </summary>
        OutCubic,
        /// <summary> 三次方缓入缓出 </summary>
        InOutCubic,
        /// <summary> 回弹缓出，终点略微超出后回弹 </summary>
        OutBack,
        /// <summary> 弹性缓出，终点附近振荡 </summary>
        OutElastic,
    }

    /// <summary>
    /// 插值通道。同一 cancelKey 下不同 Channel 的插值互不干扰，可并行运行。
    /// <see cref="Move"/> / <see cref="Rotate"/> / <see cref="Scale"/> 已分别绑定 Position / Rotation / Scale。
    /// </summary>
    public enum Channel
    {
        /// <summary> 默认通道，同 key 下同通道的插值互相打断 </summary>
        Default = 0,
        /// <summary> 位置通道（<see cref="Move"/> 内置） </summary>
        Position = 1,
        /// <summary> 旋转通道（<see cref="Rotate"/> 内置） </summary>
        Rotation = 2,
        /// <summary> 缩放通道（<see cref="Scale"/> 内置） </summary>
        Scale = 3,
    }

    /// <summary>
    /// 计算缓动后的插值系数。
    /// </summary>
    /// <param name="ease">缓动类型</param>
    /// <param name="t">归一化时间，通常为 elapsed / duration，范围 0~1</param>
    /// <returns>缓动后的系数，部分曲线（如 OutBack）可能超出 0~1</returns>
    public static float Evaluate(Ease ease, float t)
    {
        switch (ease)
        {
            case Ease.InQuad: return t * t;
            case Ease.OutQuad: return 1f - (1f - t) * (1f - t);
            case Ease.InOutQuad:
                return t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) * 0.5f;
            case Ease.InCubic: return t * t * t;
            case Ease.OutCubic: return 1f - Mathf.Pow(1f - t, 3f);
            case Ease.InOutCubic:
                return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) * 0.5f;
            case Ease.OutBack:
            {
                const float c1 = 1.70158f;
                const float c3 = c1 + 1f;
                float u = t - 1f;
                return 1f + c3 * u * u * u + c1 * u * u;
            }
            case Ease.OutElastic:
            {
                const float c4 = (2f * Mathf.PI) / 3f;
                if (t <= 0f) return 0f;
                if (t >= 1f) return 1f;
                return Mathf.Pow(2f, -10f * t) * Mathf.Sin((t * 10f - 0.75f) * c4) + 1f;
            }
            default: return t;
        }
    }

    #endregion

    #region 基础调用 — 按值类型

    /// <summary> 对 float 值在指定时长内插值 </summary>
    /// <param name="from">起始值</param>
    /// <param name="to">目标值</param>
    /// <param name="duration">持续时间（秒），&lt;= 0 时立即跳到终点</param>
    /// <param name="onUpdate">每帧回调，传入当前插值结果</param>
    /// <param name="ease">缓动曲线，默认 Linear</param>
    /// <param name="onComplete">正常完成时回调（取消不触发）</param>
    /// <param name="unscaledTime">true 时使用 unscaledDeltaTime，不受 timeScale 影响</param>
    /// <param name="ct">外部取消令牌，与自动打断机制合并</param>
    /// <param name="cancelKey">打断键；非 null 时同 key + channel 的新插值会自动取消上一次</param>
    /// <param name="channel">打断通道，默认 Default</param>
    public static void To(
        float from, float to, float duration, Action<float> onUpdate,
        Ease ease = Ease.Linear, Action onComplete = null,
        bool unscaledTime = false, CancellationToken ct = default,
        object cancelKey = null, Channel channel = Channel.Default)
    {
        Run(from, to, duration, FloatLerp, onUpdate, ease, onComplete, unscaledTime, ct, cancelKey, channel).Forget();
    }

    /// <summary> 对 Vector2 值在指定时长内插值 </summary>
    /// <inheritdoc cref="To(float, float, float, Action{float}, Ease, Action, bool, CancellationToken, object, Channel)"/>
    public static void To(
        Vector2 from, Vector2 to, float duration, Action<Vector2> onUpdate,
        Ease ease = Ease.Linear, Action onComplete = null,
        bool unscaledTime = false, CancellationToken ct = default,
        object cancelKey = null, Channel channel = Channel.Default)
    {
        Run(from, to, duration, Vector2Lerp, onUpdate, ease, onComplete, unscaledTime, ct, cancelKey, channel).Forget();
    }

    /// <summary> 对 Vector3 值在指定时长内插值 </summary>
    /// <inheritdoc cref="To(float, float, float, Action{float}, Ease, Action, bool, CancellationToken, object, Channel)"/>
    public static void To(
        Vector3 from, Vector3 to, float duration, Action<Vector3> onUpdate,
        Ease ease = Ease.Linear, Action onComplete = null,
        bool unscaledTime = false, CancellationToken ct = default,
        object cancelKey = null, Channel channel = Channel.Default)
    {
        Run(from, to, duration, Vector3Lerp, onUpdate, ease, onComplete, unscaledTime, ct, cancelKey, channel).Forget();
    }

    /// <summary> 对 Quaternion 值在指定时长内球面插值（Slerp） </summary>
    /// <inheritdoc cref="To(float, float, float, Action{float}, Ease, Action, bool, CancellationToken, object, Channel)"/>
    public static void To(
        Quaternion from, Quaternion to, float duration, Action<Quaternion> onUpdate,
        Ease ease = Ease.Linear, Action onComplete = null,
        bool unscaledTime = false, CancellationToken ct = default,
        object cancelKey = null, Channel channel = Channel.Default)
    {
        Run(from, to, duration, QuaternionLerp, onUpdate, ease, onComplete, unscaledTime, ct, cancelKey, channel).Forget();
    }

    /// <summary> 对 Color 值在指定时长内插值 </summary>
    /// <inheritdoc cref="To(float, float, float, Action{float}, Ease, Action, bool, CancellationToken, object, Channel)"/>
    public static void To(
        Color from, Color to, float duration, Action<Color> onUpdate,
        Ease ease = Ease.Linear, Action onComplete = null,
        bool unscaledTime = false, CancellationToken ct = default,
        object cancelKey = null, Channel channel = Channel.Default)
    {
        Run(from, to, duration, ColorLerp, onUpdate, ease, onComplete, unscaledTime, ct, cancelKey, channel).Forget();
    }

    #endregion

    #region 带状态 API（热路径零闭包）

    /// <summary>
    /// 对 float 值插值，并将 state 传入回调。
    /// state 自动作为 cancelKey，同 state + channel 的新插值会打断上一次。
    /// 回调使用静态方法时可避免 lambda 闭包分配。
    /// </summary>
    /// <typeparam name="TState">状态对象类型，必须为引用类型</typeparam>
    /// <param name="state">状态对象，同时作为 cancelKey</param>
    /// <param name="from">起始值</param>
    /// <param name="to">目标值</param>
    /// <param name="duration">持续时间（秒）</param>
    /// <param name="onUpdate">每帧回调 (state, 当前值)</param>
    /// <param name="ease">缓动曲线</param>
    /// <param name="onComplete">正常完成时回调 (state)</param>
    /// <param name="unscaledTime">是否使用 unscaledDeltaTime</param>
    /// <param name="ct">外部取消令牌</param>
    /// <param name="channel">打断通道</param>
    public static void To<TState>(
        TState state, float from, float to, float duration, Action<TState, float> onUpdate,
        Ease ease = Ease.Linear, Action<TState> onComplete = null,
        bool unscaledTime = false, CancellationToken ct = default,
        Channel channel = Channel.Default) where TState : class
    {
        RunState(state, from, to, duration, FloatLerp, onUpdate, ease, onComplete, unscaledTime, ct, state, channel).Forget();
    }

    /// <summary> 对 Vector3 值插值 + 状态传递，state 自动作为 cancelKey </summary>
    /// <inheritdoc cref="To{TState}(TState, float, float, float, Action{TState, float}, Ease, Action{TState}, bool, CancellationToken, Channel)"/>
    public static void To<TState>(
        TState state, Vector3 from, Vector3 to, float duration, Action<TState, Vector3> onUpdate,
        Ease ease = Ease.Linear, Action<TState> onComplete = null,
        bool unscaledTime = false, CancellationToken ct = default,
        Channel channel = Channel.Default) where TState : class
    {
        RunState(state, from, to, duration, Vector3Lerp, onUpdate, ease, onComplete, unscaledTime, ct, state, channel).Forget();
    }

    /// <summary> 对 Quaternion 值球面插值 + 状态传递，state 自动作为 cancelKey </summary>
    /// <inheritdoc cref="To{TState}(TState, float, float, float, Action{TState, float}, Ease, Action{TState}, bool, CancellationToken, Channel)"/>
    public static void To<TState>(
        TState state, Quaternion from, Quaternion to, float duration, Action<TState, Quaternion> onUpdate,
        Ease ease = Ease.Linear, Action<TState> onComplete = null,
        bool unscaledTime = false, CancellationToken ct = default,
        Channel channel = Channel.Default) where TState : class
    {
        RunState(state, from, to, duration, QuaternionLerp, onUpdate, ease, onComplete, unscaledTime, ct, state, channel).Forget();
    }

    #endregion

    #region MonoBehaviour 绑定

    /// <summary>
    /// 对 float 值插值，并绑定 MonoBehaviour 生命周期。
    /// owner 销毁时自动取消；cancelKey 默认为 owner，同 owner 的新插值会打断上一次。
    /// </summary>
    /// <param name="owner">生命周期宿主，销毁时自动取消插值</param>
    /// <inheritdoc cref="To(float, float, float, Action{float}, Ease, Action, bool, CancellationToken, object, Channel)"/>
    public static void To(
        MonoBehaviour owner, float from, float to, float duration, Action<float> onUpdate,
        Ease ease = Ease.Linear, Action onComplete = null, bool unscaledTime = false,
        object cancelKey = null, Channel channel = Channel.Default)
    {
        Run(from, to, duration, FloatLerp, onUpdate, ease, onComplete, unscaledTime,
            owner.GetCancellationTokenOnDestroy(), cancelKey ?? owner, channel).Forget();
    }

    /// <summary> 对 Vector3 值插值 + 绑定 MonoBehaviour 生命周期 </summary>
    /// <param name="owner">生命周期宿主</param>
    /// <inheritdoc cref="To(float, float, float, Action{float}, Ease, Action, bool, CancellationToken, object, Channel)"/>
    public static void To(
        MonoBehaviour owner, Vector3 from, Vector3 to, float duration, Action<Vector3> onUpdate,
        Ease ease = Ease.Linear, Action onComplete = null, bool unscaledTime = false,
        object cancelKey = null, Channel channel = Channel.Default)
    {
        Run(from, to, duration, Vector3Lerp, onUpdate, ease, onComplete, unscaledTime,
            owner.GetCancellationTokenOnDestroy(), cancelKey ?? owner, channel).Forget();
    }

    /// <summary> 对 Quaternion 值球面插值 + 绑定 MonoBehaviour 生命周期 </summary>
    /// <param name="owner">生命周期宿主</param>
    /// <inheritdoc cref="To(float, float, float, Action{float}, Ease, Action, bool, CancellationToken, object, Channel)"/>
    public static void To(
        MonoBehaviour owner, Quaternion from, Quaternion to, float duration, Action<Quaternion> onUpdate,
        Ease ease = Ease.Linear, Action onComplete = null, bool unscaledTime = false,
        object cancelKey = null, Channel channel = Channel.Default)
    {
        Run(from, to, duration, QuaternionLerp, onUpdate, ease, onComplete, unscaledTime,
            owner.GetCancellationTokenOnDestroy(), cancelKey ?? owner, channel).Forget();
    }

    /// <summary> 对 Color 值插值 + 绑定 MonoBehaviour 生命周期 </summary>
    /// <param name="owner">生命周期宿主</param>
    /// <inheritdoc cref="To(float, float, float, Action{float}, Ease, Action, bool, CancellationToken, object, Channel)"/>
    public static void To(
        MonoBehaviour owner, Color from, Color to, float duration, Action<Color> onUpdate,
        Ease ease = Ease.Linear, Action onComplete = null, bool unscaledTime = false,
        object cancelKey = null, Channel channel = Channel.Default)
    {
        Run(from, to, duration, ColorLerp, onUpdate, ease, onComplete, unscaledTime,
            owner.GetCancellationTokenOnDestroy(), cancelKey ?? owner, channel).Forget();
    }

    /// <summary>
    /// 对 float 值插值 + 状态传递 + 绑定 MonoBehaviour 生命周期。
    /// state 作为 cancelKey；owner 销毁时自动取消。
    /// </summary>
    /// <param name="owner">生命周期宿主</param>
    /// <inheritdoc cref="To{TState}(TState, float, float, float, Action{TState, float}, Ease, Action{TState}, bool, CancellationToken, Channel)"/>
    public static void To<TState>(
        MonoBehaviour owner, TState state,
        float from, float to, float duration, Action<TState, float> onUpdate,
        Ease ease = Ease.Linear, Action<TState> onComplete = null,
        bool unscaledTime = false, Channel channel = Channel.Default) where TState : class
    {
        RunState(state, from, to, duration, FloatLerp, onUpdate, ease, onComplete, unscaledTime,
            owner.GetCancellationTokenOnDestroy(), state, channel).Forget();
    }

    /// <summary> 对 Vector3 值插值 + 状态传递 + 绑定 MonoBehaviour 生命周期 </summary>
    /// <param name="owner">生命周期宿主</param>
    /// <inheritdoc cref="To{TState}(TState, float, float, float, Action{TState, float}, Ease, Action{TState}, bool, CancellationToken, Channel)"/>
    public static void To<TState>(
        MonoBehaviour owner, TState state,
        Vector3 from, Vector3 to, float duration, Action<TState, Vector3> onUpdate,
        Ease ease = Ease.Linear, Action<TState> onComplete = null,
        bool unscaledTime = false, Channel channel = Channel.Default) where TState : class
    {
        RunState(state, from, to, duration, Vector3Lerp, onUpdate, ease, onComplete, unscaledTime,
            owner.GetCancellationTokenOnDestroy(), state, channel).Forget();
    }

    #endregion

    #region Transform 便捷方法

    /// <summary>
    /// 移动 Transform 到目标世界坐标。
    /// 以 target + <see cref="Channel.Position"/> 作为打断键，重复调用自动取消上一次移动。
    /// </summary>
    /// <param name="target">目标 Transform</param>
    /// <param name="to">目标世界坐标</param>
    /// <param name="duration">持续时间（秒）</param>
    /// <param name="ease">缓动曲线</param>
    /// <param name="onComplete">正常完成时回调</param>
    /// <param name="ct">外部取消令牌</param>
    public static void Move(
        Transform target, Vector3 to, float duration,
        Ease ease = Ease.Linear, Action onComplete = null, CancellationToken ct = default)
    {
        RunState(target, target.position, to, duration, Vector3Lerp,
            (t, v) => { if (t != null) t.position = v; },
            ease, onComplete != null ? _ => onComplete() : (Action<Transform>)null,
            false, ct, target, Channel.Position).Forget();
    }

    /// <summary>
    /// 旋转 Transform 到目标世界朝向。
    /// 以 target + <see cref="Channel.Rotation"/> 作为打断键，可与 Move / Scale 并行。
    /// </summary>
    /// <param name="target">目标 Transform</param>
    /// <param name="to">目标世界旋转</param>
    /// <param name="duration">持续时间（秒）</param>
    /// <param name="ease">缓动曲线</param>
    /// <param name="onComplete">正常完成时回调</param>
    /// <param name="ct">外部取消令牌</param>
    public static void Rotate(
        Transform target, Quaternion to, float duration,
        Ease ease = Ease.Linear, Action onComplete = null, CancellationToken ct = default)
    {
        RunState(target, target.rotation, to, duration, QuaternionLerp,
            (t, q) => { if (t != null) t.rotation = q; },
            ease, onComplete != null ? _ => onComplete() : (Action<Transform>)null,
            false, ct, target, Channel.Rotation).Forget();
    }

    /// <summary>
    /// 缩放 Transform 到目标 localScale。
    /// 以 target + <see cref="Channel.Scale"/> 作为打断键，可与 Move / Rotate 并行。
    /// </summary>
    /// <param name="target">目标 Transform</param>
    /// <param name="to">目标 localScale</param>
    /// <param name="duration">持续时间（秒）</param>
    /// <param name="ease">缓动曲线</param>
    /// <param name="onComplete">正常完成时回调</param>
    /// <param name="ct">外部取消令牌</param>
    public static void Scale(
        Transform target, Vector3 to, float duration,
        Ease ease = Ease.Linear, Action onComplete = null, CancellationToken ct = default)
    {
        RunState(target, target.localScale, to, duration, Vector3Lerp,
            (t, v) => { if (t != null) t.localScale = v; },
            ease, onComplete != null ? _ => onComplete() : (Action<Transform>)null,
            false, ct, target, Channel.Scale).Forget();
    }

    #endregion

    #region 内部实现

    // ── 活跃插值注册表：(cancelKey, channel) → 可取消的 CTS ──
    private static readonly Dictionary<(object Key, int Channel), CancellationTokenSource> _active = new();

    /// <summary>
    /// 手动取消指定 cancelKey + channel 上的活跃插值。
    /// </summary>
    /// <param name="cancelKey">打断键，与启动插值时传入的 cancelKey 一致</param>
    /// <param name="channel">打断通道，默认 Default</param>
    public static void Cancel(object cancelKey, Channel channel = Channel.Default)
    {
        if (cancelKey == null) return;

        CancellationTokenSource cts;
        lock (_active)
        {
            if (!_active.TryGetValue((cancelKey, (int)channel), out cts)) return;
            _active.Remove((cancelKey, (int)channel));
        }

        cts.Cancel();
        cts.Dispose();
    }

    /// <summary>
    /// 手动取消指定 cancelKey 上所有 Channel 的活跃插值。
    /// 等价于对 Position / Rotation / Scale / Default 全部调用 <see cref="Cancel"/>。
    /// </summary>
    /// <param name="cancelKey">打断键</param>
    public static void CancelAll(object cancelKey)
    {
        if (cancelKey == null) return;

        List<CancellationTokenSource> toCancel = null;
        lock (_active)
        {
            foreach (var kv in _active)
            {
                if (!ReferenceEquals(kv.Key.Key, cancelKey)) continue;
                toCancel ??= new List<CancellationTokenSource>();
                toCancel.Add(kv.Value);
            }

            if (toCancel == null) return;

            var keysToRemove = new List<(object, int)>();
            foreach (var kv in _active)
            {
                if (ReferenceEquals(kv.Key.Key, cancelKey))
                    keysToRemove.Add(kv.Key);
            }
            foreach (var key in keysToRemove)
                _active.Remove(key);
        }

        foreach (var cts in toCancel)
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    private static CancellationToken AcquireToken(
        object cancelKey, Channel channel, CancellationToken externalCt,
        out CancellationTokenSource ownedCts)
    {
        ownedCts = null;
        if (cancelKey == null) return externalCt;

        var registryKey = (cancelKey, (int)channel);

        lock (_active)
        {
            if (_active.TryGetValue(registryKey, out var prev))
            {
                prev.Cancel();
                prev.Dispose();
                _active.Remove(registryKey);
            }
        }

        ownedCts = externalCt.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(externalCt)
            : new CancellationTokenSource();

        lock (_active)
        {
            _active[registryKey] = ownedCts;
        }

        return ownedCts.Token;
    }

    private static void ReleaseToken(object cancelKey, Channel channel, CancellationTokenSource ownedCts)
    {
        if (cancelKey == null || ownedCts == null) return;

        var registryKey = (cancelKey, (int)channel);
        lock (_active)
        {
            if (_active.TryGetValue(registryKey, out var stored) && ReferenceEquals(stored, ownedCts))
                _active.Remove(registryKey);
        }

        ownedCts.Dispose();
    }

    // ── 缓存的 Lerp 委托，避免每次调用重新分配 ──
    private static readonly Func<float, float, float, float> FloatLerp = Mathf.LerpUnclamped;
    private static readonly Func<Vector2, Vector2, float, Vector2> Vector2Lerp = Vector2.LerpUnclamped;
    private static readonly Func<Vector3, Vector3, float, Vector3> Vector3Lerp = Vector3.LerpUnclamped;
    private static readonly Func<Quaternion, Quaternion, float, Quaternion> QuaternionLerp = Quaternion.SlerpUnclamped;
    private static readonly Func<Color, Color, float, Color> ColorLerp = Color.LerpUnclamped;

    private static async UniTaskVoid Run<T>(
        T from, T to, float duration, Func<T, T, float, T> lerp, Action<T> onUpdate,
        Ease ease, Action onComplete, bool unscaledTime, CancellationToken ct,
        object cancelKey, Channel channel)
    {
        if (onUpdate == null) return;

        CancellationTokenSource ownedCts = null;
        ct = AcquireToken(cancelKey, channel, ct, out ownedCts);

        try
        {
            if (duration <= 0f)
            {
                onUpdate(to);
                onComplete?.Invoke();
                return;
            }

            float elapsed = 0f;

            while (elapsed < duration)
            {
                if (ct.IsCancellationRequested) return;

                float t = Evaluate(ease, elapsed / duration);
                onUpdate(lerp(from, to, t));

                try
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }
                catch (OperationCanceledException) { return; }

                elapsed += unscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            }

            if (ct.IsCancellationRequested) return;

            onUpdate(to);
            onComplete?.Invoke();
        }
        finally
        {
            ReleaseToken(cancelKey, channel, ownedCts);
        }
    }

    private static async UniTaskVoid RunState<TState, T>(
        TState state, T from, T to, float duration, Func<T, T, float, T> lerp,
        Action<TState, T> onUpdate, Ease ease, Action<TState> onComplete,
        bool unscaledTime, CancellationToken ct,
        object cancelKey, Channel channel) where TState : class
    {
        if (onUpdate == null) return;

        CancellationTokenSource ownedCts = null;
        ct = AcquireToken(cancelKey, channel, ct, out ownedCts);

        try
        {
            if (duration <= 0f)
            {
                onUpdate(state, to);
                onComplete?.Invoke(state);
                return;
            }

            float elapsed = 0f;

            while (elapsed < duration)
            {
                if (ct.IsCancellationRequested) return;

                float t = Evaluate(ease, elapsed / duration);
                onUpdate(state, lerp(from, to, t));

                try
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }
                catch (OperationCanceledException) { return; }

                elapsed += unscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            }

            if (ct.IsCancellationRequested) return;

            onUpdate(state, to);
            onComplete?.Invoke(state);
        }
        finally
        {
            ReleaseToken(cancelKey, channel, ownedCts);
        }
    }

    #endregion
}
/*
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UniTaskInterpolator 完整用法示例（取消注释即可编译运行）。
/// </summary>
public class UniTaskInterpolatorExample : MonoBehaviour
{
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private Image fillImage;
    [SerializeField] private RectTransform panelRect;

    private Renderer _renderer;
    private Material _material;
    private CancellationTokenSource _externalCts;

    // ── 热路径零闭包：静态回调 + TState 重载，避免 lambda 分配 ──
    private static void SetAlpha(CanvasGroup cg, float v) { if (cg != null) cg.alpha = v; }
    private static void OnFadeComplete(CanvasGroup cg) { Debug.Log($"[{cg.name}] 淡入完成"); }
    private static void SetLocalScale(Transform t, Vector3 v) { if (t != null) t.localScale = v; }
    private static void SetRotation(Transform t, Quaternion q) { if (t != null) t.rotation = q; }

    void Awake()
    {
        _renderer = GetComponent<Renderer>();
        if (_renderer != null) _material = _renderer.material;
    }

    void OnDestroy()
    {
        _externalCts?.Cancel();
        _externalCts?.Dispose();
    }

    void Start()
    {
        DemoBasicTypes();
        DemoEaseAndComplete();
        DemoCancelKey();
        DemoStateApi();
        DemoMonoBehaviourBinding();
        DemoTransformShortcuts();
        DemoParallelChannels();
        DemoUnscaledTime();
        DemoInstant();
        DemoManualCancel();
        DemoExternalToken();
    }

    // ── 1. 基础类型插值：float / Vector2 / Vector3 / Quaternion / Color ──
    void DemoBasicTypes()
    {
        // float：UI 透明度
        UniTaskInterpolator.To(
            from: 0f, to: 1f, duration: 0.5f,
            onUpdate: v => canvasGroup.alpha = v,
            cancelKey: canvasGroup);

        // Vector2：UI 尺寸
        UniTaskInterpolator.To(
            from: panelRect.sizeDelta, to: new Vector2(400f, 300f), duration: 0.4f,
            onUpdate: v => panelRect.sizeDelta = v,
            ease: UniTaskInterpolator.Ease.OutQuad,
            cancelKey: panelRect);

        // Vector3：任意三维向量（此处演示位移，生产环境更推荐 Move）
        UniTaskInterpolator.To(
            from: transform.position,
            to: transform.position + Vector3.forward * 3f,
            duration: 1f,
            onUpdate: v => transform.position = v,
            cancelKey: transform,
            channel: UniTaskInterpolator.Channel.Position);

        // Quaternion：任意旋转（生产环境更推荐 Rotate）
        UniTaskInterpolator.To(
            from: transform.rotation,
            to: Quaternion.Euler(0f, 90f, 0f),
            duration: 0.8f,
            onUpdate: q => transform.rotation = q,
            cancelKey: transform,
            channel: UniTaskInterpolator.Channel.Rotation);

        // Color：材质颜色
        if (_material != null)
        {
            UniTaskInterpolator.To(
                from: _material.color, to: Color.red, duration: 1f,
                onUpdate: c => _material.color = c,
                cancelKey: _material);
        }

        // Image fillAmount
        if (fillImage != null)
        {
            UniTaskInterpolator.To(
                from: 0f, to: 1f, duration: 2f,
                onUpdate: v => fillImage.fillAmount = v,
                ease: UniTaskInterpolator.Ease.InOutCubic,
                cancelKey: fillImage);
        }
    }

    // ── 2. 缓动曲线 + 完成回调 ──
    void DemoEaseAndComplete()
    {
        UniTaskInterpolator.To(
            from: transform.localScale,
            to: Vector3.one * 1.2f,
            duration: 0.5f,
            onUpdate: v => transform.localScale = v,
            ease: UniTaskInterpolator.Ease.OutBack,
            onComplete: () => Debug.Log("缩放弹跳完成"),
            cancelKey: transform,
            channel: UniTaskInterpolator.Channel.Scale);

        // 直接使用 Evaluate 预览缓动曲线（编辑器工具 / 自定义插值逻辑）
        float eased = UniTaskInterpolator.Evaluate(UniTaskInterpolator.Ease.OutElastic, 0.5f);
        Debug.Log($"OutElastic(0.5) = {eased}");
    }

    // ── 3. cancelKey 自动打断：重复调用无需手动 Cancel ──
    void DemoCancelKey()
    {
        // 第一次移动还在进行时调用第二次，第一次会被自动取消
        UniTaskInterpolator.Move(transform, new Vector3(0f, 5f, 0f), 2f,
            UniTaskInterpolator.Ease.OutCubic);
        UniTaskInterpolator.Move(transform, Vector3.zero, 0.5f,
            UniTaskInterpolator.Ease.OutQuad);

        // cancelKey 为 null 时不启用自动打断（向后兼容）
        UniTaskInterpolator.To(0f, 1f, 1f, v => { /* 无 cancelKey，可多个并行  });
    }

    // ── 4. TState 重载：state 即 cancelKey，配合静态方法零闭包 ──
    void DemoStateApi()
    {
        // float + state
        UniTaskInterpolator.To(
            state: canvasGroup,
            from: 0f, to: 1f, duration: 0.3f,
            onUpdate: SetAlpha,
            onComplete: OnFadeComplete);

        // Vector3 + state + 指定 channel
        UniTaskInterpolator.To(
            state: transform,
            from: Vector3.one, to: Vector3.one * 1.5f, duration: 0.4f,
            onUpdate: SetLocalScale,
            ease: UniTaskInterpolator.Ease.OutBack,
            channel: UniTaskInterpolator.Channel.Scale);

        // Quaternion + state
        UniTaskInterpolator.To(
            state: transform,
            from: transform.rotation,
            to: Quaternion.Euler(0f, 180f, 0f),
            duration: 1f,
            onUpdate: SetRotation,
            channel: UniTaskInterpolator.Channel.Rotation);
    }

    // ── 5. MonoBehaviour 绑定：owner 销毁时自动取消，默认 cancelKey = owner ──
    void DemoMonoBehaviourBinding()
    {
        // float
        UniTaskInterpolator.To(this,
            from: 0f, to: 100f, duration: 3f,
            onUpdate: v => Debug.Log($"进度: {v:F0}"));

        // Vector3
        UniTaskInterpolator.To(this,
            from: transform.position,
            to: transform.position + Vector3.up * 2f,
            duration: 1f,
            onUpdate: v => transform.position = v,
            ease: UniTaskInterpolator.Ease.InOutQuad);

        // Quaternion
        UniTaskInterpolator.To(this,
            from: transform.rotation,
            to: Quaternion.identity,
            duration: 0.5f,
            onUpdate: q => transform.rotation = q);

        // Color
        if (_material != null)
        {
            UniTaskInterpolator.To(this,
                from: _material.color, to: Color.white, duration: 0.5f,
                onUpdate: c => _material.color = c);
        }

        // TState + owner：state 作 cancelKey，owner 销毁时取消
        UniTaskInterpolator.To(this, transform,
            from: Vector3.zero, to: Vector3.one, duration: 0.3f,
            onUpdate: SetLocalScale,
            channel: UniTaskInterpolator.Channel.Scale);

        // 自定义 cancelKey：同 owner 上不同 key 的插值互不干扰
        UniTaskInterpolator.To(this,
            from: 0f, to: 1f, duration: 1f,
            onUpdate: v => canvasGroup.alpha = v,
            cancelKey: canvasGroup);
    }

    // ── 6. Transform 便捷方法：Move / Rotate / Scale ──
    void DemoTransformShortcuts()
    {
        UniTaskInterpolator.Move(transform, new Vector3(10f, 0f, 0f), 1.5f,
            UniTaskInterpolator.Ease.InOutCubic,
            onComplete: () => Debug.Log("移动完成"));

        UniTaskInterpolator.Rotate(transform, Quaternion.Euler(0f, 45f, 0f), 0.8f,
            UniTaskInterpolator.Ease.OutQuad);

        UniTaskInterpolator.Scale(transform, Vector3.one * 0.8f, 0.3f,
            UniTaskInterpolator.Ease.OutBack);
    }

    // ── 7. 不同 Channel 并行：移动 + 旋转 + 缩放同时进行 ──
    void DemoParallelChannels()
    {
        UniTaskInterpolator.Move(transform, Vector3.up * 5f, 1f,
            UniTaskInterpolator.Ease.OutCubic);
        UniTaskInterpolator.Rotate(transform, Quaternion.Euler(0f, 360f, 0f), 1f,
            UniTaskInterpolator.Ease.Linear);
        UniTaskInterpolator.Scale(transform, Vector3.one * 2f, 1f,
            UniTaskInterpolator.Ease.OutBack);
    }

    // ── 8. unscaledTime：暂停界面动画不受 timeScale 影响 ──
    void DemoUnscaledTime()
    {
        UniTaskInterpolator.To(
            from: 0f, to: 1f, duration: 0.5f,
            onUpdate: v => { if (canvasGroup != null) canvasGroup.alpha = v; },
            unscaledTime: true,
            cancelKey: canvasGroup);
    }

    // ── 9. duration <= 0：立即跳到终点并触发 onComplete ──
    void DemoInstant()
    {
        UniTaskInterpolator.To(
            from: 0f, to: 1f, duration: 0f,
            onUpdate: v => canvasGroup.alpha = v,
            onComplete: () => Debug.Log("立即完成"),
            cancelKey: canvasGroup);
    }

    // ── 10. 手动取消：Cancel（单通道）/ CancelAll（全部通道） ──
    void DemoManualCancel()
    {
        UniTaskInterpolator.Move(transform, Vector3.forward * 10f, 5f);
        UniTaskInterpolator.Rotate(transform, Quaternion.Euler(0f, 90f, 0f), 5f);

        // 只取消移动，旋转继续
        UniTaskInterpolator.Cancel(transform, UniTaskInterpolator.Channel.Position);

        // 取消该 Transform 上所有插值
        UniTaskInterpolator.CancelAll(transform);
    }

    // ── 11. 外部 CancellationToken：与自动打断合并 ──
    void DemoExternalToken()
    {
        _externalCts?.Cancel();
        _externalCts?.Dispose();
        _externalCts = new CancellationTokenSource();

        UniTaskInterpolator.Move(
            transform, Vector3.right * 5f, 3f,
            ease: UniTaskInterpolator.Ease.Linear,
            ct: _externalCts.Token);

        // 3 秒后外部取消（或等用户按键）
        CancelAfterDelay(3f).Forget();
    }

    async UniTaskVoid CancelAfterDelay(float seconds)
    {
        await UniTask.Delay((int)(seconds * 1000));
        _externalCts?.Cancel();
    }

    // ── 12. 业务场景示例：点击重定位，自动打断上一次移动 ──
    public void OnShelfClicked(Vector3 targetPos)
    {
        UniTaskInterpolator.Move(
            transform, targetPos, 0.6f,
            UniTaskInterpolator.Ease.OutCubic,
            onComplete: () => Debug.Log("货架定位完成"));
    }

    public void OnHighlightToggle(bool highlight)
    {
        float targetAlpha = highlight ? 0.6f : 1f;
        UniTaskInterpolator.To(
            state: canvasGroup,
            from: canvasGroup.alpha,
            to: targetAlpha,
            duration: 0.2f,
            onUpdate: SetAlpha,
            ease: UniTaskInterpolator.Ease.OutQuad);
    }
}
 */
}
