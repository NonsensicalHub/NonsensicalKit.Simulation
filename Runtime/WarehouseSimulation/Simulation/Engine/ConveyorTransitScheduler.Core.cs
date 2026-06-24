using System;
using System.Collections.Generic;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <content>
    /// 输送调度器的内部辅助类型：整路径构建结果、对 <see cref="WarehouseJob"/> 的变更累积、
    /// 以及「先试探预约、成功后再一次性提交」的事务封装（取货点段需与堆垛机时长耦合）。
    /// </content>
    public sealed partial class ConveyorTransitScheduler
    {
        /// <summary>整路径规划返回值：对外计划 + 待写入 job 的累计变更。</summary>
        private readonly struct TransitBuildResult
        {
            public readonly ConveyorTransitPlan Plan;
            public readonly TransitMutationAccumulator Mutations;

            public TransitBuildResult(ConveyorTransitPlan plan, TransitMutationAccumulator mutations)
            {
                Plan = plan;
                Mutations = mutations;
            }
        }

        /// <summary>将路段规划产生的等待/服务/堆垛机预约增量合并到任务对象。</summary>
        private static class ConveyorTransitApplier
        {
            public static void ApplyToJob(WarehouseJob job, TransitMutationAccumulator mutations)
            {
                mutations.CommitTo(job);
            }
        }

        /// <summary>
        /// 单段或单 zone 预约过程中累积的统计量，最后一次性提交到 <see cref="WarehouseJob"/>，
        /// 避免中途失败时 job 上出现不一致的半成品字段。
        /// </summary>
        private sealed class TransitMutationAccumulator
        {
            private double _waitDelta;
            private double _serviceDelta;
            private string _lastWaitResource;
            private bool _hasStackerReserve;
            private double _stackerReserveStart;
            private double _stackerReserveEnd;

            public void ApplySegmentMetrics(SegmentMetrics metrics)
            {
                if (metrics.EntryWaitDelta > 1e-9)
                {
                    _waitDelta += metrics.EntryWaitDelta;
                    _lastWaitResource = metrics.EntryWaitResourceId;
                }

                if (metrics.DownstreamWaitDelta > 1e-9)
                {
                    _waitDelta += metrics.DownstreamWaitDelta;
                    _lastWaitResource = metrics.DownstreamWaitResourceId;
                }

                _serviceDelta += metrics.ServiceDelta;
            }

            public void SetStackerReserve(double start, double end)
            {
                _hasStackerReserve = true;
                _stackerReserveStart = start;
                _stackerReserveEnd = end;
            }

            public void CommitTo(WarehouseJob job)
            {
                if (job == null)
                {
                    return;
                }

                job.WaitTimeAccum += _waitDelta;
                job.ServiceTimeAccum += _serviceDelta;
                if (!string.IsNullOrEmpty(_lastWaitResource))
                {
                    job.LastWaitResource = _lastWaitResource;
                }

                if (_hasStackerReserve)
                {
                    job.StackerReserveStart = _stackerReserveStart;
                    job.StackerReserveEnd = _stackerReserveEnd;
                }
            }
        }

        /// <summary>
        /// 预约事务：在 <see cref="Commit"/> 之前可随时 <see cref="Rollback"/> 撤销本次试探写入的所有区间。
        /// 用于取货点 ZPA 链与堆垛机资源需要迭代收敛的场景。
        /// </summary>
        private sealed class ReservationTransaction
        {
            private readonly ReservationTable _table;
            private readonly List<(string resourceId, double start, double end)> _applied = new();
            private bool _committed;

            public ReservationTransaction(ReservationTable table)
            {
                _table = table;
            }

            public void TryReserve(string resourceId, double start, double end, out double reservedStart)
            {
                _table.TryReserve(resourceId, start, end, out reservedStart);
                var duration = end - start;
                if (duration <= 1e-9)
                {
                    return;
                }

                _applied.Add((resourceId, reservedStart, reservedStart + duration));
            }

            public void Commit()
            {
                _committed = true;
                _applied.Clear();
            }

            public void Rollback()
            {
                if (_committed)
                {
                    return;
                }

                for (var i = _applied.Count - 1; i >= 0; i--)
                {
                    var entry = _applied[i];
                    _table.Release(entry.resourceId, entry.start, entry.end);
                }

                _applied.Clear();
            }

            public void Release(string resourceId, double start, double end)
            {
                if (_committed)
                {
                    return;
                }

                _table.Release(resourceId, start, end);
                for (var i = _applied.Count - 1; i >= 0; i--)
                {
                    var entry = _applied[i];
                    if (entry.resourceId == resourceId
                        && Math.Abs(entry.start - start) < 1e-6
                        && Math.Abs(entry.end - end) < 1e-6)
                    {
                        _applied.RemoveAt(i);
                        return;
                    }
                }
            }
        }

    }
}
