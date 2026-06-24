using System;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <summary>
    /// 跟踪每台堆垛机当前排/层位置（放货完成时更新），以及已预约作业的最晚结束时刻。
    /// 未来预约不再污染 <see cref="GetCarriageAt"/>，避免驶向时长被算成 0。
    /// </summary>
    public sealed class StackerCarriageBookkeeper
    {
        private double[] _bookedEndTime = Array.Empty<double>();
        private int[] _bookedRow = Array.Empty<int>();
        private int[] _bookedLevel = Array.Empty<int>();
        private int[] _currentRow = Array.Empty<int>();
        private int[] _currentLevel = Array.Empty<int>();

        public void Reset(int stackerCount)
        {
            var count = Math.Max(1, stackerCount);
            _bookedEndTime = new double[count];
            _bookedRow = new int[count];
            _bookedLevel = new int[count];
            _currentRow = new int[count];
            _currentLevel = new int[count];
        }

        public double GetBookedEndTime(int stackerId) =>
            IsValid(stackerId) ? _bookedEndTime[stackerId] : 0d;

        public (int row, int level) GetCarriageAt(int stackerId) =>
            IsValid(stackerId) ? (_currentRow[stackerId], _currentLevel[stackerId]) : (0, 0);

        public void SetCarriagePosition(int stackerId, int row, int level)
        {
            if (!IsValid(stackerId))
            {
                return;
            }

            _currentRow[stackerId] = row;
            _currentLevel[stackerId] = level;
        }

        /// <summary>记录堆垛机作业预约结束时刻（供冲突检测）；不修改当前物理位置。</summary>
        public void CommitBooking(int stackerId, double endTime, int endRow, int endLevel)
        {
            if (!IsValid(stackerId) || endTime < _bookedEndTime[stackerId] - 1e-9)
            {
                return;
            }

            _bookedEndTime[stackerId] = endTime;
            _bookedRow[stackerId] = endRow;
            _bookedLevel[stackerId] = endLevel;
        }

        private bool IsValid(int stackerId) =>
            stackerId >= 0 && stackerId < _bookedEndTime.Length;
    }
}
