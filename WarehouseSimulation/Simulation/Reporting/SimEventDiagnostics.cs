using System;
using System.Collections.Generic;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <summary>仿真结束时的离散事件诊断快照（用于日志与报告）。</summary>
    [Serializable]
    public sealed class SimEventDiagnostics
    {
        public int DispatchedEventCount;
        public int QueuePendingCount;
        public int MaxSimEvents;
        public double SimTimeSeconds;
        public int CompletedJobCount;
        /// <summary>入库失败箱数（含停在入库口未建任务的等候箱，与 <see cref="CompletedJobCount"/> 之和不等于目标数时属正常）。</summary>
        public int FailedJobCount;
        public int TargetJobCount;
        public int OccupiedSlotCount;
        public int TotalSlotCount;
        public int AllocatableStorageSlotCount;
        public List<SimNamedCount> FailureReasonCounts = new();

        public List<SimEventTypeStat> EventTypeStats = new();
        public List<SimJobStateStat> JobStateStats = new();
        public List<SimNamedCount> InfeedReservations = new();
        public List<SimNamedCount> OutfeedReservations = new();
        public List<SimNamedCount> PickupReservations = new();
    }

    [Serializable]
    public struct SimEventTypeStat
    {
        public SimEventType Type;
        public int Count;
        public double Percent;
    }

    [Serializable]
    public struct SimJobStateStat
    {
        public WarehouseJobState State;
        public int Count;
    }

    [Serializable]
    public struct SimNamedCount
    {
        public string Name;
        public int Count;
    }

    /// <summary>从仿真器采集事件诊断并格式化为纯文本。</summary>
    public static class SimEventDiagnosticsCollector
    {
        public static string FormatPlainText(SimEventDiagnostics diag)
        {
            if (diag == null)
            {
                return string.Empty;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(
                $"已处理 {diag.DispatchedEventCount} 个事件，队列剩余 {diag.QueuePendingCount}，仿真时刻 {diag.SimTimeSeconds:F1}s");
            sb.AppendLine(
                $"任务进度：完成 {diag.CompletedJobCount}，失败 {diag.FailedJobCount}，目标 {diag.TargetJobCount}");

            if (diag.EventTypeStats != null && diag.EventTypeStats.Count > 0)
            {
                sb.AppendLine("事件类型统计（按数量降序）：");
                for (var i = 0; i < diag.EventTypeStats.Count; i++)
                {
                    var entry = diag.EventTypeStats[i];
                    sb.AppendLine($"  {entry.Type}: {entry.Count} ({entry.Percent:F1}%)");
                }
            }

            if (diag.JobStateStats != null && diag.JobStateStats.Count > 0)
            {
                sb.AppendLine("任务状态分布：");
                for (var i = 0; i < diag.JobStateStats.Count; i++)
                {
                    var entry = diag.JobStateStats[i];
                    sb.AppendLine($"  {entry.State}: {entry.Count}");
                }
            }

            if (diag.InfeedReservations != null && diag.InfeedReservations.Count > 0)
            {
                sb.Append("入库口预定：");
                for (var i = 0; i < diag.InfeedReservations.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(", ");
                    }

                    sb.Append($"{diag.InfeedReservations[i].Name}={diag.InfeedReservations[i].Count}");
                }

                sb.AppendLine();
            }

            if (diag.OutfeedReservations != null && diag.OutfeedReservations.Count > 0)
            {
                sb.Append("出库口预定：");
                for (var i = 0; i < diag.OutfeedReservations.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(", ");
                    }

                    sb.Append($"{diag.OutfeedReservations[i].Name}={diag.OutfeedReservations[i].Count}");
                }

                sb.AppendLine();
            }

            if (diag.PickupReservations != null && diag.PickupReservations.Count > 0)
            {
                sb.Append("堆垛机交互点预定：");
                for (var i = 0; i < diag.PickupReservations.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(", ");
                    }

                    sb.Append($"{diag.PickupReservations[i].Name}={diag.PickupReservations[i].Count}");
                }

                sb.AppendLine();
            }
            else if (diag.PickupReservations != null)
            {
                sb.AppendLine("堆垛机交互点预定：（均为 0）");
            }

            if (diag.TotalSlotCount > 0)
            {
                sb.AppendLine($"货位占用：{diag.OccupiedSlotCount}/{diag.TotalSlotCount}");
            }

            if (diag.AllocatableStorageSlotCount > 0)
            {
                sb.AppendLine($"堆垛机可达可分配货位：{diag.AllocatableStorageSlotCount}");
            }

            if (diag.FailureReasonCounts != null && diag.FailureReasonCounts.Count > 0)
            {
                sb.Append("失败原因统计：");
                for (var i = 0; i < diag.FailureReasonCounts.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(", ");
                    }

                    sb.Append($"{diag.FailureReasonCounts[i].Name}={diag.FailureReasonCounts[i].Count}");
                }

                sb.AppendLine();
            }

            return sb.ToString().TrimEnd();
        }
    }
}
