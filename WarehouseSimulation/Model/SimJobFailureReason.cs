using System.Collections.Generic;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Model
{
    /// <summary>仿真任务失败原因（用于汇总统计）。</summary>
    public enum SimJobFailureReason
    {
        /// <summary>入库等候队列：无堆垛机可达空货位，批量终止剩余箱。</summary>
        InboundQueueNoAllocatableSlot,

        /// <summary>入库等候队列：仍有空货位但均不在堆垛机伸叉范围内，批量终止剩余箱。</summary>
        InboundQueueUnreachableFreeSlots,

        /// <summary>已建入库任务：从入库口无法规划至目标货位的输送路径。</summary>
        InboundConveyorRouteUnreachable,

        /// <summary>出库等候队列：无可用库存，批量终止剩余请求。</summary>
        OutboundQueueNoInventory,

        /// <summary>已建出库任务：无库存或无法规划输送路径。</summary>
        OutboundJobNoInventory,

        /// <summary>超过最大离散事件数，仿真中止；未完成任务计入此类。</summary>
        SimAbortedMaxEvents,

        /// <summary>事件队列耗尽但仍有未完成任务（非上述显式失败）。</summary>
        SimIncompleteRemaining,
    }

    /// <summary><see cref="SimJobFailureReason"/> 的人类可读标签与格式化。</summary>
    public static class SimJobFailureReasonLabels
    {
        private static readonly Dictionary<SimJobFailureReason, string> Labels = new()
        {
            [SimJobFailureReason.InboundQueueNoAllocatableSlot] = "入库队列—无可用货位（库满）",
            [SimJobFailureReason.InboundQueueUnreachableFreeSlots] = "入库队列—空货位不可达（堆垛机列域未覆盖）",
            [SimJobFailureReason.InboundConveyorRouteUnreachable] = "入库任务—输送路径不可达",
            [SimJobFailureReason.OutboundQueueNoInventory] = "出库队列—无可用库存",
            [SimJobFailureReason.OutboundJobNoInventory] = "出库任务—无库存或路径失败",
            [SimJobFailureReason.SimAbortedMaxEvents] = "仿真中止—超过最大事件数",
            [SimJobFailureReason.SimIncompleteRemaining] = "仿真未完成—任务滞留",
        };

        public static string GetLabel(SimJobFailureReason reason) =>
            Labels.TryGetValue(reason, out var label) ? label : reason.ToString();

        public static string FormatSummary(IReadOnlyDictionary<SimJobFailureReason, int> counts)
        {
            if (counts == null || counts.Count == 0)
            {
                return "（无失败）";
            }

            var parts = new List<string>();
            foreach (var kv in counts)
            {
                if (kv.Value <= 0)
                {
                    continue;
                }

                parts.Add($"{GetLabel(kv.Key)}: {kv.Value}");
            }

            return parts.Count > 0 ? string.Join("；", parts) : "（无失败）";
        }
    }
}
