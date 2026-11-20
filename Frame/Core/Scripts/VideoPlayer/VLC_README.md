# VLC VideoPlayer（原 Samples，现位于 Core）

`CustomVLCPlayer` 实现 `IVideoPlayer`，依赖宿主工程的 **VLCUnity / LibVLCSharp**（通常在 `Assets/Plugins/VLCUnity`）。

`Frame.Core.VLC` 受 `FRAME_HAS_LIBVLC` 约束：工程里没有 `LibVLCSharp.dll` 时该程序集不编译，避免 CS0246。安装 VLCUnity 后，编辑器会自动加上该宏（也可执行 `Tools/Frame/Sync LibVLC Define`）。

未安装插件时请改用 `BuildInVideoPlayer`（`VideoPlayerManager.m_useBuildInVideoPlayer`）。
