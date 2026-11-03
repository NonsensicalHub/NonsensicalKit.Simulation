#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using Frame.Core.Abstractions;
using UnityEngine;

namespace Frame.Core.Component
{
/// <summary>
/// WebGL 平台桩：转发到 FrameOptionalServices.WebBrowserWebGl。
/// 真实 WebIframe 调用在 Frame.Core.WebGL（安装 nonsensicalkit.webgl 后由 versionDefines 自动编译）。
/// </summary>
public partial class WebBrowser
{
    private partial string DealWithUrl(string url)
    {
        var backend = FrameOptionalServices.WebBrowserWebGl;
        if (backend != null)
            return backend.DealWithUrl(this, url);

        return m_urlType switch
        {
            UrlType.Http => url,
            UrlType.StreamingAssets => System.IO.Path.Combine(Application.streamingAssetsPath, url),
            UrlType.File => url,
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private partial void Init()
    {
        var backend = FrameOptionalServices.WebBrowserWebGl;
        if (backend == null)
        {
            Debug.LogWarning("[WebBrowser] 未注册 WebGL 后端。请安装 com.nonsensicallab.nonsensicalkit.webgl（Frame.Core.WebGL 会自动启用）。");
            return;
        }

        backend.OnInit(this);
    }

    public partial void LoadUrl(string url)
    {
        FrameOptionalServices.WebBrowserWebGl?.OnLoadUrl(this, url);
    }

    private void OnEnable() => FrameOptionalServices.WebBrowserWebGl?.OnEnable(this);

    private void OnDisable() => FrameOptionalServices.WebBrowserWebGl?.OnDisable(this);

    private void OnRectTransformDimensionsChange() =>
        FrameOptionalServices.WebBrowserWebGl?.OnRectTransformDimensionsChange(this);
}
}
#endif
