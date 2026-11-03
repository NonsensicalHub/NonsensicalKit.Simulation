#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using Frame.Core.Abstractions;
using Frame.Core.Component;
using NonsensicalKit.UGUI;
using NonsensicalKit.WebGL;
using UnityEngine;

namespace Frame.Core.WebGL
{
internal sealed class WebGlWebBrowserBackend : IWebBrowserWebGlBackend
{
    private sealed class State
    {
        public RectTransform Rect;
        public string CanvasId;
        public readonly Vector3[] Corners = new Vector3[2];
        public Vector3 Min;
        public Vector3 Max;
    }

    private readonly Dictionary<int, State> _states = new();

    public string DealWithUrl(WebBrowser browser, string rawUrl)
    {
        return browser.CurrentUrlType switch
        {
            UrlType.Http => rawUrl,
            UrlType.StreamingAssets => Path.Combine(Application.streamingAssetsPath, rawUrl),
            UrlType.File => rawUrl,
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    public void OnInit(WebBrowser browser)
    {
        var state = GetOrCreate(browser);
        state.Rect = browser.GetComponent<RectTransform>();
        state.CanvasId = Guid.NewGuid().ToString();
        WebIframe.Instance.Create(state.CanvasId);
    }

    public void OnLoadUrl(WebBrowser browser, string url)
    {
        var state = GetOrCreate(browser);
        if (string.IsNullOrEmpty(url) || state.Rect == null)
        {
            Debug.LogWarning("url is null or empty or 画布不存在 ");
            return;
        }

        if (string.IsNullOrEmpty(state.CanvasId))
            state.CanvasId = Guid.NewGuid().ToString();

        browser.URL = url;
        UpdatePoints(state);
        WebIframe.Instance.Change(state.Min.x, state.Min.y, state.Max.x, state.Max.y, browser.URL, state.CanvasId);
    }

    public void OnEnable(WebBrowser browser)
    {
        if (!_states.TryGetValue(browser.GetInstanceID(), out var state))
            return;
        if (string.IsNullOrEmpty(browser.URL) || state.Rect == null)
            return;
        UpdatePoints(state);
        WebIframe.Instance.Change(state.Min.x, state.Min.y, state.Max.x, state.Max.y, browser.URL, state.CanvasId);
    }

    public void OnDisable(WebBrowser browser)
    {
        if (!_states.TryGetValue(browser.GetInstanceID(), out var state))
            return;
        if (!string.IsNullOrEmpty(state.CanvasId) && WebIframe.Instance != null)
            WebIframe.Instance.Close(state.CanvasId);
    }

    public void OnRectTransformDimensionsChange(WebBrowser browser)
    {
        OnEnable(browser);
    }

    private State GetOrCreate(WebBrowser browser)
    {
        var id = browser.GetInstanceID();
        if (!_states.TryGetValue(id, out var state))
        {
            state = new State();
            _states[id] = state;
        }

        return state;
    }

    private static void UpdatePoints(State state)
    {
        if (state.Rect == null)
            return;
        state.Rect.GetWorldMinMax(ref state.Corners);
        state.Min = new Vector3(state.Corners[0].x / Screen.width, state.Corners[0].y / Screen.height);
        state.Max = new Vector3(state.Corners[1].x / Screen.width, state.Corners[1].y / Screen.height);
    }
}
}
#else
using Frame.Core.Abstractions;
using Frame.Core.Component;

namespace Frame.Core.WebGL
{
/// <summary>非 WebGL 播放器下占位，满足注册需求。</summary>
internal sealed class WebGlWebBrowserBackend : IWebBrowserWebGlBackend
{
    public void OnInit(WebBrowser browser) { }
    public void OnLoadUrl(WebBrowser browser, string url) { }
    public string DealWithUrl(WebBrowser browser, string rawUrl) => rawUrl;
    public void OnEnable(WebBrowser browser) { }
    public void OnDisable(WebBrowser browser) { }
    public void OnRectTransformDimensionsChange(WebBrowser browser) { }
}
}
#endif
