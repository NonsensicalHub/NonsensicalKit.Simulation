using System;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Model
{
    /// <summary>单任务子任务时间轴重叠或空白（仿真后自检）。</summary>
    [Serializable]
    public struct SimSubTaskTimelineIssueRecord
    {
        public int JobId;
        public SimSubTaskTimelineIssueKind Kind;

        /// <summary>重叠：子任务 A；空白：空白段前的子任务（无则 -1）。</summary>
        public int SubTaskIdA;

        /// <summary>重叠：子任务 B；空白：空白段后的子任务（无则 -1）。</summary>
        public int SubTaskIdB;
        public SimSubTaskKind KindA;
        public SimSubTaskKind KindB;

        public double SubTaskAStart;
        public double SubTaskAEnd;
        public double SubTaskBStart;
        public double SubTaskBEnd;

        /// <summary>重叠或空白区间的起点（仿真秒）。</summary>
        public double IssueStart;

        /// <summary>重叠或空白区间的终点（仿真秒，半开区间终点）。</summary>
        public double IssueEnd;

        public double IssueSeconds;
    }
}
