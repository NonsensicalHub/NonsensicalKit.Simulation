using System;
using System.Collections.Generic;
namespace Frame.Core.Abstractions
{
    /// <summary>AGV JSONL 回放帧（与具体插件 DTO 解耦）。</summary>
    public interface IAgvStatusTapeFrame
    {
        long UnixMs { get; }
        string Time { get; }
        string Map { get; }
        bool Ok { get; }
        int DataCount { get; }
        string PositionSignature { get; }
    }

    /// <summary>
    /// AGV 状态带回放后端：解析 JSONL、发布实况同款事件、可选暂停实况请求。
    /// 实现放在 Samples（可引用 HikvisionAgv）。
    /// </summary>
    public interface IAgvStatusTapePlaybackBackend
    {
        bool TryLoadFrames(string text, out List<IAgvStatusTapeFrame> frames, out string error);

        void PublishFrame(IAgvStatusTapeFrame frame, string fallbackMap);

        /// <summary>暂停实况 AGV 状态请求；无目标时返回 null。</summary>
        IDisposable SuspendLiveStatusRequest();
    }
}
