using System;
using System.Collections.Generic;
using System.Text;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Model
{
    /// <summary>仿真墙钟性能剖析快照（按阶段汇总耗时与调用次数）。</summary>
    [Serializable]
    public sealed class SimWallClockProfileSnapshot
    {
        public bool Enabled;
        public double TotalWallMilliseconds;
        public List<SimWallClockProfileEntry> Entries = new();

        public static string FormatSummary(SimWallClockProfileSnapshot snapshot)
        {
            if (snapshot == null || !snapshot.Enabled || snapshot.Entries == null || snapshot.Entries.Count == 0)
            {
                return string.Empty;
            }

            var sb = new StringBuilder(snapshot.Entries.Count * 64 + 128);
            sb.AppendLine($"  仿真引擎墙钟 {snapshot.TotalWallMilliseconds:F1} ms");
            sb.AppendLine("  阶段热点（按耗时降序）：");
            for (var i = 0; i < snapshot.Entries.Count; i++)
            {
                var entry = snapshot.Entries[i];
                sb.Append("    ");
                sb.Append(i + 1);
                sb.Append(". ");
                sb.Append(entry.PhaseLabel);
                sb.Append(" — ");
                sb.Append(entry.TotalMilliseconds.ToString("F1"));
                sb.Append(" ms (");
                sb.Append(entry.PercentOfTotal.ToString("F1"));
                sb.Append("%, ");
                sb.Append(entry.CallCount);
                sb.AppendLine(" 次)");
            }

            return sb.ToString().TrimEnd();
        }
    }

    [Serializable]
    public struct SimWallClockProfileEntry
    {
        public string PhaseKey;
        public string PhaseLabel;
        public double TotalMilliseconds;
        public int CallCount;
        public double PercentOfTotal;
    }
}
