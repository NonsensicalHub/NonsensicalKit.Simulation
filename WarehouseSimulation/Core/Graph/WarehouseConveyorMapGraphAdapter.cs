using System;
using System.Collections.Generic;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Core.Graph
{
    /// <summary>
    /// 从旧资产 <see cref="WarehouseConveyorMap"/> 派生标准化图。
    /// 缺失字段使用显式默认；绝不静默生成反向弧。
    /// </summary>
    public static class WarehouseConveyorMapGraphAdapter
    {
        public sealed class Options
        {
            /// <summary>加工站默认服务时间（地图节点 ≤0 时使用）。</summary>
            public float DefaultProcessServiceSeconds = 30f;

            /// <summary>
            /// 死端加工站：若存在对向弧则 ReverseSameLane，否则 NoReturn（不静默造弧）。
            /// </summary>
            public bool InferDeadEndReturnFromExistingReverseArcs = true;

            /// <summary>旧 Dwell 加工站默认不允许无关任务直通（对齐旧 CanTraverse）。</summary>
            public bool ProcessStationPassThroughAllowed = false;
        }

        public static ConveyorGraphBuildResult BuildAndValidate(
            WarehouseConveyorMap map,
            Options options = null)
        {
            options ??= new Options();
            if (map == null || map.Nodes == null || map.Nodes.Length == 0)
            {
                var empty = new ConveyorGraphBuildResult { Success = false };
                empty.AddError(ConveyorGraphErrorCode.NullOrEmptyMap, "WarehouseConveyorMap 为空或未配置节点。");
                return empty;
            }

            var request = AdaptToRequest(map, options);
            return ConveyorGraphBuilder.BuildAndValidate(request);
        }

        public static ConveyorGraphBuildRequest AdaptToRequest(
            WarehouseConveyorMap map,
            Options options = null)
        {
            options ??= new Options();
            var request = new ConveyorGraphBuildRequest
            {
                DefaultProcessServiceSeconds = options.DefaultProcessServiceSeconds,
                DefaultSpeedMetersPerSecond = map.DefaultSpeedMetersPerSecond > 0f
                    ? map.DefaultSpeedMetersPerSecond
                    : 0.6f,
                ValidateProcessStationReturns = true,
            };

            var idToIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < map.Nodes.Length; i++)
            {
                var id = map.Nodes[i].NodeId?.Trim();
                if (!string.IsNullOrEmpty(id))
                {
                    idToIndex[id] = i;
                }
            }

            var undirectedNeighbors = BuildUndirectedNeighbors(map, idToIndex);
            var edgePairKeys = BuildEdgePairKeys(map);

            for (var i = 0; i < map.Nodes.Length; i++)
            {
                ref var src = ref map.Nodes[i];
                var nodeId = src.NodeId?.Trim();
                var node = new ConveyorGraphNode
                {
                    NodeId = nodeId,
                    LogicalId = src.LogicalId?.Trim(),
                    Kind = MapKind(src.Kind),
                    ProcessTag = src.ProcessTag?.Trim(),
                    ProcessServiceSeconds = src.ProcessServiceSeconds > 0f
                        ? src.ProcessServiceSeconds
                        : 0f,
                    PassThroughAllowed = options.ProcessStationPassThroughAllowed,
                    PassThroughPolicy = StationPassThroughPolicy.None,
                    TransferSeconds = Math.Max(0f, src.TransferSeconds),
                    CollapsePortsToAny = true,
                    ReturnPolicy = ConveyorReturnPolicy.LoopBack,
                };

                if (node.Kind == ConveyorGraphNodeKind.ProcessStation)
                {
                    var degree = 0;
                    if (!string.IsNullOrEmpty(nodeId)
                        && undirectedNeighbors.TryGetValue(nodeId, out var neighbors))
                    {
                        degree = neighbors.Count;
                    }

                    if (degree <= 1)
                    {
                        var hasReverse = HasAnyIncomingWithReverse(map, nodeId, edgePairKeys);
                        if (options.InferDeadEndReturnFromExistingReverseArcs && hasReverse)
                        {
                            node.ReturnPolicy = ConveyorReturnPolicy.ReverseSameLane;
                            // 保留入端口以识别进站弧，支撑 Turnaround → 反向弧。
                            node.CollapsePortsToAny = false;
                        }
                        else
                        {
                            // 显式默认：无反向弧则 NoReturn，不静默生成反向能力。
                            node.ReturnPolicy = ConveyorReturnPolicy.NoReturn;
                        }
                    }
                    else
                    {
                        node.ReturnPolicy = ConveyorReturnPolicy.LoopBack;
                    }
                }

                request.Nodes.Add(node);
            }

            var segments = new Dictionary<string, PhysicalSegment>(StringComparer.Ordinal);
            if (map.Edges != null)
            {
                for (var e = 0; e < map.Edges.Length; e++)
                {
                    var edge = map.Edges[e];
                    var fromId = edge.FromNodeId?.Trim();
                    var toId = edge.ToNodeId?.Trim();
                    if (string.IsNullOrEmpty(fromId) || string.IsNullOrEmpty(toId))
                    {
                        continue;
                    }

                    var segmentId = CanonicalSegmentId(fromId, toId);
                    var hasOpposite = edgePairKeys.Contains(EndpointKey(toId, fromId));
                    if (!segments.ContainsKey(segmentId))
                    {
                        var flow = hasOpposite
                            ? ConveyorFlowMode.SharedReversibleLane
                            : ConveyorFlowMode.FixedOneWay;
                        segments[segmentId] = new PhysicalSegment
                        {
                            PhysicalSegmentId = segmentId,
                            LengthMeters = Math.Max(0f, edge.DistanceMeters),
                            FlowMode = flow,
                            ReverseSetupSeconds = 0f,
                            SafetyGapMeters = 0f,
                        };
                    }
                    else if (edge.DistanceMeters > segments[segmentId].LengthMeters)
                    {
                        segments[segmentId].LengthMeters = edge.DistanceMeters;
                    }

                    var speed = edge.SpeedOverrideMetersPerSecond > 0f
                        ? edge.SpeedOverrideMetersPerSecond
                        : request.DefaultSpeedMetersPerSecond;
                    var arcId = $"arc:{fromId}>{toId}";
                    var requiredTags = string.IsNullOrWhiteSpace(edge.ProcessTag)
                        ? Array.Empty<string>()
                        : new[] { edge.ProcessTag.Trim() };

                    // 端口绑定到有向弧端点，便于死端站保留进站方向。
                    var fromPort = $"exit:{fromId}>{toId}";
                    var toPort = $"entry:{fromId}>{toId}";
                    request.Arcs.Add(new ConveyorArc
                    {
                        ArcId = arcId,
                        PhysicalSegmentId = segmentId,
                        FromNodeId = fromId,
                        ToNodeId = toId,
                        FromPortId = fromPort,
                        ToPortId = toPort,
                        LengthMeters = Math.Max(0f, edge.DistanceMeters),
                        SpeedMetersPerSecond = speed,
                        RequiredTags = requiredTags,
                    });
                }
            }

            foreach (var seg in segments.Values)
            {
                request.PhysicalSegments.Add(seg);
            }

            return request;
        }

        private static ConveyorGraphNodeKind MapKind(SimConveyorNodeKind kind) =>
            kind switch
            {
                SimConveyorNodeKind.InfeedPort => ConveyorGraphNodeKind.InfeedPort,
                SimConveyorNodeKind.OutfeedPort => ConveyorGraphNodeKind.OutfeedPort,
                SimConveyorNodeKind.PickupPoint => ConveyorGraphNodeKind.PickupPoint,
                SimConveyorNodeKind.ProcessStation => ConveyorGraphNodeKind.ProcessStation,
                SimConveyorNodeKind.VerticalTransfer => ConveyorGraphNodeKind.VerticalTransfer,
                _ => ConveyorGraphNodeKind.Junction,
            };

        private static Dictionary<string, HashSet<string>> BuildUndirectedNeighbors(
            WarehouseConveyorMap map,
            Dictionary<string, int> idToIndex)
        {
            var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var id in idToIndex.Keys)
            {
                result[id] = new HashSet<string>(StringComparer.Ordinal);
            }

            if (map.Edges == null)
            {
                return result;
            }

            for (var i = 0; i < map.Edges.Length; i++)
            {
                var from = map.Edges[i].FromNodeId?.Trim();
                var to = map.Edges[i].ToNodeId?.Trim();
                if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
                {
                    continue;
                }

                if (!result.ContainsKey(from))
                {
                    result[from] = new HashSet<string>(StringComparer.Ordinal);
                }

                if (!result.ContainsKey(to))
                {
                    result[to] = new HashSet<string>(StringComparer.Ordinal);
                }

                result[from].Add(to);
                result[to].Add(from);
            }

            return result;
        }

        private static HashSet<string> BuildEdgePairKeys(WarehouseConveyorMap map)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            if (map.Edges == null)
            {
                return keys;
            }

            for (var i = 0; i < map.Edges.Length; i++)
            {
                var from = map.Edges[i].FromNodeId?.Trim();
                var to = map.Edges[i].ToNodeId?.Trim();
                if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
                {
                    continue;
                }

                keys.Add(EndpointKey(from, to));
            }

            return keys;
        }

        private static bool HasAnyIncomingWithReverse(
            WarehouseConveyorMap map,
            string stationId,
            HashSet<string> edgePairKeys)
        {
            if (map.Edges == null || string.IsNullOrEmpty(stationId))
            {
                return false;
            }

            for (var i = 0; i < map.Edges.Length; i++)
            {
                var to = map.Edges[i].ToNodeId?.Trim();
                var from = map.Edges[i].FromNodeId?.Trim();
                if (!string.Equals(to, stationId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (edgePairKeys.Contains(EndpointKey(stationId, from)))
                {
                    return true;
                }
            }

            return false;
        }

        public static string CanonicalSegmentId(string nodeIdA, string nodeIdB)
        {
            var a = nodeIdA ?? string.Empty;
            var b = nodeIdB ?? string.Empty;
            return string.CompareOrdinal(a, b) <= 0 ? $"seg:{a}|{b}" : $"seg:{b}|{a}";
        }

        private static string EndpointKey(string from, string to) => $"{from}\u001f{to}";
    }
}
