using System;
namespace Frame.Core.Abstractions
{
    /// <summary>
    /// 可选插件适配器的轻量注册表（目前仅 AGV 回放后端）。
    /// 高亮由 FocusPointManager.HighlightHandler（Func）外部注入，不经本表。
    /// </summary>
    public static class FrameOptionalServices
    {
        public static IAgvStatusTapePlaybackBackend AgvTape { get; private set; }

        public static void Register(IAgvStatusTapePlaybackBackend backend)
        {
            AgvTape = backend;
        }

        public static void Unregister(IAgvStatusTapePlaybackBackend backend)
        {
            if (ReferenceEquals(AgvTape, backend))
            {
                AgvTape = null;
            }
        }
    }
}
