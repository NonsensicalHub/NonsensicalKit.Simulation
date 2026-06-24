using NonsensicalKit.Simulation.WarehouseSimulation.Simulation;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Runtime
{
    /// <summary>在系统文件管理器中打开仿真 HTML 报告导出目录（由 Runtime 调用，Playback 层不依赖 Simulation 导出 API）。</summary>
    public static class WarehouseSimReportDirectory
    {
        public static void Open(string directory = null) => SimRunExporter.OpenExportDirectory(directory);
    }
}
