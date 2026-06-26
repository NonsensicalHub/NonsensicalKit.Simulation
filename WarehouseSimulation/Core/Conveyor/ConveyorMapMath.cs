using System;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Core
{
    /// <summary>输送地图的物理量换算：速度、通过时间、路段容量与无向边键。</summary>
    public static class ConveyorMapMath
    {
        /// <summary>无向路段的稳定键（用于资源 ID，与方向无关，如 C1-P1）。</summary>
        public static string CanonicalSegmentKey(string nodeIdA, string nodeIdB) =>
            SimEntityNaming.CanonicalSegmentKey(nodeIdA, nodeIdB);

        public static float ResolveSpeedMetersPerSecond(WarehouseConveyorMap map, SimConveyorMapEdge edge)
        {
            if (edge.SpeedOverrideMetersPerSecond > 0f)
            {
                return edge.SpeedOverrideMetersPerSecond;
            }

            return map != null && map.DefaultSpeedMetersPerSecond > 0f
                ? map.DefaultSpeedMetersPerSecond
                : 0.6f;
        }

        public static float GetTransitSeconds(WarehouseConveyorMap map, SimConveyorMapEdge edge)
        {
            var speed = ResolveSpeedMetersPerSecond(map, edge);
            return Math.Max(0f, edge.DistanceMeters) / Math.Max(0.01f, speed);
        }

        /// <summary>单箱在输送线上的占用长度 / 碰撞长度（米），用于路段容量与跟车间距。</summary>
        public static float GetCargoOccupancyLengthMeters(WarehouseConveyorMap map) =>
            map != null && map.CargoUnitLengthMeters > 0f
                ? map.CargoUnitLengthMeters
                : 1.2f;

        public static int GetSegmentCapacity(WarehouseConveyorMap map, SimConveyorMapEdge edge)
        {
            var unit = GetCargoOccupancyLengthMeters(map);
            return Math.Max(1, (int)Math.Floor(Math.Max(0f, edge.DistanceMeters) / unit));
        }

        /// <summary>货箱尾端经过某点所需的额外时间（碰撞长度 ÷ 路段速度）。</summary>
        public static float GetCargoTailClearanceSeconds(WarehouseConveyorMap map, SimConveyorMapEdge edge)
        {
            var length = GetCargoOccupancyLengthMeters(map);
            var speed = ResolveSpeedMetersPerSecond(map, edge);
            return length / Math.Max(0.01f, speed);
        }

        /// <summary>相邻 zone 之间的 hop 时长（= 单箱长度 ÷ 路段速度），ZPA 步进单位。</summary>
        public static float GetZoneHopSeconds(WarehouseConveyorMap map, SimConveyorMapEdge edge) =>
            GetCargoTailClearanceSeconds(map, edge);

        /// <summary>zone 在路段上的归一化位置（0=起点，1=终点）。</summary>
        public static float GetZoneNormalizedPosition(int capacity, int slotIndex)
        {
            if (capacity <= 1)
            {
                return 1f;
            }

            var spacing = 1f / capacity;
            var t = (capacity - 1 - slotIndex) * spacing + spacing * 0.5f;
            return Math.Max(0f, Math.Min(1f, t));
        }

        /// <summary>货箱占用一整段路的总时长：前端通过 + 尾端离开（= (距离+碰撞长度)/速度）。</summary>
        public static float GetCargoSegmentOccupancySeconds(WarehouseConveyorMap map, SimConveyorMapEdge edge) =>
            GetTransitSeconds(map, edge) + GetCargoTailClearanceSeconds(map, edge);

        /// <summary>输送路段槽位资源 ID（与方向无关的段键 + 槽位序号）。</summary>
        public static string[] BuildSegmentSlotIds(
            WarehouseConveyorMap map,
            int fromNodeIndex,
            int toNodeIndex,
            int capacity)
        {
            var fromId = map?.Nodes != null && fromNodeIndex >= 0 && fromNodeIndex < map.Nodes.Length
                ? map.Nodes[fromNodeIndex].NodeId?.Trim()
                : null;
            var toId = map?.Nodes != null && toNodeIndex >= 0 && toNodeIndex < map.Nodes.Length
                ? map.Nodes[toNodeIndex].NodeId?.Trim()
                : null;
            var segKey = CanonicalSegmentKey(
                string.IsNullOrEmpty(fromId) ? $"n{fromNodeIndex}" : fromId,
                string.IsNullOrEmpty(toId) ? $"n{toNodeIndex}" : toId);
            var slotIds = new string[capacity];
            for (var s = 0; s < capacity; s++)
            {
                slotIds[s] = $"seg-{segKey}-slot-{s}";
            }

            return slotIds;
        }
    }
}
