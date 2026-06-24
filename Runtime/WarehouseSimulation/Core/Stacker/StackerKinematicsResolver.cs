namespace NonsensicalKit.Simulation.WarehouseSimulation.Core
{
    /// <summary>按堆垛机编号解析运动学参数。</summary>
    public static class StackerKinematicsResolver
    {
        public static IStackerKinematics Resolve(
            IStackerFleetDescriptor fleet,
            ConveyorMapTopology topology,
            int stackerId)
        {
            if (StackerColumnReachUtility.TryGetDefinition(fleet, topology, stackerId, out var definition)
                && definition.Kinematics != null)
            {
                return definition.Kinematics;
            }

            return fleet?.DefaultStackerKinematics ?? StackerKinematicsConfig.CreateDefault();
        }
    }
}
