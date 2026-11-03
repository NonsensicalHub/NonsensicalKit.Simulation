# VLC VideoPlayer（原 Samples，现位于 Core）

`CustomVLCPlayer` 实现 `IVideoPlayer`，依赖宿主工程的 **VLCUnity / LibVLCSharp**（通常在 `Assets/Plugins/VLCUnity`）。

未安装插件时请改用 `BuildInVideoPlayer`（`VideoPlayerManager.m_useBuildInVideoPlayer`）。
