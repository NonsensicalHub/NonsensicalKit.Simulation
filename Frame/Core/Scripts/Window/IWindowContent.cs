/// <summary>
/// 所有可加载到窗口容器中的预制体内容都应实现此接口。
/// 窗口在实例化预制体后会调用 OnContentInit 传入参数，
/// 关闭前会调用 OnContentClose 用于清理。
/// </summary>
public interface IWindowContent
{
    /// <summary>
    /// 内容初始化，接收外部传入的参数
    /// </summary>
    /// <param name="args">自定义参数，由调用方决定含义</param>
    void OnContentInit(object args);

    /// <summary>
    /// 窗口关闭前调用，用于清理资源、保存数据等
    /// </summary>
    /// <returns>返回 true 表示允许关闭，false 表示阻止关闭</returns>
    bool OnContentClose();
}

/// <summary>
/// 可选接口：窗口内容可接收容器分配的实例句柄，
/// 用于构造实例级别的事件频道 ID，避免多开窗口串消息。
/// </summary>
public interface IWindowHandleReceiver
{
    string WindowHandle { get; set; }
    void SetWindowHandle(string windowHandle);
}
