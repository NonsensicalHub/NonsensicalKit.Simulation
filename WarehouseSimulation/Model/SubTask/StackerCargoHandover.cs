namespace NonsensicalKit.Simulation.WarehouseSimulation.Model
{
    /// <summary>
    /// 取货/放货交接时刻的唯一算法。
    /// 比例默认值只在这里读取；已写入时间轴的交接时刻优先于比例。
    /// </summary>
    public static class StackerCargoHandover
    {
        private const double TimeEpsilon = 1e-6;

        /// <summary>阶段 [start, end] 内的交接仿真时刻。</summary>
        public static double FromPhase(double startSimTime, double endSimTime)
        {
            var span = endSimTime - startSimTime;
            if (span <= TimeEpsilon)
            {
                return endSimTime;
            }

            return startSimTime + span * SimSubTaskKinds.StackerForkCargoAttachProgress;
        }

        /// <summary>时间轴上的交接时刻。旧记录没有字段时退回阶段比例。</summary>
        public static double Resolve(in SimSubTask task)
        {
            if (task.HasCargoHandover)
            {
                return task.CargoHandoverSimTime;
            }

            return FromPhase(task.StartSimTime, task.EndSimTime);
        }

        /// <summary>该时刻货物是否已经完成这一阶段的交接。</summary>
        public static bool HasTransferred(in SimSubTask task, double simTime) =>
            simTime + TimeEpsilon >= Resolve(in task);

        /// <summary>
        /// 伸叉段在阶段内的比例。回放插值与交接使用同一时刻，不再各写一个 0.55。
        /// </summary>
        public static float ExtendFraction(in SimSubTask task)
        {
            var span = task.EndSimTime - task.StartSimTime;
            if (!task.HasCargoHandover || span <= TimeEpsilon)
            {
                return SimSubTaskKinds.StackerForkCargoAttachProgress;
            }

            var fraction = (task.CargoHandoverSimTime - task.StartSimTime) / span;
            if (fraction <= 0d)
            {
                return 0f;
            }

            if (fraction >= 1d)
            {
                return 1f;
            }

            return (float)fraction;
        }

        /// <summary>取货交接之后、放货交接之前，货物归堆垛机持有。</summary>
        public static bool IsHeldByStacker(in SimSubTask active, double simTime) =>
            active.Kind switch
            {
                SimSubTaskKind.StackerPick => HasTransferred(in active, simTime),
                SimSubTaskKind.StackerMove => true,
                SimSubTaskKind.StackerPlace => !HasTransferred(in active, simTime),
                _ => false,
            };
    }
}
