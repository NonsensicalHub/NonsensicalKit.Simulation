using System;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <summary>
    /// 堆垛机位置的两套状态：
    /// 当前状态是仿真时钟已经走到的排/层；计划尾是已提交作业结束后的预计排/层。
    /// 下一项作业的起点只读计划尾，不读当前物理位置。
    /// </summary>
    public sealed class StackerCarriageBookkeeper
    {
        private double[] _bookedEndTime = Array.Empty<double>();
        private int[] _bookedRow = Array.Empty<int>();
        private int[] _bookedLevel = Array.Empty<int>();
        private bool[] _hasPlanTail = Array.Empty<bool>();
        private int[] _currentRow = Array.Empty<int>();
        private int[] _currentLevel = Array.Empty<int>();

        public void Reset(int stackerCount)
        {
            var count = Math.Max(1, stackerCount);
            _bookedEndTime = new double[count];
            _bookedRow = new int[count];
            _bookedLevel = new int[count];
            _hasPlanTail = new bool[count];
            _currentRow = new int[count];
            _currentLevel = new int[count];
        }

        public double GetBookedEndTime(int stackerId) =>
            IsValid(stackerId) ? _bookedEndTime[stackerId] : 0d;

        /// <summary>截至当前仿真事件已经到达的排/层。规划下一项作业不要用这个。</summary>
        public (int row, int level) GetCarriageAt(int stackerId) =>
            IsValid(stackerId) ? (_currentRow[stackerId], _currentLevel[stackerId]) : (0, 0);

        /// <summary>下一项作业的起点：已有提交计划时用计划尾，否则用当前状态。</summary>
        public (int row, int level) GetPlanningOrigin(int stackerId)
        {
            if (!IsValid(stackerId))
            {
                return (0, 0);
            }

            return _hasPlanTail[stackerId]
                ? (_bookedRow[stackerId], _bookedLevel[stackerId])
                : (_currentRow[stackerId], _currentLevel[stackerId]);
        }

        public void SetCarriagePosition(int stackerId, int row, int level)
        {
            if (!IsValid(stackerId))
            {
                return;
            }

            _currentRow[stackerId] = row;
            _currentLevel[stackerId] = level;
        }

        /// <summary>提交作业的预计结束排/层。不修改当前状态。</summary>
        public void CommitBooking(int stackerId, double endTime, int endRow, int endLevel)
        {
            if (!IsValid(stackerId) || endTime < _bookedEndTime[stackerId] - 1e-9)
            {
                return;
            }

            _bookedEndTime[stackerId] = endTime;
            _bookedRow[stackerId] = endRow;
            _bookedLevel[stackerId] = endLevel;
            _hasPlanTail[stackerId] = true;
        }

        private bool IsValid(int stackerId) =>
            stackerId >= 0 && stackerId < _bookedEndTime.Length;
    }
}
