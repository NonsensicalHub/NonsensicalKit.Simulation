#if (UNITY_EDITOR || UNITY_STANDALONE_WIN) && !UNITY_WEBGL
using System;
using System.Reflection;
using NonsensicalKit.UGUI;
using UnityEngine;

/// <summary>
/// WebBrowser 的 Standalone 对接。通过反射使用 Vuplex，避免 Core asmdef 硬依赖 Plugins 脚本程序集。
/// 宿主需提供 Assets/Plugins/Vuplex，并在 Resources 放置 CanvasWebViewPrefab。
/// </summary>
public partial class WebBrowser
{
    public Component m_WebViewPrefab;

    private static readonly Type CanvasWebViewPrefabType =
        Type.GetType("Vuplex.WebView.CanvasWebViewPrefab, Assembly-CSharp-firstpass")
        ?? Type.GetType("Vuplex.WebView.CanvasWebViewPrefab, Assembly-CSharp")
        ?? Type.GetType("Vuplex.WebView.CanvasWebViewPrefab");

    private partial void Init()
    {
        if (CanvasWebViewPrefabType == null)
        {
            Debug.LogWarning("[WebBrowser] 未检测到 Vuplex.WebView.CanvasWebViewPrefab，请安装 Vuplex 插件。");
            return;
        }

        if (m_WebViewPrefab == null)
        {
            var g = Resources.Load<GameObject>("CanvasWebViewPrefab");
            if (g == null)
            {
                Debug.LogWarning("[WebBrowser] Canvas WebView prefab could not be found.");
                return;
            }

            var gg = Instantiate(g, this.transform);
            var rt = gg.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.Stretch();
            }

            m_WebViewPrefab = gg.GetComponent(CanvasWebViewPrefabType);
            if (m_WebViewPrefab == null)
            {
                Debug.LogWarning("[WebBrowser] CanvasWebViewPrefab component missing on prefab.");
                return;
            }
        }

        var initialUrlProp = CanvasWebViewPrefabType.GetProperty("InitialUrl", BindingFlags.Instance | BindingFlags.Public);
        initialUrlProp?.SetValue(m_WebViewPrefab, URL, null);
    }

    private partial string DealWithUrl(string url)
    {
        return m_urlType switch
        {
            UrlType.Http => url,
            UrlType.StreamingAssets => $"streaming-assets://{url}",
            UrlType.File => $"file://{url}",
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    public partial void LoadUrl(string url)
    {
        if (string.IsNullOrEmpty(url))
        {
            Debug.LogWarning("url is null or empty");
            return;
        }

        URL = url;
        if (m_WebViewPrefab == null || CanvasWebViewPrefabType == null)
        {
            Debug.LogWarning("[WebBrowser] WebView not ready.");
            return;
        }

        var webViewProp = CanvasWebViewPrefabType.GetProperty("WebView", BindingFlags.Instance | BindingFlags.Public);
        var webView = webViewProp?.GetValue(m_WebViewPrefab, null);
        if (webView == null)
        {
            Debug.LogWarning("[WebBrowser] WebView instance is null (may still be initializing).");
            return;
        }

        var load = webView.GetType().GetMethod("LoadUrl", new[] { typeof(string) });
        load?.Invoke(webView, new object[] { URL });
    }
}
#endif
