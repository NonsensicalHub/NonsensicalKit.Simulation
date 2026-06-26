using System;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <summary>仿真 HTML 报告与控制台摘要共用的实体/时长格式化。</summary>
    internal static class SimReportFormatting
    {
        private static readonly TimeZoneInfo ChinaStandardTimeZone = ResolveChinaStandardTimeZone();

        /// <summary>报告元数据用的东八区（UTC+8）墙钟时间。</summary>
        public static string FormatExportDateTime(DateTime? utcNow = null)
        {
            var utc = utcNow ?? DateTime.UtcNow;
            return TimeZoneInfo.ConvertTimeFromUtc(utc, ChinaStandardTimeZone)
                .ToString("yyyy-MM-dd HH:mm:ss");
        }

        /// <summary>报告文件名时间戳（东八区）。</summary>
        public static string FormatExportFileStamp(DateTime? utcNow = null)
        {
            var utc = utcNow ?? DateTime.UtcNow;
            return TimeZoneInfo.ConvertTimeFromUtc(utc, ChinaStandardTimeZone)
                .ToString("yyyyMMdd_HHmmss");
        }

        private static TimeZoneInfo ResolveChinaStandardTimeZone()
        {
            foreach (var id in new[] { "China Standard Time", "Asia/Shanghai" })
            {
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById(id);
                }
                catch (TimeZoneNotFoundException)
                {
                }
                catch (InvalidTimeZoneException)
                {
                }
            }

            return TimeZoneInfo.CreateCustomTimeZone(
                "UTC+8",
                TimeSpan.FromHours(8),
                "UTC+8",
                "UTC+8");
        }

        public static string FormatDuration(double seconds)
        {
            var ts = TimeSpan.FromSeconds(seconds);
            return ts.TotalHours >= 1
                ? $"{(int)ts.TotalHours}h {ts.Minutes}m {ts.Seconds}s"
                : ts.TotalMinutes >= 1
                    ? $"{ts.Minutes}m {ts.Seconds}s"
                    : $"{seconds:F0}s";
        }

        public static string FormatStacker(int stackerId) => SimEntityNaming.FormatStacker(stackerId);

        public static string FormatInfeedPort(
            WarehouseConveyorMap map,
            ConveyorMapTopology topology,
            int infeedPortIndex) =>
            map != null && topology != null
                ? SimEntityNaming.FormatInfeedPort(map, topology, infeedPortIndex)
                : infeedPortIndex >= 0 ? $"IN{infeedPortIndex + 1}" : "—";

        public static string FormatPickupPoint(WarehouseConveyorMap map, int pickupNodeIndex) =>
            map != null
                ? SimEntityNaming.FormatPickupPoint(map, pickupNodeIndex)
                : pickupNodeIndex >= 0 ? $"P{pickupNodeIndex + 1}" : "—";

        public static string FormatOutfeedPort(
            WarehouseConveyorMap map,
            ConveyorMapTopology topology,
            int outfeedPortIndex) =>
            map != null && topology != null
                ? SimEntityNaming.FormatOutfeedPort(map, topology, outfeedPortIndex)
                : outfeedPortIndex >= 0 ? $"OUT{outfeedPortIndex + 1}" : "—";
    }
}
