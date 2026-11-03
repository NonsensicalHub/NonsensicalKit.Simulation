namespace Frame.Core.Abstractions
{
    /// <summary>WebGL 内嵌页后端；由 Frame.Core.WebGL 在检测到 webgl 包时注册。</summary>
    public interface IWebBrowserWebGlBackend
    {
        void OnInit(Frame.Core.Component.WebBrowser browser);
        void OnLoadUrl(Frame.Core.Component.WebBrowser browser, string url);
        string DealWithUrl(Frame.Core.Component.WebBrowser browser, string rawUrl);
        void OnEnable(Frame.Core.Component.WebBrowser browser);
        void OnDisable(Frame.Core.Component.WebBrowser browser);
        void OnRectTransformDimensionsChange(Frame.Core.Component.WebBrowser browser);
    }
}
