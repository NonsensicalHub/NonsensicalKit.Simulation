using System;
using System.Collections.Generic;
using System.Text;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Model
{
    /// <summary>单个入出库任务的完整子任务校验报告（供 Inspector / 复制校验）。</summary>
    [Serializable]
    public sealed class SimCargoJobValidationReport
    {
        public int JobId = -1;
        public bool IsOutbound;
        public GridIndex TargetSlot;
        public int InfeedPortIndex = -1;
        public int OutfeedPortIndex = -1;
        public int PickupPointIndex = -1;
        public int StackerId = -1;
        public double SimTime;
        public double JobStartSimTime;
        public double JobEndSimTime;
        public double JobDurationSeconds;
        public int PathNodeCount;
        public int SegmentCount;
        public string PathNodeSummary = string.Empty;
        public int ActiveSubTaskSequence;
        public string ActiveSubTaskLabel = "—";
        public float ActiveSubTaskProgress;
        public bool HasStarted;
        public bool IsCompleted;
        public List<SimCargoSubTaskDisplayEntry> SubTasks = new();
        public string FullReportText = string.Empty;
        public ConveyorSegmentScheduleEntry[] SegmentSchedule = System.Array.Empty<ConveyorSegmentScheduleEntry>();

        public void Clear()
        {
            JobId = -1;
            IsOutbound = false;
            TargetSlot = default;
            InfeedPortIndex = -1;
            OutfeedPortIndex = -1;
            PickupPointIndex = -1;
            StackerId = -1;
            SimTime = 0;
            JobStartSimTime = 0;
            JobEndSimTime = 0;
            JobDurationSeconds = 0;
            PathNodeCount = 0;
            SegmentCount = 0;
            PathNodeSummary = string.Empty;
            ActiveSubTaskSequence = 0;
            ActiveSubTaskLabel = "—";
            ActiveSubTaskProgress = 0;
            HasStarted = false;
            IsCompleted = false;
            SubTasks.Clear();
            FullReportText = string.Empty;
            SegmentSchedule = System.Array.Empty<ConveyorSegmentScheduleEntry>();
        }
    }

}
