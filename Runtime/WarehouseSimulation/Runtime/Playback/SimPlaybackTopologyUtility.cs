using UnityEngine;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Runtime
{
    /// <summary>回放侧输送拓扑构建（Handler 共用）。</summary>
    public static class SimPlaybackTopologyUtility
    {
        public static bool TryBuild(
            WarehouseConveyorMap map,
            out ConveyorMapTopology topology,
            Object context = null) =>
            TryBuild(map, null, out topology, context);

        public static bool TryBuild(
            WarehouseConveyorMap map,
            IStackerFleetDescriptor fleet,
            out ConveyorMapTopology topology,
            Object context = null)
        {
            topology = null;
            if (map == null)
            {
                return false;
            }

            if (!ConveyorMapTopology.TryBuild(map, fleet, out topology, out var error))
            {
                Debug.LogError($"[SimPlayback] 输送地图无效：{error}", context);
                return false;
            }

            return true;
        }
    }
}
