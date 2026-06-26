using System;
using System.Collections.Generic;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <summary>从子任务时间轴汇总堆垛机、入库口、出库口等设备利用率。</summary>
    public static class SimResourceUtilizationBuilder
    {
        private const double TimeEpsilon = 1e-6;

        public static List<SimResourceUtilizationStat> Build(
            IReadOnlyList<SimSubTask> subTasks,
            double totalSimSeconds,
            WarehouseConveyorMap map = null,
            ConveyorMapTopology topology = null)
        {
            var result = new List<SimResourceUtilizationStat>();
            if (subTasks == null || subTasks.Count == 0 || totalSimSeconds <= TimeEpsilon)
            {
                return result;
            }

            var stackerIntervals = new Dictionary<int, List<(double Start, double End)>>();
            var infeedIntervals = new Dictionary<int, List<(double Start, double End)>>();
            var outfeedIntervals = new Dictionary<int, List<(double Start, double End)>>();

            for (var i = 0; i < subTasks.Count; i++)
            {
                var task = subTasks[i];
                if (task.EndSimTime - task.StartSimTime <= TimeEpsilon)
                {
                    continue;
                }

                switch (task.Kind)
                {
                    case SimSubTaskKind.StackerApproach:
                    case SimSubTaskKind.StackerPick:
                    case SimSubTaskKind.StackerMove:
                    case SimSubTaskKind.StackerPlace:
                        if (task.StackerId >= 0)
                        {
                            AddInterval(stackerIntervals, task.StackerId, task.StartSimTime, task.EndSimTime);
                        }

                        break;
                    case SimSubTaskKind.InfeedPlace:
                        if (task.InfeedPortIndex >= 0)
                        {
                            AddInterval(infeedIntervals, task.InfeedPortIndex, task.StartSimTime, task.EndSimTime);
                        }

                        break;
                    case SimSubTaskKind.OutfeedService:
                        if (task.OutfeedPortIndex >= 0)
                        {
                            AddInterval(outfeedIntervals, task.OutfeedPortIndex, task.StartSimTime, task.EndSimTime);
                        }

                        break;
                }
            }

            AppendStats(result, SimResourceUtilizationKind.Stacker, stackerIntervals, totalSimSeconds, map, topology);
            AppendStats(result, SimResourceUtilizationKind.InfeedPort, infeedIntervals, totalSimSeconds, map, topology);
            AppendStats(result, SimResourceUtilizationKind.OutfeedPort, outfeedIntervals, totalSimSeconds, map, topology);
            return result;
        }

        public static bool TryGetAverageUtilizationPercent(
            IReadOnlyList<SimResourceUtilizationStat> stats,
            SimResourceUtilizationKind kind,
            out double averagePercent)
        {
            averagePercent = 0;
            if (stats == null || stats.Count == 0)
            {
                return false;
            }

            var count = 0;
            var sum = 0d;
            for (var i = 0; i < stats.Count; i++)
            {
                if (stats[i].Kind != kind)
                {
                    continue;
                }

                sum += stats[i].UtilizationPercent;
                count++;
            }

            if (count == 0)
            {
                return false;
            }

            averagePercent = sum / count;
            return true;
        }

        public static string FormatKindLabel(SimResourceUtilizationKind kind) =>
            kind switch
            {
                SimResourceUtilizationKind.Stacker => "堆垛机",
                SimResourceUtilizationKind.InfeedPort => "入库口",
                SimResourceUtilizationKind.OutfeedPort => "出库口",
                _ => kind.ToString(),
            };

        private static void AppendStats(
            List<SimResourceUtilizationStat> result,
            SimResourceUtilizationKind kind,
            Dictionary<int, List<(double Start, double End)>> intervalsByIndex,
            double totalSimSeconds,
            WarehouseConveyorMap map,
            ConveyorMapTopology topology)
        {
            if (intervalsByIndex.Count == 0)
            {
                return;
            }

            var keys = new List<int>(intervalsByIndex.Keys);
            keys.Sort();
            for (var i = 0; i < keys.Count; i++)
            {
                var index = keys[i];
                result.Add(new SimResourceUtilizationStat
                {
                    Kind = kind,
                    ResourceIndex = index,
                    Label = FormatResourceLabel(kind, index, map, topology),
                    BusySeconds = MergeBusySeconds(intervalsByIndex[index]),
                    TotalSeconds = totalSimSeconds,
                });
            }
        }

        private static string FormatResourceLabel(
            SimResourceUtilizationKind kind,
            int index,
            WarehouseConveyorMap map,
            ConveyorMapTopology topology) =>
            kind switch
            {
                SimResourceUtilizationKind.Stacker => SimEntityNaming.FormatStacker(index),
                SimResourceUtilizationKind.InfeedPort => SimReportFormatting.FormatInfeedPort(map, topology, index),
                SimResourceUtilizationKind.OutfeedPort => SimReportFormatting.FormatOutfeedPort(map, topology, index),
                _ => index.ToString(),
            };

        private static void AddInterval(
            Dictionary<int, List<(double Start, double End)>> intervalsByIndex,
            int index,
            double start,
            double end)
        {
            if (!intervalsByIndex.TryGetValue(index, out var list))
            {
                list = new List<(double Start, double End)>();
                intervalsByIndex[index] = list;
            }

            list.Add((start, end));
        }

        private static double MergeBusySeconds(List<(double Start, double End)> intervals)
        {
            if (intervals == null || intervals.Count == 0)
            {
                return 0;
            }

            intervals.Sort((a, b) => a.Start.CompareTo(b.Start));
            var total = 0d;
            var currentStart = intervals[0].Start;
            var currentEnd = intervals[0].End;

            for (var i = 1; i < intervals.Count; i++)
            {
                var interval = intervals[i];
                if (interval.Start <= currentEnd + TimeEpsilon)
                {
                    if (interval.End > currentEnd)
                    {
                        currentEnd = interval.End;
                    }

                    continue;
                }

                total += currentEnd - currentStart;
                currentStart = interval.Start;
                currentEnd = interval.End;
            }

            total += currentEnd - currentStart;
            return total;
        }
    }
}
