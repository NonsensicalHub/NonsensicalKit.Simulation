using System;
using System.Collections.Generic;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Core.Graph
{
    /// <summary>
    /// 标准化有向图 + PhysicalSegment 构建与校验。
    /// 错误以 <see cref="ConveyorGraphErrorCode"/> 明确返回；缺反向/无退路等在规划前失败。
    /// </summary>
    public static class ConveyorGraphBuilder
    {
        public static ConveyorGraphBuildResult BuildAndValidate(ConveyorGraphBuildRequest request)
        {
            var result = new ConveyorGraphBuildResult { Success = true };
            if (request == null || request.Nodes == null || request.Nodes.Count == 0)
            {
                result.AddError(ConveyorGraphErrorCode.NullOrEmptyMap, "构建请求为空或未配置节点。");
                return result;
            }

            var nodes = new Dictionary<string, ConveyorGraphNode>(StringComparer.Ordinal);
            var logicalIds = new HashSet<string>(StringComparer.Ordinal);
            var infeeds = new List<string>();
            var outfeeds = new List<string>();
            var pickups = new List<string>();
            var processStations = new List<string>();

            for (var i = 0; i < request.Nodes.Count; i++)
            {
                var node = request.Nodes[i];
                if (node == null)
                {
                    result.AddError(ConveyorGraphErrorCode.MissingNodeId, $"节点下标 {i} 为空。");
                    continue;
                }

                var nodeId = node.NodeId?.Trim();
                if (string.IsNullOrEmpty(nodeId))
                {
                    result.AddError(ConveyorGraphErrorCode.MissingNodeId, $"节点下标 {i} 缺少 NodeId。");
                    continue;
                }

                node.NodeId = nodeId;
                if (nodes.ContainsKey(nodeId))
                {
                    result.AddError(ConveyorGraphErrorCode.DuplicateNodeId, $"重复的 NodeId：{nodeId}", nodeId);
                    continue;
                }

                nodes[nodeId] = node;

                var logicalId = node.LogicalId?.Trim();
                if (string.IsNullOrEmpty(logicalId))
                {
                    result.AddError(ConveyorGraphErrorCode.MissingLogicalId, $"节点 {nodeId} 缺少 LogicalId。", nodeId);
                }
                else
                {
                    node.LogicalId = logicalId;
                    if (!logicalIds.Add(logicalId))
                    {
                        result.AddError(ConveyorGraphErrorCode.DuplicateLogicalId, $"重复的 LogicalId：{logicalId}", nodeId);
                    }
                }

                if (node.ProcessServiceSeconds < 0f || node.ReverseSetupSeconds < 0f || node.TransferSeconds < 0f)
                {
                    result.AddError(
                        ConveyorGraphErrorCode.NegativeServiceOrActionTime,
                        $"节点 {logicalId ?? nodeId} 存在负的服务/动作时间。",
                        nodeId);
                }

                if (node.Kind == ConveyorGraphNodeKind.ProcessStation
                    && string.IsNullOrWhiteSpace(node.ProcessTag))
                {
                    result.AddError(
                        ConveyorGraphErrorCode.ProcessTagMissingOnDwellStation,
                        $"加工站 {logicalId ?? nodeId} 缺少 ProcessTag。",
                        nodeId);
                }

                switch (node.Kind)
                {
                    case ConveyorGraphNodeKind.InfeedPort:
                        infeeds.Add(nodeId);
                        break;
                    case ConveyorGraphNodeKind.OutfeedPort:
                        outfeeds.Add(nodeId);
                        break;
                    case ConveyorGraphNodeKind.PickupPoint:
                        pickups.Add(nodeId);
                        break;
                    case ConveyorGraphNodeKind.ProcessStation:
                        processStations.Add(nodeId);
                        break;
                }
            }

            var segments = new Dictionary<string, PhysicalSegment>(StringComparer.Ordinal);
            if (request.PhysicalSegments != null)
            {
                for (var i = 0; i < request.PhysicalSegments.Count; i++)
                {
                    var seg = request.PhysicalSegments[i];
                    if (seg == null)
                    {
                        continue;
                    }

                    var segId = seg.PhysicalSegmentId?.Trim();
                    if (string.IsNullOrEmpty(segId))
                    {
                        result.AddError(
                            ConveyorGraphErrorCode.MissingPhysicalSegmentId,
                            $"物理段下标 {i} 缺少 PhysicalSegmentId。");
                        continue;
                    }

                    seg.PhysicalSegmentId = segId;
                    if (seg.LengthMeters < 0f || float.IsNaN(seg.LengthMeters) || float.IsInfinity(seg.LengthMeters))
                    {
                        result.AddError(
                            ConveyorGraphErrorCode.InvalidDistance,
                            $"物理段 {segId} 距离非法。",
                            physicalSegmentId: segId);
                    }

                    if (seg.ReverseSetupSeconds < 0f)
                    {
                        result.AddError(
                            ConveyorGraphErrorCode.NegativeServiceOrActionTime,
                            $"物理段 {segId} ReverseSetupSeconds 为负。",
                            physicalSegmentId: segId);
                    }

                    if (segments.ContainsKey(segId))
                    {
                        result.AddError(
                            ConveyorGraphErrorCode.DuplicatePhysicalSegmentId,
                            $"重复的 PhysicalSegmentId：{segId}",
                            physicalSegmentId: segId);
                    }
                    else
                    {
                        segments[segId] = seg;
                    }
                }
            }

            var arcs = new Dictionary<string, ConveyorArc>(StringComparer.Ordinal);
            if (request.Arcs != null)
            {
                for (var i = 0; i < request.Arcs.Count; i++)
                {
                    var arc = request.Arcs[i];
                    if (arc == null)
                    {
                        continue;
                    }

                    var arcId = arc.ArcId?.Trim();
                    if (string.IsNullOrEmpty(arcId))
                    {
                        result.AddError(ConveyorGraphErrorCode.MissingArcId, $"弧下标 {i} 缺少 ArcId。");
                        continue;
                    }

                    arc.ArcId = arcId;
                    if (arcs.ContainsKey(arcId))
                    {
                        result.AddError(ConveyorGraphErrorCode.DuplicateArcId, $"重复的 ArcId：{arcId}", arcId: arcId);
                        continue;
                    }

                    arcs[arcId] = arc;

                    var fromId = arc.FromNodeId?.Trim();
                    var toId = arc.ToNodeId?.Trim();
                    arc.FromNodeId = fromId;
                    arc.ToNodeId = toId;
                    if (string.IsNullOrEmpty(fromId) || string.IsNullOrEmpty(toId)
                        || !nodes.ContainsKey(fromId) || !nodes.ContainsKey(toId))
                    {
                        result.AddError(
                            ConveyorGraphErrorCode.ArcEndpointMissing,
                            $"弧 {arcId} 端点缺失或不存在：{fromId} → {toId}",
                            arcId: arcId);
                    }

                    if (string.IsNullOrEmpty(arc.FromPortId) || string.IsNullOrEmpty(arc.ToPortId))
                    {
                        result.AddError(
                            ConveyorGraphErrorCode.InvalidPortId,
                            $"弧 {arcId} 缺少 FromPortId/ToPortId。",
                            arcId: arcId);
                    }

                    if (arc.LengthMeters < 0f || float.IsNaN(arc.LengthMeters) || float.IsInfinity(arc.LengthMeters))
                    {
                        result.AddError(
                            ConveyorGraphErrorCode.InvalidDistance,
                            $"弧 {arcId} 距离非法。",
                            arcId: arcId);
                    }

                    if (arc.SpeedMetersPerSecond < 0f || float.IsNaN(arc.SpeedMetersPerSecond))
                    {
                        result.AddError(
                            ConveyorGraphErrorCode.InvalidSpeed,
                            $"弧 {arcId} 速度非法。",
                            arcId: arcId);
                    }

                    var segId = arc.PhysicalSegmentId?.Trim();
                    if (string.IsNullOrEmpty(segId))
                    {
                        result.AddError(
                            ConveyorGraphErrorCode.MissingPhysicalSegmentId,
                            $"弧 {arcId} 缺少 PhysicalSegmentId。",
                            arcId: arcId);
                    }
                    else
                    {
                        arc.PhysicalSegmentId = segId;
                        if (!segments.ContainsKey(segId))
                        {
                            result.AddError(
                                ConveyorGraphErrorCode.PhysicalSegmentArcMismatch,
                                $"弧 {arcId} 引用未知物理段 {segId}。",
                                arcId: arcId,
                                physicalSegmentId: segId);
                        }
                    }

                    if (arc.RequiredTags == null)
                    {
                        arc.RequiredTags = Array.Empty<string>();
                    }
                }
            }

            if (!result.Success)
            {
                return result;
            }

            ValidateSegmentDirectionConsistency(segments, arcs, result);
            if (request.ValidateProcessStationReturns)
            {
                ValidateProcessStationReturns(nodes, arcs, processStations, result);
            }

            if (!result.Success)
            {
                return result;
            }

            var speed = request.DefaultSpeedMetersPerSecond > 0f
                ? request.DefaultSpeedMetersPerSecond
                : 0.6f;
            result.Graph = new ConveyorGraph(
                nodes,
                arcs,
                segments,
                infeeds,
                outfeeds,
                pickups,
                processStations,
                Math.Max(0f, request.DefaultProcessServiceSeconds),
                speed);
            result.Success = true;
            return result;
        }

        private static void ValidateSegmentDirectionConsistency(
            Dictionary<string, PhysicalSegment> segments,
            Dictionary<string, ConveyorArc> arcs,
            ConveyorGraphBuildResult result)
        {
            var arcsBySegment = new Dictionary<string, List<ConveyorArc>>(StringComparer.Ordinal);
            foreach (var arc in arcs.Values)
            {
                if (!arcsBySegment.TryGetValue(arc.PhysicalSegmentId, out var list))
                {
                    list = new List<ConveyorArc>();
                    arcsBySegment[arc.PhysicalSegmentId] = list;
                }

                list.Add(arc);
            }

            foreach (var kv in arcsBySegment)
            {
                if (!segments.TryGetValue(kv.Key, out var seg))
                {
                    continue;
                }

                var list = kv.Value;
                if (seg.FlowMode == ConveyorFlowMode.FixedOneWay && list.Count > 1)
                {
                    // 同一物理段上出现对向弧却标为 FixedOneWay：配置错误。
                    var hasOpposite = false;
                    for (var i = 0; i < list.Count && !hasOpposite; i++)
                    {
                        for (var j = i + 1; j < list.Count; j++)
                        {
                            if (list[i].FromNodeId == list[j].ToNodeId
                                && list[i].ToNodeId == list[j].FromNodeId)
                            {
                                hasOpposite = true;
                                break;
                            }
                        }
                    }

                    if (hasOpposite)
                    {
                        result.AddError(
                            ConveyorGraphErrorCode.FixedOneWayMustNotHaveImplicitReverse,
                            $"物理段 {kv.Key} 为 FixedOneWay，但存在对向弧。请改为 SharedReversibleLane/SeparateBidirectionalLanes，或删除反向弧。",
                            physicalSegmentId: kv.Key);
                    }
                }
            }
        }

        private static void ValidateProcessStationReturns(
            Dictionary<string, ConveyorGraphNode> nodes,
            Dictionary<string, ConveyorArc> arcs,
            List<string> processStations,
            ConveyorGraphBuildResult result)
        {
            var outCount = new Dictionary<string, int>(StringComparer.Ordinal);
            var inCount = new Dictionary<string, int>(StringComparer.Ordinal);
            var undirected = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var id in nodes.Keys)
            {
                outCount[id] = 0;
                inCount[id] = 0;
                undirected[id] = new HashSet<string>(StringComparer.Ordinal);
            }

            foreach (var arc in arcs.Values)
            {
                if (!outCount.ContainsKey(arc.FromNodeId) || !inCount.ContainsKey(arc.ToNodeId))
                {
                    continue;
                }

                outCount[arc.FromNodeId]++;
                inCount[arc.ToNodeId]++;
                undirected[arc.FromNodeId].Add(arc.ToNodeId);
                undirected[arc.ToNodeId].Add(arc.FromNodeId);
            }

            for (var i = 0; i < processStations.Count; i++)
            {
                var stationId = processStations[i];
                if (!nodes.TryGetValue(stationId, out var station))
                {
                    continue;
                }

                var degree = undirected[stationId].Count;
                if (degree > 1)
                {
                    // 多邻接：回路或分叉，不强制返程策略。
                    continue;
                }

                if (station.ReturnPolicy == ConveyorReturnPolicy.NoReturn)
                {
                    result.AddError(
                        ConveyorGraphErrorCode.DeadEndNoLegalReturn,
                        $"死端加工站 {station.LogicalId ?? stationId} 标记为 NoReturn，规划阶段将无法返回。",
                        stationId);
                    continue;
                }

                if (station.ReturnPolicy == ConveyorReturnPolicy.ReverseSameLane)
                {
                    var hasReversePair = false;
                    foreach (var arc in arcs.Values)
                    {
                        if (arc.ToNodeId != stationId)
                        {
                            continue;
                        }

                        foreach (var other in arcs.Values)
                        {
                            if (other.FromNodeId == stationId
                                && other.ToNodeId == arc.FromNodeId
                                && string.Equals(other.PhysicalSegmentId, arc.PhysicalSegmentId, StringComparison.Ordinal))
                            {
                                hasReversePair = true;
                                break;
                            }
                        }

                        if (hasReversePair)
                        {
                            break;
                        }
                    }

                    if (!hasReversePair)
                    {
                        result.AddError(
                            ConveyorGraphErrorCode.NoLegalReturnRoute,
                            $"死端加工站 {station.LogicalId ?? stationId} 配置 ReverseSameLane，但缺少共享物理段的反向弧（不会静默生成）。",
                            stationId);
                    }
                }
            }
        }
    }
}
