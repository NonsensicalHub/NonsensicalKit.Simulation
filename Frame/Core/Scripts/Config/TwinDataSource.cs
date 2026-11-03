using System;
using NonsensicalKit.Core.Service.Config;
using UnityEngine;

/// <summary>
/// 全局数据源模式（与 URL 无关）：离线走本地 Demo/Mock，在线才走真实接口。
/// 配置资产 <see cref="TwinDataSourceConfig"/> 只决定启动默认值；
/// 运行时用 <see cref="SetMode"/> 自由切换，不会写回配置。
/// </summary>

namespace Frame.Core.Config
{
public static class TwinDataSource
{
    /// <summary>当前运行时模式。</summary>
    public static TwinDataSourceMode Mode { get; private set; } = TwinDataSourceMode.Offline;

    /// <summary>配置中的启动默认模式（读配置后固定；当前 Mode 可由 SetMode 覆盖）。</summary>
    public static TwinDataSourceMode DefaultMode { get; private set; } = TwinDataSourceMode.Offline;

    public static bool IsOnline => Mode == TwinDataSourceMode.Online;

    public static bool IsOffline => Mode == TwinDataSourceMode.Offline;

    /// <summary>已有可用模式（来自配置或运行时 SetMode）。</summary>
    public static bool IsReady { get; private set; }

    /// <summary>运行时模式变化时触发（Init 应用默认值时不触发）。</summary>
    public static event Action<TwinDataSourceMode> ModeChanged;

    private static bool _configApplied;

    /// <summary>
    /// 用配置记录默认模式。首次且尚未 SetMode 时，同时把当前 Mode 设为默认值。
    /// 已应用过配置后再次调用无效；若此前已 SetMode，只写入 DefaultMode，不覆盖当前 Mode。
    /// </summary>
    public static void Init(TwinDataSourceData data)
    {
        TwinDataSourceMode configMode = data != null ? data.Mode : TwinDataSourceMode.Offline;

        if (_configApplied)
        {
            return;
        }

        DefaultMode = configMode;
        _configApplied = true;

        if (!IsReady)
        {
            Mode = configMode;
            IsReady = true;
        }
    }

    /// <summary>
    /// 从 ConfigService 读取配置并初始化默认模式。
    /// 找不到配置时默认离线，并打警告。已应用过配置则直接返回 true。
    /// </summary>
    public static bool TryInit(ConfigService config, UnityEngine.Object context = null)
    {
        if (_configApplied)
        {
            return true;
        }

        if (config != null && config.TryGetConfig<TwinDataSourceData>(out var data) && data != null)
        {
            Init(data);
            return true;
        }

        DefaultMode = TwinDataSourceMode.Offline;
        _configApplied = true;
        if (!IsReady)
        {
            Mode = TwinDataSourceMode.Offline;
            IsReady = true;
        }

        Debug.LogWarning(
            "[TwinDataSource] 未找到 TwinDataSourceData 配置，按离线（Demo/Mock）默认模式处理。",
            context);
        return false;
    }

    /// <summary>
    /// 运行时切换数据源模式（不写回 Config）。切换后会触发 <see cref="ModeChanged"/>。
    /// </summary>
    public static void SetMode(TwinDataSourceMode mode)
    {
        if (IsReady && Mode == mode)
        {
            return;
        }

        Mode = mode;
        IsReady = true;
        Debug.Log($"[TwinDataSource] 运行时模式 → {mode}");
        ModeChanged?.Invoke(mode);
    }

    /// <summary>恢复为配置中的默认模式。</summary>
    public static void ResetToDefault()
    {
        SetMode(DefaultMode);
    }
}
}
