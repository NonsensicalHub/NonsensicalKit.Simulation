using System;
using UnityEngine;

/// <summary>
/// 窗口配置参数，用于控制窗口的大小、标题、图标等
/// </summary>
[Serializable]
public struct WindowConfig
{
    /// <summary>窗口标题文本</summary>
    public string Title;

    /// <summary>窗口标题图标（可选，传 null 则隐藏图标）</summary>
    public Sprite Icon;

    /// <summary>窗口宽度（像素）</summary>
    public float Width;

    /// <summary>窗口高度（像素）</summary>
    public float Height;


    /// <summary>关闭窗口时的回调（由外部注册，关闭时自动触发）</summary>
    public Action OnCloseCallback;

    /// <summary>
    /// 是否允许全屏切换。
    /// true 时标题栏显示全屏按钮，用户可点击切换全屏/还原；
    /// false 时隐藏全屏按钮，窗口固定为 Width × Height。
    /// </summary>
    public bool AllowFullscreen;

    /// <summary>
    /// 只允许一个同类窗口
    /// </summary>
    public bool IsInstance;

    public bool AlwaysInit;

    /// <summary>默认配置</summary>
    public static WindowConfig Default => new WindowConfig
    {
        Title = "窗口",
        Icon = null,
        Width = 800f,
        Height = 600f,
        AllowFullscreen = false,
        IsInstance = true,
        AlwaysInit = true,
        OnCloseCallback = null
    };
}
