using System.Collections.Generic;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Simulation;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Model
{
    /// <summary>
    /// 单箱仓库任务在仿真中的可变状态（入库 / 出库统一模型）。
    /// <para>由 <c>WarehouseSimEngine</c> 创建；各流程进程按阶段更新对应字段。</para>
    /// </summary>
    public sealed class WarehouseJob
    {
        #region 标识与时间

        public int JobId { get; }
        public SimFlowDirection Direction { get; set; } = SimFlowDirection.Inbound;
        public double ArrivalTime { get; set; }
        public WarehouseJobState State { get; set; } = WarehouseJobState.PendingArrival;

        /// <summary>当前阶段计划开始时刻。</summary>
        public double PhaseStartTime { get; set; }

        /// <summary>当前阶段计划结束时刻（各阶段复用，含义随 State 变化）。</summary>
        public double ScheduledCompleteTime { get; set; }

        #endregion

        #region 货位与设备绑定

        public GridIndex TargetSlot { get; set; }
        public bool HasSlot { get; set; }

        /// <summary>多入库口时：入库口序号（对应地图 InfeedPort 节点列表）。</summary>
        public int InfeedPortIndex { get; set; } = -1;

        /// <summary>多出库口时：出库口序号（对应地图 OutfeedPort 节点列表）。</summary>
        public int OutfeedPortIndex { get; set; } = -1;

        /// <summary>取货点节点在 ConveyorMap.Nodes 中的下标。</summary>
        public int PickupPointIndex { get; set; } = -1;

        public int AssignedStackerId { get; set; } = -1;

        /// <summary>货物需经过的加工站点标签（如 wrap）；空或 null 表示无要求。</summary>
        public string[] RequiredProcessTags { get; set; }

        #endregion

        #region 输送路径（选定后逐步推进 zone）

        /// <summary>仿真选定的输送路径（地图节点下标）。</summary>
        public List<int> ConveyorPathNodeIndices { get; set; }

        /// <summary>输送路径各路段的预约时刻（与仿真预约表一致）。</summary>
        public List<ConveyorSegmentScheduleEntry> ConveyorSegmentSchedule { get; set; }

        /// <summary>按行进顺序展开的输送 zone 链（离散事件逐步预约）。</summary>
        public List<ConveyorPathZone> ConveyorPathZones { get; set; }

        /// <summary>下一个待预约的 zone 下标。</summary>
        public int NextConveyorZoneIndex { get; set; }

        /// <summary>已批量写入 hop/停留子任务的路径边下标（避免重复记录）。</summary>
        public HashSet<int> ConveyorEdgeSubTasksRecorded { get; set; }

        /// <summary>入库口服务结束时刻（输送规划前保留，不受后续 ScheduledCompleteTime 覆盖）。</summary>
        public double InfeedCompleteSimTime { get; set; }

        /// <summary>出库时堆垛机在取货点放货完成时刻（反向输送规划起点）。</summary>
        public double PickupCompleteSimTime { get; set; }

        /// <summary>输送内核：货箱从起点（入库口 / 交互点）开始驶出的时刻。</summary>
        public double ConveyorStartMoveSimTime { get; set; } = double.NaN;

        /// <summary>输送内核：目标位置（取货点 / 出库口）上的货箱被取走的时刻。</summary>
        public double ConveyorGoalReleaseSimTime { get; set; } = double.NaN;

        /// <summary>已确认入库口到目标货位拓扑可达（忽略取货点容量），避免每次重试再扫一遍。</summary>
        public bool InboundRouteReachableChecked { get; set; }

        #endregion

        #region 资源预约（堆垛机 / 巷道）

        /// <summary>巷道列互斥资源 ID。0 号库为 <c>aisle-col-{列}</c>，其他仓库带仓库号。</summary>
        public string AisleResourceId { get; set; }
        public double AisleReserveStart { get; set; }
        public double AisleReserveEnd { get; set; }

        /// <summary>堆垛机本体互斥资源 ID；与取货点 zone 预约可能已在输送阶段写入。</summary>
        public string StackerResourceId { get; set; }
        /// <summary>堆垛机取+移+放连续作业的实际开始时刻。</summary>
        public double StackerReserveStart { get; set; }
        /// <summary>堆垛机作业计划结束时刻（放货完成）。</summary>
        public double StackerReserveEnd { get; set; }

        #endregion

        #region 统计（完成时写入 SimRunResult）

        public double ServiceTimeAccum { get; set; }
        public double WaitTimeAccum { get; set; }

        /// <summary>最近一次显著等待所阻塞的资源 ID（用于瓶颈分析）。</summary>
        public string LastWaitResource { get; set; }

        #endregion

        #region 回放快照（仿真结束后冻结，供子任务/事件共享引用）

        public int[] PlaybackPathSnapshot { get; set; }
        public ConveyorSegmentScheduleEntry[] PlaybackScheduleSnapshot { get; set; }

        #endregion

        public WarehouseJob(int jobId, double arrivalTime)
        {
            JobId = jobId;
            ArrivalTime = arrivalTime;
        }
    }
}
