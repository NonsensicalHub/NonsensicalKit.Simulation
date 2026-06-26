using System;
using System.Collections.Generic;
using System.Text;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <summary>从子任务时间轴构建单任务校验报告（HTML 详情页数据源）。</summary>
    public static class SimCargoJobValidationReportBuilder
    {
        private readonly struct FormatContext
        {
            public readonly WarehouseConveyorMap Map;
            public readonly ConveyorMapTopology Topology;
            public readonly ConveyorSegmentScheduleEntry[] SegmentSchedule;

            public FormatContext(
                WarehouseConveyorMap map,
                ConveyorMapTopology topology,
                ConveyorSegmentScheduleEntry[] segmentSchedule)
            {
                Map = map;
                Topology = topology;
                SegmentSchedule = segmentSchedule ?? Array.Empty<ConveyorSegmentScheduleEntry>();
            }
        }

        public static bool TryBuild(
            IReadOnlyList<SimSubTask> subTasks,
            int jobId,
            double simTime,
            SimCargoJobValidationReport report,
            WarehouseConveyorMap map = null,
            ConveyorMapTopology topology = null)
        {
            if (report == null || subTasks == null || jobId < 0)
            {
                return false;
            }

            report.Clear();
            report.JobId = jobId;
            report.SimTime = simTime;

            var jobTasks = new List<SimSubTask>();
            for (var i = 0; i < subTasks.Count; i++)
            {
                if (subTasks[i].JobId == jobId)
                {
                    jobTasks.Add(subTasks[i]);
                }
            }

            if (jobTasks.Count == 0)
            {
                return false;
            }

            jobTasks.Sort((a, b) =>
            {
                var start = a.StartSimTime.CompareTo(b.StartSimTime);
                return start != 0 ? start : a.SubTaskId.CompareTo(b.SubTaskId);
            });

            report.HasStarted = SimSubTaskQuery.HasStarted(subTasks, jobId, simTime);
            report.IsOutbound = SimSubTaskQuery.IsOutboundJob(jobTasks);
            ExtractJobContext(jobTasks, report, map, topology);
            var formatContext = new FormatContext(map, topology, report.SegmentSchedule);

            var sequence = 0;
            for (var i = 0; i < jobTasks.Count; i++)
            {
                var task = jobTasks[i];
                sequence++;
                var entry = BuildEntry(formatContext, task, sequence, simTime, report.IsOutbound);
                report.SubTasks.Add(entry);

                if (entry.IsActiveAtCurrentTime)
                {
                    report.ActiveSubTaskSequence = entry.Sequence;
                    report.ActiveSubTaskLabel = entry.KindLabel;
                    report.ActiveSubTaskProgress = entry.Progress;
                }

                if (task.Kind == SimSubTaskKind.Completed && simTime >= task.EndSimTime - 1e-9)
                {
                    report.IsCompleted = true;
                }
            }

            if (report.JobStartSimTime <= 1e-9 && jobTasks.Count > 0)
            {
                report.JobStartSimTime = jobTasks[0].StartSimTime;
            }

            if (report.JobEndSimTime <= 1e-9)
            {
                report.JobEndSimTime = jobTasks[^1].EndSimTime;
            }

            report.JobDurationSeconds = Math.Max(0, report.JobEndSimTime - report.JobStartSimTime);
            report.FullReportText = FormatFullReport(formatContext, report);
            return true;
        }

        private static void ExtractJobContext(
            List<SimSubTask> jobTasks,
            SimCargoJobValidationReport report,
            WarehouseConveyorMap map,
            ConveyorMapTopology topology)
        {
            var pathFormatContext = new FormatContext(map, topology, null);
            report.JobStartSimTime = double.MaxValue;
            report.JobEndSimTime = 0;

            for (var i = 0; i < jobTasks.Count; i++)
            {
                var task = jobTasks[i];
                report.TargetSlot = task.Slot;
                if (task.InfeedPortIndex >= 0)
                {
                    report.InfeedPortIndex = task.InfeedPortIndex;
                }

                if (task.OutfeedPortIndex >= 0)
                {
                    report.OutfeedPortIndex = task.OutfeedPortIndex;
                }

                if (task.PickupPointIndex >= 0)
                {
                    report.PickupPointIndex = task.PickupPointIndex;
                }

                if (task.StackerId >= 0)
                {
                    report.StackerId = task.StackerId;
                }

                if (task.StartSimTime < report.JobStartSimTime)
                {
                    report.JobStartSimTime = task.StartSimTime;
                }

                if (task.EndSimTime > report.JobEndSimTime)
                {
                    report.JobEndSimTime = task.EndSimTime;
                }

                if (task.PathNodeIndices != null && task.PathNodeIndices.Length > 0)
                {
                    report.PathNodeCount = task.PathNodeIndices.Length;
                    report.PathNodeSummary = FormatPathSummary(pathFormatContext, task.PathNodeIndices);
                }

                if (task.SegmentSchedule != null && task.SegmentSchedule.Length > 0)
                {
                    report.SegmentCount = task.SegmentSchedule.Length;
                    report.SegmentSchedule = task.SegmentSchedule;
                }
            }

            if (report.JobStartSimTime == double.MaxValue)
            {
                report.JobStartSimTime = 0;
            }
        }

        private static SimCargoSubTaskDisplayEntry BuildEntry(
            in FormatContext ctx,
            in SimSubTask task,
            int sequence,
            double simTime,
            bool isOutbound)
        {
            var duration = Math.Max(0, task.EndSimTime - task.StartSimTime);
            var state = SimCargoSubTaskPlaybackState.Pending;
            var progress = 0f;
            var isActive = false;

            if (simTime >= task.EndSimTime - 1e-9)
            {
                state = SimCargoSubTaskPlaybackState.Completed;
                progress = 1f;
            }
            else if (simTime >= task.StartSimTime - 1e-9)
            {
                state = SimCargoSubTaskPlaybackState.Active;
                progress = task.NormalizedProgress(simTime);
                isActive = true;
            }

            return new SimCargoSubTaskDisplayEntry
            {
                Sequence = sequence,
                SubTaskId = task.SubTaskId,
                Kind = task.Kind,
                KindLabel = SimSubTaskQuery.GetKindLabel(task),
                StartSimTime = task.StartSimTime,
                EndSimTime = task.EndSimTime,
                DurationSeconds = duration,
                Detail = FormatTaskDetail(ctx, task, isOutbound),
                PlaybackState = state,
                Progress = progress,
                IsActiveAtCurrentTime = isActive,
                FromNodeIndex = task.FromNodeIndex,
                ToNodeIndex = task.ToNodeIndex,
                SegmentSlotIndex = task.SegmentSlotIndex,
                StackerId = task.StackerId,
                Slot = task.Slot,
                InfeedPortIndex = task.InfeedPortIndex,
                PickupPointIndex = task.PickupPointIndex,
            };
        }

        private static string FormatTaskDetail(in FormatContext ctx, in SimSubTask task, bool isOutbound)
        {
            switch (task.Kind)
            {
                case SimSubTaskKind.InfeedPlace:
                    return $"在入库口 {FormatInfeedPort(ctx, task.InfeedPortIndex)} 放货并完成扫描建单";
                case SimSubTaskKind.OutfeedService:
                    return $"在出库口 {FormatOutfeedPort(ctx, task.OutfeedPortIndex)} 发运服务";
                case SimSubTaskKind.ProcessStationService:
                    return $"在加工站点 {FormatNode(ctx, task.ToNodeIndex)} 停留加工";
                case SimSubTaskKind.VerticalTransferMove:
                    return $"在垂直提升机 {FormatNode(ctx, task.ToNodeIndex)} 跨层移动";
                case SimSubTaskKind.InfeedMove:
                    return FormatInfeedMoveDetail(ctx, task);
                case SimSubTaskKind.OutboundMove:
                    return FormatOutboundMoveDetail(ctx, task);
                case SimSubTaskKind.SegmentQueue:
                    if (SimSubTaskKinds.IsOutboundPickupQueue(task))
                    {
                        return $"在{FormatPickupPoint(ctx, task.PickupPointIndex)}等待出库输送";
                    }

                    return
                        $"货物件{FormatNode(ctx, task.FromNodeIndex)} 等待进入 {FormatSegment(ctx, task.FromNodeIndex, task.ToNodeIndex)}";
                case SimSubTaskKind.SegmentTransit:
                    return
                        $"货物件{FormatNode(ctx, task.FromNodeIndex)} 移动列{FormatNode(ctx, task.ToNodeIndex)}（整段）";
                case SimSubTaskKind.SegmentHopMove:
                    return
                        $"从{FormatSegmentHopMoveSource(ctx, task)}移动到{FormatSegmentHopMoveTarget(ctx, task)}";
                case SimSubTaskKind.SegmentStopDwell:
                    return
                        $"停留在{FormatSegmentStopLabel(ctx, task.FromNodeIndex, task.ToNodeIndex, task.SegmentSlotIndex)}等待下游放行";
                case SimSubTaskKind.JunctionEnter:
                    return FormatJunctionEnterDetail(ctx, task);
                case SimSubTaskKind.JunctionWait:
                    return FormatJunctionWaitDetail(ctx, task);
                case SimSubTaskKind.JunctionExit:
                    return FormatJunctionExitDetail(ctx, task);
                case SimSubTaskKind.StackerWait:
                    return isOutbound
                        ? $"堆垛机在货位 {task.Slot} 等待取货资源"
                        : $"货物在 {FormatPickupPoint(ctx, task.PickupPointIndex)} 等待堆垛机接驳";
                case SimSubTaskKind.StackerApproach:
                    return isOutbound
                        ? $"堆垛机从当前位置驶向货位 {task.Slot}"
                        : $"堆垛机驶向 {FormatPickupPoint(ctx, task.PickupPointIndex)}";
                case SimSubTaskKind.StackerPick:
                    return isOutbound
                        ? $"堆垛机从货位 {task.Slot} 取货"
                        : $"堆垛机从 {FormatPickupPoint(ctx, task.PickupPointIndex)} 取货";
                case SimSubTaskKind.StackerMove:
                    return isOutbound
                        ? $"堆垛机将货物从货位 {task.Slot} 移至 {FormatPickupPoint(ctx, task.PickupPointIndex)}"
                        : $"堆垛机将货物从 {FormatPickupPoint(ctx, task.PickupPointIndex)} 移动到货位 {task.Slot}";
                case SimSubTaskKind.StackerPlace:
                    return isOutbound
                        ? $"堆垛机在 {FormatPickupPoint(ctx, task.PickupPointIndex)} 放货等待输送"
                        : $"堆垛机将货物放入货位 {task.Slot}";
                case SimSubTaskKind.Completed:
                    return isOutbound ? "出库完成" : "入库完成";
                default:
                    return task.Slot.ToString();
            }
        }

        private static string FormatPathSummary(in FormatContext ctx, int[] path)
        {
            if (path == null || path.Length == 0)
            {
                return string.Empty;
            }

            if (ctx.Map != null)
            {
                return SimEntityNaming.FormatPathSummary(ctx.Map, path);
            }

            if (path.Length <= 12)
            {
                var sb = new StringBuilder(path.Length * 4);
                for (var i = 0; i < path.Length; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(" →");
                    }

                    sb.Append('n');
                    sb.Append(path[i]);
                }

                return sb.ToString();
            }

            return
                $"n{path[0]} →n{path[1]} →—→n{path[^2]} →n{path[^1]} ({path.Length} 节点)";
        }

        private static string FormatFullReport(in FormatContext ctx, SimCargoJobValidationReport report)
        {
            var sb = new StringBuilder(512);
            sb.AppendLine($"=== Job {report.JobId} {(report.IsOutbound ? "出库" : "入库")}任务校验 ===");
            sb.AppendLine($"仿真时刻: {report.SimTime:F2}s");
            if (report.IsOutbound)
            {
                sb.AppendLine(
                    $"源货位: {report.TargetSlot} | 出库口{FormatOutfeedPort(ctx, report.OutfeedPortIndex)} | " +
                    $"堆垛机交互点{FormatPickupPoint(ctx, report.PickupPointIndex)} | 堆垛机{FormatStacker(report.StackerId)}");
            }
            else
            {
                sb.AppendLine(
                    $"目标货位: {report.TargetSlot} | 入库口{FormatInfeedPort(ctx, report.InfeedPortIndex)} | " +
                    $"堆垛机交互点{FormatPickupPoint(ctx, report.PickupPointIndex)} | 堆垛机{FormatStacker(report.StackerId)}");
            }
            sb.AppendLine(
                $"任务区间: {report.JobStartSimTime:F2}s ~ {report.JobEndSimTime:F2}s " +
                $"({report.JobDurationSeconds:F2}s)");

            if (report.PathNodeCount > 0)
            {
                sb.AppendLine($"输送路径 {report.PathNodeCount} 节点 / {report.SegmentCount} 路段");
                if (!string.IsNullOrEmpty(report.PathNodeSummary))
                {
                    sb.AppendLine($"路径: {report.PathNodeSummary}");
                }

                AppendSegmentScheduleSection(ctx, sb, report.SegmentSchedule);
            }

            sb.AppendLine("--- 子任务时间轴 ---");
            for (var i = 0; i < report.SubTasks.Count; i++)
            {
                var entry = report.SubTasks[i];
                sb.Append('[');
                sb.Append(entry.Sequence);
                sb.Append("] ");
                sb.Append(entry.KindLabel.PadRight(8));
                sb.Append(' ');
                sb.Append(entry.StartSimTime.ToString("F2").PadLeft(8));
                sb.Append(" ~ ");
                sb.Append(entry.EndSimTime.ToString("F2").PadRight(8));
                sb.Append(" (");
                sb.Append(entry.DurationSeconds.ToString("F2"));
                sb.Append("s) [");
                sb.Append(FormatPlaybackState(entry.PlaybackState, entry.Progress));
                sb.Append("]  ");
                sb.Append(entry.Detail);
                sb.Append("  #");
                sb.Append(entry.SubTaskId);
                sb.AppendLine();
            }

            sb.AppendLine("---");
            if (report.IsCompleted)
            {
                sb.AppendLine("当前: 任务已完成");
            }
            else if (report.ActiveSubTaskSequence > 0)
            {
                sb.AppendLine(
                    $"当前: [{report.ActiveSubTaskSequence}] {report.ActiveSubTaskLabel} " +
                    $"{report.ActiveSubTaskProgress * 100f:F0}%");
            }
            else if (!report.HasStarted)
            {
                sb.AppendLine("当前: 任务尚未开始");
            }
            else
            {
                sb.AppendLine("当前: 子任务间隙 / 等待下一阶段");
            }

            return sb.ToString();
        }

        private static void AppendSegmentScheduleSection(
            in FormatContext ctx,
            StringBuilder sb,
            ConveyorSegmentScheduleEntry[] schedule)
        {
            if (schedule == null || schedule.Length == 0)
            {
                return;
            }

            sb.AppendLine("--- 输送路段预约 ---");
            for (var i = 0; i < schedule.Length; i++)
            {
                var seg = schedule[i];
                sb.Append("[S");
                sb.Append(i + 1);
                sb.Append("] ");
                sb.Append(FormatSegment(ctx, seg.FromNodeIndex, seg.ToNodeIndex));
                sb.Append(" 槽位");
                sb.Append(seg.SlotIndex);
                sb.Append(" | 进入 ");
                sb.Append(seg.EntrySimTime.ToString("F2"));
                sb.Append(" 到达 ");
                sb.Append(seg.ExitSimTime.ToString("F2"));
                sb.Append(" 占用止 ");
                sb.Append(seg.OccupancyEndSimTime.ToString("F2"));
                sb.AppendLine();
            }
        }

        private static string FormatPlaybackState(SimCargoSubTaskPlaybackState state, float progress) =>
            state switch
            {
                SimCargoSubTaskPlaybackState.Pending => "待执行",
                SimCargoSubTaskPlaybackState.Active => $"进行中 {progress * 100f:F0}%",
                SimCargoSubTaskPlaybackState.Completed => "已完成",
                _ => state.ToString(),
            };

        private static string FormatStacker(int stackerId) => SimEntityNaming.FormatStacker(stackerId);

        private static string FormatInfeedPort(in FormatContext ctx, int infeedPortIndex)
        {
            if (ctx.Map != null && ctx.Topology != null)
            {
                return SimEntityNaming.FormatInfeedPort(ctx.Map, ctx.Topology, infeedPortIndex);
            }

            return infeedPortIndex >= 0 ? $"IN{infeedPortIndex + 1}" : "—";
        }

        private static string FormatOutfeedPort(in FormatContext ctx, int outfeedPortIndex)
        {
            if (ctx.Map != null && ctx.Topology != null)
            {
                return SimEntityNaming.FormatOutfeedPort(ctx.Map, ctx.Topology, outfeedPortIndex);
            }

            return outfeedPortIndex >= 0 ? $"OUT{outfeedPortIndex + 1}" : "—";
        }

        private static string FormatPickupPoint(in FormatContext ctx, int pickupNodeIndex)
        {
            if (ctx.Map != null)
            {
                return SimEntityNaming.FormatPickupPoint(ctx.Map, pickupNodeIndex);
            }

            return pickupNodeIndex >= 0 ? $"P{pickupNodeIndex + 1}" : "—";
        }

        private static string FormatNode(in FormatContext ctx, int nodeIndex)
        {
            if (ctx.Map != null)
            {
                return SimEntityNaming.FormatNode(ctx.Map, nodeIndex);
            }

            return nodeIndex >= 0 ? $"n{nodeIndex}" : "—";
        }

        private static string FormatSegment(in FormatContext ctx, int fromNodeIndex, int toNodeIndex)
        {
            if (ctx.Map != null)
            {
                return SimEntityNaming.FormatSegment(ctx.Map, fromNodeIndex, toNodeIndex);
            }

            return fromNodeIndex >= 0 && toNodeIndex >= 0
                ? $"n{fromNodeIndex}-n{toNodeIndex}"
                : "—";
        }

        private static string FormatInfeedMoveDetail(in FormatContext ctx, in SimSubTask task)
        {
            var infeed = FormatInfeedPort(ctx, task.InfeedPortIndex);
            var slot = task.SegmentSlotIndex >= 0 ? task.SegmentSlotIndex : 0;
            return
                $"从{infeed}移动到{FormatSegmentStopLabel(ctx, task.FromNodeIndex, task.ToNodeIndex, slot)}";
        }

        private static string FormatOutboundMoveDetail(in FormatContext ctx, in SimSubTask task)
        {
            var pickup = FormatPickupPoint(ctx, task.PickupPointIndex);
            var slot = task.SegmentSlotIndex >= 0 ? task.SegmentSlotIndex : 0;
            return
                $"从{pickup}移动到{FormatSegmentStopLabel(ctx, task.FromNodeIndex, task.ToNodeIndex, slot)}";
        }

        private static string FormatJunctionEnterDetail(in FormatContext ctx, in SimSubTask task)
        {
            var fromStop = FormatSegmentStopLabel(ctx, task.FromNodeIndex, task.ToNodeIndex, 0);
            return $"从{fromStop}驶入{FormatNode(ctx, task.ToNodeIndex)}路口";
        }

        private static string FormatJunctionWaitDetail(in FormatContext ctx, in SimSubTask task) =>
            $"在{FormatNode(ctx, task.ToNodeIndex)}路口等待下一段入口停留点";

        private static string FormatJunctionExitDetail(in FormatContext ctx, in SimSubTask task)
        {
            var junctionNode = FormatNode(ctx, task.ToNodeIndex);
            if (TryFindNextSegmentSchedule(ctx, task.FromNodeIndex, task.ToNodeIndex, out var nextSeg))
            {
                var nextStops = nextSeg.StopArriveSimTimes;
                if (nextStops != null && nextStops.Length > 0)
                {
                    return
                        $"从{junctionNode}路口驶出至{FormatSegmentStopLabel(ctx, nextSeg.FromNodeIndex, nextSeg.ToNodeIndex, nextStops.Length - 1)}";
                }
            }

            return $"从{junctionNode}路口驶出";
        }

        private static bool TryFindNextSegmentSchedule(
            in FormatContext ctx,
            int fromNodeIndex,
            int toNodeIndex,
            out ConveyorSegmentScheduleEntry segment)
        {
            segment = default;
            if (ctx.SegmentSchedule.Length < 2)
            {
                return false;
            }

            for (var i = 0; i < ctx.SegmentSchedule.Length - 1; i++)
            {
                var current = ctx.SegmentSchedule[i];
                if (current.FromNodeIndex == fromNodeIndex && current.ToNodeIndex == toNodeIndex)
                {
                    segment = ctx.SegmentSchedule[i + 1];
                    return true;
                }
            }

            return false;
        }

        private static string FormatSegmentStopLabel(
            in FormatContext ctx,
            int fromNodeIndex,
            int toNodeIndex,
            int slotIndex)
        {
            return $"{FormatSegment(ctx, fromNodeIndex, toNodeIndex)}路段停留点S{slotIndex}";
        }

        private static string FormatSegmentHopMoveSource(in FormatContext ctx, in SimSubTask task)
        {
            if (SimSubTaskKinds.IsPickupArrivalHop(task))
            {
                return FormatSegmentStopLabel(ctx, task.FromNodeIndex, task.ToNodeIndex, 0);
            }

            return FormatSegmentStopLabel(ctx, task.FromNodeIndex, task.ToNodeIndex, task.SegmentSlotIndex + 1);
        }

        private static string FormatSegmentHopMoveTarget(in FormatContext ctx, in SimSubTask task)
        {
            if (SimSubTaskKinds.IsPickupArrivalHop(task))
            {
                var pickupNode = task.PickupPointIndex >= 0 ? task.PickupPointIndex : task.ToNodeIndex;
                return FormatPickupPoint(ctx, pickupNode);
            }

            return FormatSegmentStopLabel(
                ctx,
                task.FromNodeIndex,
                task.ToNodeIndex,
                task.SegmentSlotIndex);
        }
    }
}
