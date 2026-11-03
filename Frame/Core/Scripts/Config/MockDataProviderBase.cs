using NonsensicalKit.Core;
using NonsensicalKit.Core.Service;
using NonsensicalKit.Core.Service.Config;
using UnityEngine;

/// <summary>
/// Mock / Demo 数据提供者基类：仅在 <see cref="TwinDataSource.IsOffline"/> 时启用。
/// 在线模式由真实接口 Binder / Request 接管。
/// 会监听 <see cref="TwinDataSource.ModeChanged"/>，运行时切换时自动启停。
/// </summary>

namespace Frame.Core.Config
{
public abstract class MockDataProviderBase : NonsensicalMono
{
    [SerializeField, Tooltip("为 true 时即使在线也启用 Mock（仅调试用）")]
    private bool m_forceEnable;

    private bool _modeReady;
    private bool _started;
    private bool _mockActive;

    protected virtual void Awake()
    {
        TwinDataSource.ModeChanged += OnModeChanged;
        ServiceCore.SafeGet<ConfigService>(OnConfigReady);
    }

    protected virtual void Start()
    {
        _started = true;
        TryActivate();
    }

    protected override void OnDestroy()
    {
        TwinDataSource.ModeChanged -= OnModeChanged;
        if (_mockActive)
        {
            _mockActive = false;
            OnMockDisabled();
        }

        base.OnDestroy();
    }

    private void OnConfigReady(ConfigService config)
    {
        TwinDataSource.TryInit(config, this);
        _modeReady = true;
        TryActivate();
    }

    private void OnModeChanged(TwinDataSourceMode _)
    {
        TryActivate();
    }

    private void TryActivate()
    {
        if (!_modeReady || !_started)
            return;

        bool shouldEnable = ShouldEnableMock();
        if (shouldEnable == _mockActive)
            return;

        _mockActive = shouldEnable;
        if (shouldEnable)
            OnMockEnabled();
        else
            OnMockDisabled();
    }

    protected bool ShouldEnableMock() => m_forceEnable || TwinDataSource.IsOffline;

    /// <summary>离线（或强制）时注册 Handler / 启动定时灌数。</summary>
    protected abstract void OnMockEnabled();

    /// <summary>在线时跳过 Mock；运行时切回在线时做清理。</summary>
    protected virtual void OnMockDisabled()
    {
        Debug.Log($"[{GetType().Name}] 在线模式，跳过 Mock 数据。", this);
    }
}
}
