using System;
using System.Collections.Generic;
using Framework.Core;
using HikvisionAgv;
using NonsensicalKit.Core;
using UnityEngine;

/// <summary>
/// Samples：海康 AGV JSONL 回放后端。挂到场景任意物体即可（建议与 AgvStatusTapePlayer 同物体）。
/// 依赖宿主工程 HikvisionAgv（Network 的 StatusTape / StatusRequest + Runtime 事件与 DTO）。
/// </summary>
[DisallowMultipleComponent]
public class HikvisionAgvTapeBackend : MonoBehaviour, IAgvStatusTapePlaybackBackend
{
    [SerializeField] private bool m_autoRegister = true;
    [SerializeField] private HikvisionAgvStatusRequest m_statusRequest;

    private void OnEnable()
    {
        if (m_autoRegister)
        {
            FrameOptionalServices.Register((IAgvStatusTapePlaybackBackend)this);
        }
    }

    private void OnDisable()
    {
        if (m_autoRegister)
        {
            FrameOptionalServices.Unregister((IAgvStatusTapePlaybackBackend)this);
        }
    }

    public bool TryLoadFrames(string text, out List<IAgvStatusTapeFrame> frames, out string error)
    {
        frames = new List<IAgvStatusTapeFrame>();
        var list = HikvisionAgvStatusTape.LoadFromText(text, out _, out error);
        if (list == null)
        {
            return false;
        }

        for (int i = 0; i < list.Count; i++)
        {
            frames.Add(new FrameAdapter(list[i]));
        }

        return true;
    }

    public void PublishFrame(IAgvStatusTapeFrame frame, string fallbackMap)
    {
        if (frame is not FrameAdapter adapter || adapter.Source == null)
            return;

        var source = adapter.Source;
        if (!source.ok || source.data == null || source.data.Count == 0)
            return;

        string map = source.map;
        if (string.IsNullOrEmpty(map))
            map = fallbackMap;

        IOCC.Publish(HikvisionAgvEvents.StatusRaw, map, source.data);
    }

    public IDisposable SuspendLiveStatusRequest()
    {
        if (m_statusRequest == null)
        {
            m_statusRequest = FindFirstObjectByType<HikvisionAgvStatusRequest>();
        }

        if (m_statusRequest == null)
            return null;

        return new StatusRequestSuspend(m_statusRequest);
    }

    private sealed class StatusRequestSuspend : IDisposable
    {
        private readonly HikvisionAgvStatusRequest _request;
        private readonly bool _wasEnabled;
        private bool _disposed;

        public StatusRequestSuspend(HikvisionAgvStatusRequest request)
        {
            _request = request;
            _wasEnabled = request.enabled;
            if (_wasEnabled)
                request.enabled = false;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            if (_request != null && _wasEnabled)
                _request.enabled = true;
        }
    }

    private sealed class FrameAdapter : IAgvStatusTapeFrame
    {
        public HikvisionAgvStatusTape.Frame Source { get; }

        public FrameAdapter(HikvisionAgvStatusTape.Frame source)
        {
            Source = source;
            if (source?.data == null || source.data.Count == 0)
            {
                DataCount = 0;
                PositionSignature = string.Empty;
                return;
            }

            DataCount = source.data.Count;
            var parts = new List<string>(source.data.Count);
            for (int i = 0; i < source.data.Count; i++)
            {
                var d = source.data[i];
                if (d == null || string.IsNullOrEmpty(d.robotCode))
                    continue;
                parts.Add($"{d.robotCode}:{d.posX},{d.posY}");
            }

            parts.Sort(StringComparer.Ordinal);
            PositionSignature = string.Join("|", parts);
        }

        public long UnixMs => Source?.unixMs ?? 0;
        public string Time => Source?.time;
        public string Map => Source?.map;
        public bool Ok => Source != null && Source.ok;
        public int DataCount { get; }
        public string PositionSignature { get; }
    }
}
