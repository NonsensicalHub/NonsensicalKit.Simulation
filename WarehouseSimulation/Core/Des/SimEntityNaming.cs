using System;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Simulation;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Core
{
    /// <summary>仿真实体 ID 的统一命名：堆垛机 S1/S2、路口 C1/C2、取货点 P1/P2、路段 C1-P1。</summary>
    public static class SimEntityNaming
    {
        /// <summary>堆垛机显示/资源 ID（内部下标 0 起，显示 1 起）。</summary>
        public static string FormatStacker(int stackerIndex) =>
            stackerIndex >= 0 ? $"S{stackerIndex + 1}" : "—";

        public static string StackerResourceId(int stackerIndex) => FormatStacker(stackerIndex);

        /// <summary>编辑器新建节点时的 GUID。</summary>
        public static string NewNodeGuid() => Guid.NewGuid().ToString("D");

        /// <summary>编辑器新建节点时的默认逻辑 ID。</summary>
        public static string NewLogicalId(SimConveyorNodeKind kind, int existingCountOfKind)
        {
            var n = existingCountOfKind + 1;
            return kind switch
            {
                SimConveyorNodeKind.InfeedPort => $"IN{n}",
                SimConveyorNodeKind.PickupPoint => $"P{n}",
                SimConveyorNodeKind.OutfeedPort => $"OUT{n}",
                SimConveyorNodeKind.ProcessStation => $"PS{n}",
                SimConveyorNodeKind.VerticalTransfer => $"VT{n}",
                _ => $"C{n}",
            };
        }

        /// <summary>节点可读标识（地图显示、日志、场景锚点）。</summary>
        public static string FormatLogicalId(in SimConveyorMapNode node, int nodeIndex = -1)
        {
            var logical = node.LogicalId?.Trim();
            if (!string.IsNullOrEmpty(logical))
            {
                return logical;
            }

            return nodeIndex >= 0 ? $"n{nodeIndex}" : "—";
        }

        public static string FormatLogicalId(WarehouseConveyorMap map, int nodeIndex) =>
            map?.Nodes != null && nodeIndex >= 0 && nodeIndex < map.Nodes.Length
                ? FormatLogicalId(map.Nodes[nodeIndex], nodeIndex)
                : nodeIndex >= 0 ? $"n{nodeIndex}" : "—";

        public static string FormatLogicalId(WarehouseConveyorMap map, string nodeId)
        {
            if (map?.Nodes == null || string.IsNullOrWhiteSpace(nodeId))
            {
                return "—";
            }

            var trimmed = nodeId.Trim();
            for (var i = 0; i < map.Nodes.Length; i++)
            {
                if (map.Nodes[i].NodeId?.Trim() == trimmed)
                {
                    return FormatLogicalId(map.Nodes[i], i);
                }
            }

            return trimmed;
        }

        /// <summary>无向路段键（字典序 + 连字符，如 C1-P1）。</summary>
        public static string CanonicalSegmentKey(string nodeIdA, string nodeIdB)
        {
            var a = nodeIdA?.Trim() ?? string.Empty;
            var b = nodeIdB?.Trim() ?? string.Empty;
            return string.CompareOrdinal(a, b) < 0 ? $"{a}-{b}" : $"{b}-{a}";
        }

        public static string FormatSegmentLabel(string nodeIdA, string nodeIdB) =>
            CanonicalSegmentKey(nodeIdA, nodeIdB);

        public static string FormatSegmentLabel(WarehouseConveyorMap map, string nodeIdA, string nodeIdB) =>
            CanonicalSegmentKey(FormatLogicalId(map, nodeIdA), FormatLogicalId(map, nodeIdB));

        public static string FormatNode(WarehouseConveyorMap map, int nodeIndex) =>
            FormatLogicalId(map, nodeIndex);

        public static string FormatSegment(WarehouseConveyorMap map, int fromNodeIndex, int toNodeIndex) =>
            CanonicalSegmentKey(FormatNode(map, fromNodeIndex), FormatNode(map, toNodeIndex));

        public static string FormatInfeedPort(WarehouseConveyorMap map, ConveyorMapTopology topology, int infeedPortIndex)
        {
            if (topology?.InfeedNodeIndices != null
                && infeedPortIndex >= 0
                && infeedPortIndex < topology.InfeedNodeIndices.Count)
            {
                return FormatNode(map, topology.InfeedNodeIndices[infeedPortIndex]);
            }

            return infeedPortIndex >= 0 ? $"IN{infeedPortIndex + 1}" : "—";
        }

        public static string FormatPickupPoint(WarehouseConveyorMap map, int pickupNodeIndex) =>
            FormatNode(map, pickupNodeIndex);

        public static string FormatOutfeedPort(WarehouseConveyorMap map, ConveyorMapTopology topology, int outfeedPortIndex)
        {
            if (topology?.OutfeedNodeIndices != null
                && outfeedPortIndex >= 0
                && outfeedPortIndex < topology.OutfeedNodeIndices.Count)
            {
                return FormatNode(map, topology.OutfeedNodeIndices[outfeedPortIndex]);
            }

            return outfeedPortIndex >= 0 ? $"OUT{outfeedPortIndex + 1}" : "—";
        }

        public static string PickupResourceId(in SimConveyorMapNode pickupNode, int pickupNodeIndex)
        {
            var id = FormatLogicalId(in pickupNode, pickupNodeIndex);
            return id != "—" ? id : $"P{pickupNodeIndex + 1}";
        }

        public static string OutfeedResourceId(in SimConveyorMapNode outfeedNode, int outfeedNodeIndex)
        {
            var id = FormatLogicalId(in outfeedNode, outfeedNodeIndex);
            return id != "—" ? id : $"OUT{outfeedNodeIndex + 1}";
        }

        /// <summary>出库口发运服务串行资源（与 <see cref="StackerWarehouseSimulator"/> 出库服务排队共用）。</summary>
        public static string OutfeedServiceResourceId(in SimConveyorMapNode outfeedNode, int outfeedPortIndex)
        {
            var portLabel = FormatLogicalId(in outfeedNode, outfeedPortIndex);
            if (portLabel == "—")
            {
                portLabel = $"outfeed-{outfeedPortIndex}";
            }

            return $"{portLabel}-outfeed";
        }

        public static string ProcessStationZoneResourceId(in SimConveyorMapNode node, int nodeIndex)
        {
            var id = FormatLogicalId(in node, nodeIndex);
            return id != "—" ? id : $"PS{nodeIndex + 1}";
        }

        /// <summary>加工站点串行服务资源（同一设备一次只加工一箱）。</summary>
        public static string ProcessStationServiceResourceId(in SimConveyorMapNode node, int nodeIndex)
        {
            var zoneId = ProcessStationZoneResourceId(in node, nodeIndex);
            return $"{zoneId}-process";
        }

        public static string VerticalTransferZoneResourceId(in SimConveyorMapNode node, int nodeIndex)
        {
            var id = FormatLogicalId(in node, nodeIndex);
            return id != "—" ? id : $"VT{nodeIndex + 1}";
        }

        /// <summary>垂直提升机设备互斥资源（同一 TransferGroupId 共用）。</summary>
        public static string VerticalTransferServiceResourceId(in SimConveyorMapNode node, int nodeIndex)
        {
            var groupId = ConveyorVerticalTransferUtility.ResolveTransferGroupId(in node, nodeIndex);
            return $"{groupId}-vtransfer";
        }

        public static string JunctionFallbackId(int junctionNodeIndex) =>
            junctionNodeIndex >= 0 ? $"C{junctionNodeIndex + 1}" : "—";

        /// <summary>路口 ZPA zone 资源 ID（基于节点逻辑 ID；资源键前缀 stop- 保持与地图配置兼容）。</summary>
        public static string JunctionZoneResourceId(string nodeOrResourceId) =>
            $"stop-{nodeOrResourceId?.Trim() ?? "unknown"}";

        public static string JunctionZoneResourceId(int junctionNodeIndex) =>
            junctionNodeIndex >= 0 ? $"stop-C{junctionNodeIndex + 1}" : "stop-unknown";

        public static string FormatPathSummary(WarehouseConveyorMap map, int[] path)
        {
            if (path == null || path.Length == 0)
            {
                return string.Empty;
            }

            if (path.Length <= 12)
            {
                var sb = new System.Text.StringBuilder(path.Length * 6);
                for (var i = 0; i < path.Length; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(" → ");
                    }

                    sb.Append(FormatNode(map, path[i]));
                }

                return sb.ToString();
            }

            return
                $"{FormatNode(map, path[0])} → {FormatNode(map, path[1])} → … → " +
                $"{FormatNode(map, path[^2])} → {FormatNode(map, path[^1])} ({path.Length} 节点)";
        }
    }
}
