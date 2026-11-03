using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 子场景跳转封装：统一走 <see cref="SubSceneManager"/>，场景名须已在 SubSceneConfig 中声明。
/// </summary>

namespace Frame.Core.SubScene
{
public class SubSceneJump : MonoBehaviour
{
    [Header("Inspector / UnityEvent 无参调用")]
    [Tooltip("LoadConfiguredScene 要加载的子场景名")]
    [SerializeField] private string loadTargetSceneName;
    [Tooltip("UnloadConfiguredScene 要卸载的子场景名")]
    [SerializeField] private string unloadTargetSceneName;
    [Tooltip("UnloadConfiguredScene 是否强制卸载（Persistent 场景需要开启）")]
    [SerializeField] private bool unloadForce;

    [Tooltip("SwitchConfiguredScene 要卸载的子场景名")]
    [SerializeField] private string switchFromSceneName;
    [Tooltip("SwitchConfiguredScene 要加载的子场景名")]
    [SerializeField] private string switchToSceneName;

    [Header("加载方式")]
    [Tooltip("勾选：配置方法走 UniTask 异步接口（WebGL 安全）；取消：走回调接口（由 SubSceneManager.LoadBackend 决定）")]
    [SerializeField] private bool useAsyncLoad;

    [Tooltip("跳转（加载/切换/卸载）开始前触发")]
    [SerializeField] private UnityEvent m_beforeJump;
    [Tooltip("跳转（加载/切换/卸载）完成后触发")]
    [SerializeField] private UnityEvent m_afterJump;

    /// <summary>加载子场景（Additive，依赖由 SubSceneManager 处理）</summary>
    public void LoadScene(string sceneName, Action onComplete = null, Action<float> onProgress = null)
    {
        if (!TryGetManager(out var mgr)) return;
        InvokeBeforeJump();
        mgr.LoadScene(sceneName, onComplete, onProgress);
    }

    /// <summary>使用 Inspector 中的 loadTargetSceneName 加载，完成后触发 m_afterJump</summary>
    public void LoadConfiguredScene()
    {
        if (string.IsNullOrEmpty(loadTargetSceneName))
        {
            Debug.LogError("[SubSceneJump] loadTargetSceneName 未配置");
            return;
        }
        if (useAsyncLoad)
            LoadSceneAsync(loadTargetSceneName).Forget();
        else
            LoadScene(loadTargetSceneName, InvokeAfterJump);
    }

    /// <summary>卸载子场景（Persistent 场景需 force 才真正卸载）</summary>
    public void UnloadScene(string sceneName, bool force = false, Action onComplete = null)
    {
        if (!TryGetManager(out var mgr)) return;
        InvokeBeforeJump();
        mgr.UnloadScene(sceneName, force, onComplete);
    }

    /// <summary>使用 Inspector 中的 unloadTargetSceneName 卸载，完成后触发 m_afterJump</summary>
    public void UnloadConfiguredScene()
    {
        if (string.IsNullOrEmpty(unloadTargetSceneName))
        {
            Debug.LogError("[SubSceneJump] unloadTargetSceneName 未配置");
            return;
        }
        if (useAsyncLoad)
            UnloadSceneAsync(unloadTargetSceneName, unloadForce).Forget();
        else
            UnloadScene(unloadTargetSceneName, unloadForce, InvokeAfterJump);
    }

    /// <summary>切换子场景：强制卸载旧场景后加载新场景</summary>
    public void SwitchScene(string oldScene, string newScene, Action onComplete = null, Action<float> onProgress = null)
    {
        if (!TryGetManager(out var mgr)) return;
        InvokeBeforeJump();
        mgr.SwitchScene(oldScene, newScene, onComplete, onProgress);
    }

    /// <summary>使用 Inspector 中的 switchFromSceneName / switchToSceneName 切换，完成后触发 m_afterJump</summary>
    public void SwitchConfiguredScene()
    {
        if (string.IsNullOrEmpty(switchFromSceneName) || string.IsNullOrEmpty(switchToSceneName))
        {
            Debug.LogError("[SubSceneJump] switchFromSceneName 或 switchToSceneName 未配置");
            return;
        }
        if (useAsyncLoad)
            SwitchSceneAsync(switchFromSceneName, switchToSceneName).Forget();
        else
            SwitchScene(switchFromSceneName, switchToSceneName, InvokeAfterJump);
    }

    /// <summary>UniTask 加载，完成后触发 m_afterJump（WebGL 安全）</summary>
    public UniTask LoadSceneAsync(string sceneName, IProgress<float> onProgress = null,
                                  CancellationToken cancellationToken = default)
    {
        if (!TryGetManager(out var mgr))
            return UniTask.CompletedTask;
        InvokeBeforeJump();
        return mgr.LoadSceneAsync(sceneName, onProgress, cancellationToken)
            .ContinueWith(InvokeAfterJump);
    }

    /// <summary>UniTask 卸载，完成后触发 m_afterJump（WebGL 安全）</summary>
    public UniTask UnloadSceneAsync(string sceneName, bool force = false,
                                    CancellationToken cancellationToken = default)
    {
        if (!TryGetManager(out var mgr))
            return UniTask.CompletedTask;
        InvokeBeforeJump();
        return mgr.UnloadSceneAsync(sceneName, force, cancellationToken)
            .ContinueWith(InvokeAfterJump);
    }

    /// <summary>UniTask 切换，完成后触发 m_afterJump（WebGL 安全）</summary>
    public UniTask SwitchSceneAsync(string oldScene, string newScene, IProgress<float> onProgress = null,
                                    CancellationToken cancellationToken = default)
    {
        if (!TryGetManager(out var mgr))
            return UniTask.CompletedTask;
        InvokeBeforeJump();
        return mgr.SwitchSceneAsync(oldScene, newScene, onProgress, cancellationToken)
            .ContinueWith(InvokeAfterJump);
    }

    void InvokeBeforeJump() => m_beforeJump?.Invoke();
    void InvokeAfterJump() => m_afterJump?.Invoke();

    static bool TryGetManager(out SubSceneManager mgr)
    {
        mgr = SubSceneManager.Instance;
        if (mgr != null) return true;
        Debug.LogError("[SubSceneJump] SubSceneManager.Instance 为空，请确认常驻场景里已挂载 SubSceneManager。");
        return false;
    }
}
}
