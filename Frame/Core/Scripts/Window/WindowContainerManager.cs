using System.Collections.Generic;
using NonsensicalKit.Core;
using UnityEngine;

/// <summary>
/// 全局窗口容器管理器。
/// 负责创建窗口实例、打开/关闭窗口，支持单例窗口与多实例窗口。
/// </summary>
public class WindowContainerManager : MonoBehaviour
{
    [Header("窗口容器预制体")]
    [Tooltip("该预制体根节点上需挂载 WindowContainer 组件，并按图中层级结构搭建 UI")]
    [SerializeField] private GameObject m_windowContainerPrefab;

    [SerializeField, Tooltip("每次创建窗口时新建窗口容器")]
    private bool m_createNewWhenInstantiate = false;

    [SerializeField] private string m_windowName;

    private readonly List<WindowContainer> _createdWindows = new();
    private readonly List<WindowContainer> _openedWindows = new();

    private void Awake()
    {
        if (string.IsNullOrEmpty(m_windowName))
        {
            Debug.LogWarning("未配置窗口名称");
            m_windowName = "WindowContainer_" + this.gameObject.name;
        }

        IOCC.Set<WindowContainerManager>(m_windowName, this);
    }

    private void OnDestroy()
    {
        foreach (var window in _createdWindows)
        {
            if (window != null)
            {
                window.OnClosed -= HandleWindowClosed;
                window.OnFocusRequested -= HandleWindowFocusRequested;
            }
        }
    }

    /// <summary>
    /// 打开一个窗口，加载指定的内容预制体
    /// </summary>
    /// <param name="config">窗口配置（大小、标题、图标等）</param>
    /// <param name="contentPrefab">内容预制体（需挂载 IWindowContent）</param>
    /// <param name="contentArgs">传给内容的自定义参数</param>
    /// <returns>打开的窗口容器实例</returns>
    public WindowContainer OpenWindow(WindowConfig config, GameObject contentPrefab, object contentArgs = null)
    {
        if (contentPrefab == null)
        {
            Debug.LogError("[WindowContainerManager] 打开窗口失败，contentPrefab 为空。");
            return null;
        }


        if (config.IsInstance)
        {
            var existing = FindOpenedWindowByPrefab(contentPrefab);
            if (existing != null)
            {
                if (config.AlwaysInit)
                {
                    existing.Open(config, contentPrefab, contentArgs);
                }
                else
                {
                    existing.ApplyConfig(config,contentArgs);
                }

                FocusWindow(existing);
                return existing;
            }
        }

        //允许多窗口
        var targetWindow = CreateOrReuseWindow();
        if (targetWindow == null)
        {
            return null;
        }

        targetWindow.Open(config, contentPrefab, contentArgs);
        RegisterOpenedWindow(targetWindow);
        FocusWindow(targetWindow);
        return targetWindow;
    }


    /// <summary>
    /// 使用默认配置打开窗口
    /// </summary>
    public WindowContainer OpenWindow(GameObject contentPrefab, object contentArgs = null)
    {
        return OpenWindow(WindowConfig.Default, contentPrefab, contentArgs);
    }

    /// <summary>
    /// 关闭当前活动窗口
    /// </summary>
    public void CloseCurrentWindow()
    {
        for (var i = _openedWindows.Count - 1; i >= 0; i--)
        {
            var window = _openedWindows[i];
            if (window != null && window.IsOpened)
            {
                window.Close();
                return;
            }
        }
    }

    /// <summary>当前是否有窗口打开</summary>
    public bool HasActiveWindow => _openedWindows.Count > 0;

    public void CloseAllWindows()
    {
        var openedSnapshot = new List<WindowContainer>(_openedWindows);
        foreach (var window in openedSnapshot)
        {
            if (window != null && window.IsOpened)
            {
                window.Close();
            }
        }
    }

    private WindowContainer CreateOrReuseWindow()
    {
        //复用窗口
        if (m_createNewWhenInstantiate == false)
        {
            for (var i = 0; i < _createdWindows.Count; i++)
            {
                var window = _createdWindows[i];
                if (window != null && !window.IsOpened)
                {
                    window.gameObject.SetActive(true);
                    return window;
                }
            }
        }

        var windowGo = Instantiate(m_windowContainerPrefab, this.transform);
        var created = windowGo.GetComponent<WindowContainer>();
        if (created == null)
        {
            Debug.LogError("[WindowContainerManager] 窗口预制体上缺少 WindowContainer 组件！");
            Destroy(windowGo);
            return null;
        }

        created.OnClosed += HandleWindowClosed;
        created.OnFocusRequested += HandleWindowFocusRequested;
        _createdWindows.Add(created);
        created.gameObject.SetActive(true);
        return created;
    }

    private WindowContainer FindOpenedWindowByPrefab(GameObject contentPrefab)
    {
        foreach (var window in _openedWindows)
        {
            if (window != null && window.IsOpened && window.ContentPrefab == contentPrefab)
            {
                return window;
            }
        }

        return null;
    }

    private void RegisterOpenedWindow(WindowContainer window)
    {
        _openedWindows.Remove(window);
        _openedWindows.Add(window);
    }

    private void HandleWindowClosed(WindowContainer closed)
    {
        _openedWindows.Remove(closed);
        closed.SetFocusState(false);

        if (_openedWindows.Count > 0)
        {
            FocusWindow(_openedWindows[^1]);
        }
    }

    private void HandleWindowFocusRequested(WindowContainer window)
    {
        if (window == null || !window.IsOpened)
            return;

        FocusWindow(window);
    }

    private void FocusWindow(WindowContainer target)
    {
        if (target == null)
            return;

        foreach (var window in _openedWindows)
        {
            if (window == null)
                continue;

            window.SetFocusState(window == target);
        }

        target.transform.SetAsLastSibling();
        RegisterOpenedWindow(target);
    }
}
