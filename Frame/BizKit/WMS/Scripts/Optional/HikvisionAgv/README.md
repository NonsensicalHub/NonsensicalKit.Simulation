# HikvisionAgv

海康 RCS-Lite 孪生 Runtime / Editor（从冷链 `Assets/Scripts/HikvisionAgv` 迁入，保留 GUID）。

- `Runtime/` → 程序集 `HikvisionAgv.Runtime`
- `Editor/` → 程序集 `HikvisionAgv.Editor`
- HTTP 胶水与 SiteConfig 在同级 `../Hikvision/`（`Frame.BizKit.WMS.Hikvision`，含 `Network/`）
- Sample 磁带后端仍在 `Samples~/FrameSample/Adapters/HikvisionAgv`

宿主需已引用本包内 ScriptAnimation Runtime；缺依赖时仅本 Optional 受影响。
