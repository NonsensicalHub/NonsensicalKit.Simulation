# Vuplex WebBrowser（原 Samples，现位于 Core）

`StandaloneWebBrowser.cs` 为 `WebBrowser` 的 Standalone partial 实现，依赖宿主 **Vuplex 3D WebView**（`Assets/Plugins/Vuplex`）。

WebGL 使用 `WebGLWebBrowser.cs`（`UNITY_WEBGL && !UNITY_EDITOR`），与 Standalone 互斥。
