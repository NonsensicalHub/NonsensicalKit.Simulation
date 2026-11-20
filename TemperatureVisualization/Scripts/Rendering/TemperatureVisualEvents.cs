namespace TemperatureVisualization
{
    /// <summary>
    /// UI → 可视化控制器的 IOCC 命令名。
    /// 通过 PublishWithID(event, libraryId, …) / Subscribe(event, libraryId, …) 配对。
    /// </summary>
    public static class TemperatureVisualEvents
    {
        public const string SetVisible = "TemperatureVisual.SetVisible";
        public const string SetMode = "TemperatureVisual.SetMode";
        public const string SetSlicePositionXY = "TemperatureVisual.SetSlicePositionXY";
        public const string SetSlicePositionXZ = "TemperatureVisual.SetSlicePositionXZ";
        public const string SetSlicePositionYZ = "TemperatureVisual.SetSlicePositionYZ";
        public const string SetShowSliceXY = "TemperatureVisual.SetShowSliceXY";
        public const string SetShowSliceXZ = "TemperatureVisual.SetShowSliceXZ";
        public const string SetShowSliceYZ = "TemperatureVisual.SetShowSliceYZ";
        public const string SetIsoTemperature = "TemperatureVisual.SetIsoTemperature";
    }
}
