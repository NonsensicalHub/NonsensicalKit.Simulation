using System;
using System.Collections.Generic;
using System.Text;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <summary>生成节点占用冲突的详细文本报告。</summary>
    public static class SimOccupancyConflictReportBuilder
    {
        public static string BuildFullReport(
            IReadOnlyList<SimOccupancyConflictRecord> conflicts,
            IReadOnlyList<SimOccupancyConflictChecker.IntervalSnapshot> intervals = null,
            double simEndTime = 0)
        {
            if (conflicts == null || conflicts.Count == 0)
            {
                return "=== 节点占用自检 ===\n通过：未发现跨任务独占资源时间重叠。";
            }

            var sb = new StringBuilder(Math.Max(4096, conflicts.Count * 160));
            sb.AppendLine("=== 节点占用自检 — 冲突报告 ===");
            sb.AppendLine($"冲突总数: {conflicts.Count}");
            if (simEndTime > 1e-9)
            {
                sb.AppendLine($"仿真结束时刻: {simEndTime:F2}s");
            }

            AppendCategorySummary(sb, conflicts);
            AppendTopResources(sb, conflicts, 15);
            sb.AppendLine();
            sb.AppendLine("--- 冲突明细（按资源键、重叠起点排序） ---");

            var sorted = new List<SimOccupancyConflictRecord>(conflicts);
            sorted.Sort((a, b) =>
            {
                var key = string.CompareOrdinal(a.ResourceKey, b.ResourceKey);
                if (key != 0)
                {
                    return key;
                }

                var overlap = a.OverlapStart.CompareTo(b.OverlapStart);
                return overlap != 0 ? overlap : a.JobA.CompareTo(b.JobA);
            });

            for (var i = 0; i < sorted.Count; i++)
            {
                AppendConflictLine(sb, i + 1, sorted[i]);
            }

            if (intervals != null && intervals.Count > 0)
            {
                sb.AppendLine();
                AppendResourceTimelineAppendix(sb, conflicts, intervals, 8);
            }

            return sb.ToString().TrimEnd();
        }

        public static string FormatCategoryLabel(SimOccupancyResourceCategory category) =>
            category switch
            {
                SimOccupancyResourceCategory.Junction => "路口",
                SimOccupancyResourceCategory.SegmentSlot => "路段停留点",
                SimOccupancyResourceCategory.Infeed => "入库口",
                SimOccupancyResourceCategory.Outfeed => "出库口",
                SimOccupancyResourceCategory.Pickup => "堆垛机交互点",
                _ => "未知",
            };

        private static void AppendCategorySummary(StringBuilder sb, IReadOnlyList<SimOccupancyConflictRecord> conflicts)
        {
            var counts = new Dictionary<SimOccupancyResourceCategory, int>();
            for (var i = 0; i < conflicts.Count; i++)
            {
                var cat = conflicts[i].Category;
                counts.TryGetValue(cat, out var n);
                counts[cat] = n + 1;
            }

            sb.AppendLine("按类型统计:");
            foreach (SimOccupancyResourceCategory cat in Enum.GetValues(typeof(SimOccupancyResourceCategory)))
            {
                if (cat == SimOccupancyResourceCategory.Unknown || !counts.TryGetValue(cat, out var n) || n == 0)
                {
                    continue;
                }

                sb.Append("  ");
                sb.Append(FormatCategoryLabel(cat));
                sb.Append(": ");
                sb.Append(n);
                sb.AppendLine(" 处");
            }
        }

        private static void AppendTopResources(
            StringBuilder sb,
            IReadOnlyList<SimOccupancyConflictRecord> conflicts,
            int topN)
        {
            var byResource = new Dictionary<string, (string label, SimOccupancyResourceCategory cat, int count)>(
                StringComparer.Ordinal);
            for (var i = 0; i < conflicts.Count; i++)
            {
                var c = conflicts[i];
                if (byResource.TryGetValue(c.ResourceKey, out var entry))
                {
                    byResource[c.ResourceKey] = (entry.label, entry.cat, entry.count + 1);
                }
                else
                {
                    byResource[c.ResourceKey] = (c.ResourceLabel, c.Category, 1);
                }
            }

            var ranking = new List<(string key, string label, SimOccupancyResourceCategory cat, int count)>(
                byResource.Count);
            foreach (var pair in byResource)
            {
                ranking.Add((pair.Key, pair.Value.label, pair.Value.cat, pair.Value.count));
            }

            ranking.Sort((a, b) =>
            {
                var count = b.count.CompareTo(a.count);
                return count != 0 ? count : string.CompareOrdinal(a.key, b.key);
            });

            sb.AppendLine();
            sb.AppendLine($"冲突最多的资源 (Top {Math.Min(topN, ranking.Count)}):");
            for (var i = 0; i < ranking.Count && i < topN; i++)
            {
                var row = ranking[i];
                sb.Append("  ");
                sb.Append(i + 1);
                sb.Append(". [");
                sb.Append(FormatCategoryLabel(row.cat));
                sb.Append("] ");
                sb.Append(row.label);
                sb.Append("  key=");
                sb.Append(row.key);
                sb.Append("  ×");
                sb.AppendLine(row.count.ToString());
            }
        }

        private static void AppendConflictLine(StringBuilder sb, int index, in SimOccupancyConflictRecord c)
        {
            sb.Append('[');
            sb.Append(index);
            sb.Append("] [");
            sb.Append(FormatCategoryLabel(c.Category));
            sb.Append("] ");
            sb.Append(c.ResourceLabel);
            sb.Append("  key=");
            sb.AppendLine(c.ResourceKey);
            sb.Append("    Job ");
            sb.Append(c.JobA);
            sb.Append(' ');
            sb.Append(SimSubTaskQuery.GetKindLabel(c.KindA));
            sb.Append(' ');
            sb.Append(c.JobAStart.ToString("F3"));
            sb.Append("~");
            sb.Append(c.JobAEnd.ToString("F3"));
            sb.Append("s");
            sb.AppendLine();
            sb.Append("    Job ");
            sb.Append(c.JobB);
            sb.Append(' ');
            sb.Append(SimSubTaskQuery.GetKindLabel(c.KindB));
            sb.Append(' ');
            sb.Append(c.JobBStart.ToString("F3"));
            sb.Append("~");
            sb.Append(c.JobBEnd.ToString("F3"));
            sb.Append("s");
            sb.AppendLine();
            sb.Append("    重叠 ");
            sb.Append(c.OverlapStart.ToString("F3"));
            sb.Append("~");
            sb.Append(c.OverlapEnd.ToString("F3"));
            sb.Append("s (");
            sb.Append(c.OverlapSeconds.ToString("F3"));
            sb.AppendLine("s)");
        }

        private static void AppendResourceTimelineAppendix(
            StringBuilder sb,
            IReadOnlyList<SimOccupancyConflictRecord> conflicts,
            IReadOnlyList<SimOccupancyConflictChecker.IntervalSnapshot> intervals,
            int maxResources)
        {
            var hotKeys = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < conflicts.Count; i++)
            {
                var key = conflicts[i].ResourceKey;
                counts.TryGetValue(key, out var n);
                counts[key] = n + 1;
            }

            var ranking = new List<(string key, int count)>(counts.Count);
            foreach (var pair in counts)
            {
                ranking.Add((pair.Key, pair.Value));
            }

            ranking.Sort((a, b) =>
            {
                var count = b.count.CompareTo(a.count);
                return count != 0 ? count : string.CompareOrdinal(a.key, b.key);
            });

            for (var i = 0; i < ranking.Count && hotKeys.Count < maxResources; i++)
            {
                hotKeys.Add(ranking[i].key);
            }

            sb.AppendLine("--- 附录：高冲突资源上的全部占用区间 ---");
            for (var r = 0; r < hotKeys.Count; r++)
            {
                var key = hotKeys[r];
                if (!seen.Add(key))
                {
                    continue;
                }

                sb.AppendLine();
                sb.Append("# ");
                sb.Append(key);
                sb.AppendLine(" — 全部任务占用区间 [start, end):");
                for (var i = 0; i < intervals.Count; i++)
                {
                    var iv = intervals[i];
                    if (!string.Equals(iv.ResourceKey, key, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    sb.Append("    Job ");
                    sb.Append(iv.JobId);
                    sb.Append(' ');
                    sb.Append(SimSubTaskQuery.GetKindLabel(iv.Kind));
                    sb.Append(' ');
                    sb.Append(iv.Start.ToString("F3"));
                    sb.Append("~");
                    sb.Append(iv.End.ToString("F3"));
                    sb.AppendLine("s");
                }
            }
        }
    }
}
