using Frame.Core.Abstractions;
using UnityEngine;

namespace Frame.Core.WebGL
{
/// <summary>
/// 安装 nonsensicalkit.webgl 后本程序集由 versionDefines 自动编译并注册硬引用后端。
/// </summary>
internal static class WebGlOptionalBootstrap
{
    private static readonly WebGlMqttBackend Mqtt = new();
    private static readonly WebGlWebBrowserBackend Browser = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Register()
    {
        FrameOptionalServices.Register((IWebMqttBackend)Mqtt);
        FrameOptionalServices.Register((IWebBrowserWebGlBackend)Browser);
    }
}
}
