using System;
using System.Collections.Generic;
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

        /// <summary>
        /// 路段容量（停留点个数）：两端各留出 <see cref="GetEdgeEndMarginMeters"/>（≥ 一个箱长），
        /// 中间停留点间距不小于一个箱长。放不下时至少 1 个（短路段由内核的车身间距规则保证不重叠）。
        /// </summary>
        public static int GetSegmentCapacity(WarehouseConveyorMap map, SimConveyorMapEdge edge)
        {
            var unit = GetCargoOccupancyLengthMeters(map);
            var usable = Math.Max(0f, edge.DistanceMeters) - 2f * GetEdgeEndMarginMeters(map, edge);
            var cap = usable < 0f ? 1 : Math.Max(1, (int)Math.Floor(usable / unit + 1e-4f) + 1);
            // 死胡同加工站（度数 1）的进出边：容量强制 1，避免多箱挤进后再折返对向卡死。
            if (IsDeadEndProcessSpurEdge(map, edge))
            {
                return 1;
            }

            return cap;
        }

        /// <summary>边是否连到仅有一条无向邻接的加工站（进站必须原路折返）。</summary>
        public static bool IsDeadEndProcessSpurEdge(WarehouseConveyorMap map, SimConveyorMapEdge edge)
        {
            // SimConveyorMapEdge 是 struct，不能与 null 比较；无效边用空 NodeId 判断。
            if (map?.Nodes == null
                || map.Edges == null
                || string.IsNullOrWhiteSpace(edge.FromNodeId)
                || string.IsNullOrWhiteSpace(edge.ToNodeId))
            {
                return false;
            }

            var from = FindNodeIndexById(map, edge.FromNodeId);
            var to = FindNodeIndexById(map, edge.ToNodeId);
            if (from < 0 || to < 0)
            {
                return false;
            }

            return IsDeadEndProcessStationNode(map, from) || IsDeadEndProcessStationNode(map, to);
        }

        public static bool IsDeadEndProcessStationNode(WarehouseConveyorMap map, int nodeIndex)
        {
            if (map?.Nodes == null || nodeIndex < 0 || nodeIndex >= map.Nodes.Length)
            {
                return false;
            }

            ref var node = ref map.Nodes[nodeIndex];
            if (node.Kind != SimConveyorNodeKind.ProcessStation)
            {
                return false;
            }

            return CountUndirectedNeighbors(map, nodeIndex) <= 1;
        }

        private static int FindNodeIndexById(WarehouseConveyorMap map, string nodeId)
        {
            if (map?.Nodes == null || string.IsNullOrWhiteSpace(nodeId))
            {
                return -1;
            }

            var trimmed = nodeId.Trim();
            for (var i = 0; i < map.Nodes.Length; i++)
            {
                if (string.Equals(map.Nodes[i].NodeId?.Trim(), trimmed, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        private static int CountUndirectedNeighbors(WarehouseConveyorMap map, int nodeIndex)
        {
            var id = map.Nodes[nodeIndex].NodeId?.Trim();
            if (string.IsNullOrEmpty(id) || map.Edges == null)
            {
                return 0;
            }

            var neighbors = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < map.Edges.Length; i++)
            {
                var e = map.Edges[i];
                var a = e.FromNodeId?.Trim();
                var b = e.ToNodeId?.Trim();
                if (string.Equals(a, id, StringComparison.Ordinal) && !string.IsNullOrEmpty(b))
                {
                    neighbors.Add(b);
                }
                else if (string.Equals(b, id, StringComparison.Ordinal) && !string.IsNullOrEmpty(a))
                {
                    neighbors.Add(a);
                }
            }

            return neighbors.Count;
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

        /// <summary>
        /// 路段端部留白（米）：末端 / 首端停留点中心到节点中心的距离 = 单箱长度 + 节点前停车额外留白。
        /// 这样节点上的货与节点前停车等待的货中心距不小于一个箱长（以前是半个箱长，回放会重叠）。
        /// </summary>
        public static float GetEdgeEndMarginMeters(WarehouseConveyorMap map, SimConveyorMapEdge edge) =>
            GetCargoOccupancyLengthMeters(map) + GetNodeStopExtraClearanceMeters(map);

        /// <summary>节点前停车额外留白（米，≥0）。</summary>
        public static float GetNodeStopExtraClearanceMeters(WarehouseConveyorMap map) =>
            map != null && map.NodeStopExtraClearanceMeters > 0f ? map.NodeStopExtraClearanceMeters : 0f;

        /// <summary>路段端部留白（归一化 0–1）。</summary>
        public static float GetEdgeEndMarginNormalized(WarehouseConveyorMap map, SimConveyorMapEdge edge)
        {
            var distance = Math.Max(0f, edge.DistanceMeters);
            if (distance <= 1e-6f)
            {
                return 0f;
            }

            var margin = GetEdgeEndMarginMeters(map, edge);
            return Math.Min(0.49f, margin / distance);
        }

        /// <summary>按归一化路程换算通过时间。</summary>
        public static float GetTransitSecondsForNormalizedSpan(
            WarehouseConveyorMap map,
            SimConveyorMapEdge edge,
            float normalizedSpan)
        {
            var speed = ResolveSpeedMetersPerSecond(map, edge);
            var distance = Math.Abs(normalizedSpan) * Math.Max(0f, edge.DistanceMeters);
            return distance / Math.Max(0.01f, speed);
        }

        /// <summary>从路段端点（节点）到首/末停留点的 hop 时长（= 端部留白 ÷ 速度）。</summary>
        public static float GetEdgeTerminalHopSeconds(WarehouseConveyorMap map, SimConveyorMapEdge edge) =>
            GetTransitSecondsForNormalizedSpan(map, edge, GetEdgeEndMarginNormalized(map, edge));

        /// <summary>
        /// 从上游节点驶入本段入口停留点的 hop。
        /// 多槽时入口槽就在端部留白处；单槽时唯一停留点靠近终点，路程是整段减去端部留白。
        /// </summary>
        public static float GetEdgeEntrySlotHopSeconds(WarehouseConveyorMap map, SimConveyorMapEdge edge)
        {
            var capacity = GetSegmentCapacity(map, edge);
            var entrySlot = Math.Max(0, capacity - 1);
            return GetZoneHopSecondsFromPrevious(map, edge, entrySlot);
        }

        /// <summary>从上一位置驶入指定槽位 zone 的 hop 时长（按实际路程）。</summary>
        public static float GetZoneHopSecondsFromPrevious(
            WarehouseConveyorMap map,
            SimConveyorMapEdge edge,
            int slotIndex)
        {
            var capacity = GetSegmentCapacity(map, edge);
            slotIndex = Math.Max(0, Math.Min(capacity - 1, slotIndex));
            var margin = GetEdgeEndMarginNormalized(map, edge);

            if (capacity <= 1)
            {
                return GetTransitSecondsForNormalizedSpan(map, edge, 1f - margin);
            }

            if (slotIndex >= capacity - 1)
            {
                return GetEdgeTerminalHopSeconds(map, edge);
            }

            var step = GetInteriorSlotSpacingNormalized(map, edge, capacity);
            return GetTransitSecondsForNormalizedSpan(map, edge, step);
        }

        /// <summary>从末槽位驶入下游节点（路口/取货点等）的 hop 时长。</summary>
        public static float GetNodeApproachHopSeconds(WarehouseConveyorMap map, SimConveyorMapEdge edge) =>
            GetEdgeTerminalHopSeconds(map, edge);

        private const float GateUnsetEpsilon = 1e-4f;
        private const float GateResolvedStart = 0.001f;
        private const float GateResolvedEnd = 0.999f;

        /// <summary>
        /// 中间门的归一化位置（0=起点，1=终点）。配置为 0 时视为路段中点。
        /// </summary>
        public static float ResolveGateNormalizedPosition(in SimConveyorMapEdge edge)
        {
            var t = edge.GateNormalizedPosition;
            if (t <= GateUnsetEpsilon)
            {
                return 0.5f;
            }

            if (t >= 1f)
            {
                return GateResolvedEnd;
            }

            return t;
        }

        /// <summary>
        /// 反向路段上的门位置。0 表示「使用中点」，保持 0。
        /// 正向在终点时，反向落到起点侧；不能写回 0，否则会被当成中点。
        /// </summary>
        public static float MirrorGateNormalizedPosition(float position)
        {
            if (position <= GateUnsetEpsilon)
            {
                return 0f;
            }

            var resolved = position >= 1f ? GateResolvedEnd : position;
            var mirrored = 1f - resolved;
            if (mirrored <= GateUnsetEpsilon)
            {
                return GateResolvedStart;
            }

            if (mirrored >= 1f)
            {
                return GateResolvedEnd;
            }

            return mirrored;
        }

        /// <summary>
        /// 中间门前的等待落在哪个停留点：门位置之前、最靠近门的那个槽位。
        /// 门在全部停留点之前时，停在入口停留点。
        /// </summary>
        public static bool TryGetGateHoldSlot(
            WarehouseConveyorMap map,
            in SimConveyorMapEdge edge,
            out int slotIndex,
            out float waitSeconds)
        {
            waitSeconds = edge.GateWaitSeconds;
            slotIndex = -1;
            if (waitSeconds <= 1e-4f)
            {
                return false;
            }

            var capacity = GetSegmentCapacity(map, edge);
            if (capacity <= 0)
            {
                return false;
            }

            var gateT = ResolveGateNormalizedPosition(in edge);
            var bestSlot = -1;
            var bestT = -1f;
            for (var s = 0; s < capacity; s++)
            {
                var t = GetZoneNormalizedPosition(map, edge, capacity, s);
                if (t <= gateT + 1e-4f && t > bestT)
                {
                    bestT = t;
                    bestSlot = s;
                }
            }

            slotIndex = bestSlot >= 0 ? bestSlot : capacity - 1;
            return true;
        }

        /// <summary>中间门资源 ID（与方向无关，用于等待统计）。</summary>
        public static string BuildSegmentGateResourceId(
            WarehouseConveyorMap map,
            int fromNodeIndex,
            int toNodeIndex)
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
            return $"seg-{segKey}-gate";
        }

        /// <summary>zone 在路段上的归一化位置（0=起点，1=终点）。</summary>
        public static float GetZoneNormalizedPosition(
            WarehouseConveyorMap map,
            SimConveyorMapEdge edge,
            int slotIndex)
        {
            var capacity = GetSegmentCapacity(map, edge);
            return GetZoneNormalizedPosition(map, edge, capacity, slotIndex);
        }

        /// <summary>zone 在路段上的归一化位置（0=起点，1=终点）。</summary>
        public static float GetZoneNormalizedPosition(
            WarehouseConveyorMap map,
            SimConveyorMapEdge edge,
            int capacity,
            int slotIndex)
        {
            if (capacity <= 0)
            {
                return 1f;
            }

            slotIndex = Math.Max(0, Math.Min(capacity - 1, slotIndex));
            var margin = GetEdgeEndMarginNormalized(map, edge);

            if (capacity <= 1)
            {
                return Math.Max(margin, 1f - margin);
            }

            var step = GetInteriorSlotSpacingNormalized(map, edge, capacity);
            return margin + (capacity - 1 - slotIndex) * step;
        }

        /// <summary>
        /// zone 在路段上的归一化位置（兼容旧调用；无地图时使用均匀均分，不含端部留白）。
        /// 有地图/边时请改用 <see cref="GetZoneNormalizedPosition(WarehouseConveyorMap,SimConveyorMapEdge,int,int)"/>。
        /// </summary>
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

        private static float GetInteriorSlotSpacingNormalized(
            WarehouseConveyorMap map,
            SimConveyorMapEdge edge,
            int capacity)
        {
            if (capacity <= 1)
            {
                return 0f;
            }

            var margin = GetEdgeEndMarginNormalized(map, edge);
            return (1f - 2f * margin) / (capacity - 1);
        }

        /// <summary>货箱占用一整段路的总时长：前端通过 + 尾端离开（= (距离+碰撞长度)/速度）。</summary>
        public static float GetCargoSegmentOccupancySeconds(WarehouseConveyorMap map, SimConveyorMapEdge edge) =>
            GetTransitSeconds(map, edge) + GetCargoTailClearanceSeconds(map, edge);

        /// <summary>输送路段槽位资源 ID（无向段键 + 槽位序号）。</summary>
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

        /// <summary>
        /// 加工站占用路口互斥资源：首次经过路口 X 独占，再次经过时释放（见 ProcessOccupiedJunctionLogicalId）。
        /// </summary>
        public static string BuildProcessOccupiedJunctionResourceId(string junctionLogicalId)
        {
            if (string.IsNullOrWhiteSpace(junctionLogicalId))
            {
                return null;
            }

            return $"proc-junc-excl|{junctionLogicalId.Trim()}";
        }

        /// <summary>按逻辑 ID 查找节点下标；找不到返回 -1。</summary>
        public static int FindNodeIndexByLogicalId(WarehouseConveyorMap map, string logicalId)
        {
            if (map?.Nodes == null || string.IsNullOrWhiteSpace(logicalId))
            {
                return -1;
            }

            var trimmed = logicalId.Trim();
            for (var i = 0; i < map.Nodes.Length; i++)
            {
                if (string.Equals(map.Nodes[i].LogicalId?.Trim(), trimmed, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

    }
}
