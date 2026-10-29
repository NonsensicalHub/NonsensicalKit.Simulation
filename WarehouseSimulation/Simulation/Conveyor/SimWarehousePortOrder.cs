using System.Collections.Generic;
using System.Runtime.CompilerServices;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <summary>
    /// 库号限定时，用已经缓存的可达取货点判断入库口能到哪些堆垛机。
    /// 到不了目标库的入库口不再进入货位扫描。
    /// </summary>
    internal static class SimWarehousePortOrder
    {
        private sealed class TopologyCache
        {
            public int BindingsToken;
            public readonly Dictionary<string, ReachMap> ByAccess = new();
        }

        private sealed class ReachMap
        {
            public int[][] StackersByPort;
            public int[][] WarehousesByPort;
        }

        private static readonly ConditionalWeakTable<ConveyorMapTopology, TopologyCache> Caches = new();
        private static int[] _matchedScratch = new int[8];

        public static int BringLocalPortsForward(
            ConveyorMapTopology topology,
            IWarehouseSimulationBindings bindings,
            int[] portOrder,
            int count,
            int[] allowedWarehouseIds,
            ConveyorRouteAccess access)
        {
            if (topology == null
                || bindings == null
                || portOrder == null
                || count <= 0
                || !SimWarehouseRestriction.IsRestricted(allowedWarehouseIds))
            {
                return count;
            }

            var map = Reach(topology, bindings, access);
            if (map == null)
            {
                return count;
            }

            if (_matchedScratch.Length < count)
            {
                _matchedScratch = new int[count];
            }

            var matched = 0;
            var anyKnown = false;
            for (var i = 0; i < count; i++)
            {
                var port = portOrder[i];
                if ((uint)port >= (uint)map.WarehousesByPort.Length)
                {
                    continue;
                }

                var warehouses = map.WarehousesByPort[port];
                if (warehouses == null || warehouses.Length == 0)
                {
                    continue;
                }

                anyKnown = true;
                if (!Intersects(warehouses, allowedWarehouseIds))
                {
                    continue;
                }

                _matchedScratch[matched++] = port;
            }

            if (!anyKnown)
            {
                return count;
            }

            for (var i = 0; i < matched; i++)
            {
                portOrder[i] = _matchedScratch[i];
            }

            return matched;
        }

        public static bool ReachesStacker(
            ConveyorMapTopology topology,
            IWarehouseSimulationBindings bindings,
            int portIndex,
            int stackerId,
            int[] allowedWarehouseIds,
            ConveyorRouteAccess access)
        {
            if (!SimWarehouseRestriction.IsRestricted(allowedWarehouseIds))
            {
                return true;
            }

            var map = Reach(topology, bindings, access);
            if (map == null || (uint)portIndex >= (uint)map.StackersByPort.Length)
            {
                return true;
            }

            var stackers = map.StackersByPort[portIndex];
            if (stackers == null || stackers.Length == 0)
            {
                return true;
            }

            for (var i = 0; i < stackers.Length; i++)
            {
                if (stackers[i] == stackerId)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool Intersects(int[] warehouses, int[] allowed)
        {
            for (var i = 0; i < warehouses.Length; i++)
            {
                if (SimWarehouseRestriction.Allows(allowed, warehouses[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static ReachMap Reach(
            ConveyorMapTopology topology,
            IWarehouseSimulationBindings bindings,
            ConveyorRouteAccess access)
        {
            var portCount = topology.InfeedNodeIndices != null ? topology.InfeedNodeIndices.Count : 0;
            if (portCount <= 0)
            {
                return null;
            }

            var cache = Caches.GetOrCreateValue(topology);
            var token = RuntimeHelpers.GetHashCode(bindings);
            if (cache.BindingsToken != token)
            {
                cache.BindingsToken = token;
                cache.ByAccess.Clear();
            }

            var signature = access.Signature ?? string.Empty;
            if (cache.ByAccess.TryGetValue(signature, out var map)
                && map.StackersByPort != null
                && map.StackersByPort.Length == portCount)
            {
                return map;
            }

            map = Build(topology, bindings, access, portCount);
            cache.ByAccess[signature] = map;
            return map;
        }

        private static ReachMap Build(
            ConveyorMapTopology topology,
            IWarehouseSimulationBindings bindings,
            ConveyorRouteAccess access,
            int portCount)
        {
            var stackers = new int[portCount][];
            var warehouses = new int[portCount][];
            var stackerIds = new List<int>(8);
            var warehouseIds = new List<int>(4);
            for (var port = 0; port < portCount; port++)
            {
                stackerIds.Clear();
                warehouseIds.Clear();
                var infeedNode = topology.InfeedNodeIndices[port];
                var pickups = topology.GetAccessiblePickupNodeIndices(infeedNode, access);
                for (var i = 0; i < pickups.Length; i++)
                {
                    ref var node = ref topology.GetNode(pickups[i]);
                    if (node.StackerId < 0 || Contains(stackerIds, node.StackerId))
                    {
                        continue;
                    }

                    stackerIds.Add(node.StackerId);
                    var warehouseId = StackerColumnReachUtility.GetWarehouseId(bindings, topology, node.StackerId);
                    if (!Contains(warehouseIds, warehouseId))
                    {
                        warehouseIds.Add(warehouseId);
                    }
                }

                stackers[port] = stackerIds.ToArray();
                warehouses[port] = warehouseIds.ToArray();
            }

            return new ReachMap
            {
                StackersByPort = stackers,
                WarehousesByPort = warehouses,
            };
        }

        private static bool Contains(List<int> values, int id)
        {
            for (var i = 0; i < values.Count; i++)
            {
                if (values[i] == id)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
