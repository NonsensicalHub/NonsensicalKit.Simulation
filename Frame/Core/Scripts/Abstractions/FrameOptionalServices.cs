using System;

namespace Frame.Core.Abstractions
{
    /// <summary>
    /// 可选插件适配器的轻量注册表。
    /// 高亮由 FocusPointManager.HighlightHandler（Func）外部注入，不经本表。
    /// WebGL 后端由 Frame.Core.WebGL（versionDefines 自动启用）注册。
    /// </summary>
    public static class FrameOptionalServices
    {
        public static IAgvStatusTapePlaybackBackend AgvTape { get; private set; }
        public static IWebMqttBackend WebMqtt { get; private set; }
        public static IWebBrowserWebGlBackend WebBrowserWebGl { get; private set; }

        public static void Register(IAgvStatusTapePlaybackBackend backend) => AgvTape = backend;

        public static void Unregister(IAgvStatusTapePlaybackBackend backend)
        {
            if (ReferenceEquals(AgvTape, backend)) AgvTape = null;
        }

        public static void Register(IWebMqttBackend backend) => WebMqtt = backend;

        public static void Unregister(IWebMqttBackend backend)
        {
            if (ReferenceEquals(WebMqtt, backend)) WebMqtt = null;
        }

        public static void Register(IWebBrowserWebGlBackend backend) => WebBrowserWebGl = backend;

        public static void Unregister(IWebBrowserWebGlBackend backend)
        {
            if (ReferenceEquals(WebBrowserWebGl, backend)) WebBrowserWebGl = null;
        }
    }
}
