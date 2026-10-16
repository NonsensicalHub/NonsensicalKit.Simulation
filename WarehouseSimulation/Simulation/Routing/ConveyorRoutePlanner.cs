using System;
using System.Collections.Generic;
using NonsensicalKit.Simulation.WarehouseSimulation.Core.Graph;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation.Routing
{
    /// <summary>
    /// 静态可行路线规划（方案 A）。
    /// 搜索状态 = (NodeId, ProcessStageIndex, EntryPortId)；
    /// 成本仅含静态移动 / 工艺服务 / 设备动作，不读写正式资源预约。
    /// </summary>
    public static class ConveyorRoutePlanner
    {
        private readonly struct SearchState : IEquatable<SearchState>
        {
            public readonly string NodeId;
            public readonly int StageIndex;
            public readonly string EntryPortId;

            public SearchState(string nodeId, int stageIndex, string entryPortId)
            {
                NodeId = nodeId ?? string.Empty;
                StageIndex = stageIndex;
                EntryPortId = entryPortId ?? ConveyorPortIds.Any;
            }

            public bool Equals(SearchState other) =>
                StageIndex == other.StageIndex
                && string.Equals(NodeId, other.NodeId, StringComparison.Ordinal)
                && string.Equals(EntryPortId, other.EntryPortId, StringComparison.Ordinal);

            public override bool Equals(object obj) => obj is SearchState other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = NodeId.GetHashCode();
                    hash = (hash * 397) ^ StageIndex;
                    hash = (hash * 397) ^ EntryPortId.GetHashCode();
                    return hash;
                }
            }
        }

        private sealed class CameFrom
        {
            public SearchState Prev;
            public RouteStep Step;
            public bool HasPrev;
        }

        /// <summary>
        /// 规划满足工艺顺序的静态路线。成功输出带 OccurrenceIndex 的步骤序列。
        /// </summary>
        public static RoutePlanResult TryPlan(
            ConveyorGraph graph,
            string startNodeId,
            string goalNodeId,
            IReadOnlyList<ProcessRequirement> requirements = null,
            float? defaultProcessServiceSeconds = null)
        {
            if (graph == null)
            {
                return RoutePlanResult.Fail(RoutePlanErrorCode.NullGraph, "ConveyorGraph 为空。");
            }

            var start = startNodeId?.Trim();
            var goal = goalNodeId?.Trim();
            if (string.IsNullOrEmpty(start) || string.IsNullOrEmpty(goal))
            {
                return RoutePlanResult.Fail(RoutePlanErrorCode.MissingStartOrGoal, "起点或终点为空。");
            }

            if (!graph.TryGetNode(start, out _))
            {
                return RoutePlanResult.Fail(RoutePlanErrorCode.StartNodeMissing, $"起点不存在：{start}");
            }

            if (!graph.TryGetNode(goal, out _))
            {
                return RoutePlanResult.Fail(RoutePlanErrorCode.GoalNodeMissing, $"终点不存在：{goal}");
            }

            var ordered = NormalizeRequirements(requirements);
            for (var i = 0; i < ordered.Count; i++)
            {
                var stations = graph.GetProcessStationsWithTag(ordered[i].Tag);
                if (stations.Count == 0)
                {
                    return RoutePlanResult.Fail(
                        RoutePlanErrorCode.NoProcessStationForRequirement,
                        $"工艺标签「{ordered[i].Tag}」没有可用加工站。");
                }
            }

            var defaultService = defaultProcessServiceSeconds ?? graph.DefaultProcessServiceSeconds;
            var startState = new SearchState(start, 0, ConveyorPortIds.Source);
            var dist = new Dictionary<SearchState, float>();
            var parent = new Dictionary<SearchState, CameFrom>();
            var settled = new HashSet<SearchState>();
            var open = new List<SearchState> { startState };
            dist[startState] = 0f;
            parent[startState] = new CameFrom { HasPrev = false };

            SearchState? goalState = null;
            var goalCost = float.MaxValue;

            while (open.Count > 0)
            {
                var bestIdx = 0;
                var bestCost = dist[open[0]];
                for (var i = 1; i < open.Count; i++)
                {
                    var c = dist[open[i]];
                    if (c < bestCost - 1e-9f
                        || (Math.Abs(c - bestCost) <= 1e-9f && CompareState(open[i], open[bestIdx]) < 0))
                    {
                        bestCost = c;
                        bestIdx = i;
                    }
                }

                var u = open[bestIdx];
                open.RemoveAt(bestIdx);
                if (!settled.Add(u))
                {
                    continue;
                }

                if (u.StageIndex >= ordered.Count
                    && string.Equals(u.NodeId, goal, StringComparison.Ordinal))
                {
                    goalState = u;
                    goalCost = bestCost;
                    break;
                }

                if (!graph.TryGetNode(u.NodeId, out var node))
                {
                    continue;
                }

                // 若当前站仍是下一道必需工序，只展开 Process，避免换向/离开抢在加工前。
                if (ShouldProcessHere(node, ordered, u.StageIndex))
                {
                    ExpandProcess(graph, ordered, defaultService, u, bestCost, node, dist, parent, open, settled);
                }
                else
                {
                    ExpandTurnaround(graph, u, bestCost, node, dist, parent, open, settled);
                    ExpandMoves(graph, ordered, u, bestCost, node, dist, parent, open, settled);
                }
            }

            if (goalState == null)
            {
                if (ordered.Count > 0 && HasDeadEndNoReturnBlocking(graph, ordered))
                {
                    return RoutePlanResult.Fail(
                        RoutePlanErrorCode.NoLegalReturnRoute,
                        "存在死端加工站且无合法返程弧/回路/转台，规划失败。");
                }

                return RoutePlanResult.Fail(
                    RoutePlanErrorCode.Unreachable,
                    $"无法从 {start} 完成工艺后到达 {goal}。");
            }

            var steps = Reconstruct(parent, goalState.Value);
            AssignOccurrenceIndices(steps);
            var plan = new RoutePlan
            {
                StartNodeId = start,
                GoalNodeId = goal,
                Steps = steps,
                TotalNominalCostSeconds = goalCost,
                Requirements = ordered,
            };
            return RoutePlanResult.Ok(plan);
        }


        private static bool ShouldProcessHere(
            ConveyorGraphNode node,
            IReadOnlyList<ProcessRequirement> ordered,
            int stageIndex)
        {
            if (node == null
                || node.Kind != ConveyorGraphNodeKind.ProcessStation
                || stageIndex >= ordered.Count)
            {
                return false;
            }

            return string.Equals(
                node.ProcessTag?.Trim(),
                ordered[stageIndex].Tag,
                StringComparison.OrdinalIgnoreCase);
        }

        private static void ExpandProcess(
            ConveyorGraph graph,
            IReadOnlyList<ProcessRequirement> ordered,
            float defaultService,
            SearchState u,
            float bestCost,
            ConveyorGraphNode node,
            Dictionary<SearchState, float> dist,
            Dictionary<SearchState, CameFrom> parent,
            List<SearchState> open,
            HashSet<SearchState> settled)
        {
            if (u.StageIndex >= ordered.Count)
            {
                return;
            }

            if (node.Kind != ConveyorGraphNodeKind.ProcessStation)
            {
                return;
            }

            var req = ordered[u.StageIndex];
            if (!string.Equals(node.ProcessTag?.Trim(), req.Tag, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var service = ResolveServiceSeconds(req, node, defaultService);
            var next = new SearchState(u.NodeId, u.StageIndex + 1, u.EntryPortId);
            var step = new RouteStep
            {
                Kind = RouteStepKind.Process,
                FromNodeId = u.NodeId,
                ToNodeId = u.NodeId,
                EntryPortId = u.EntryPortId,
                ExitPortId = u.EntryPortId,
                ToEntryPortId = u.EntryPortId,
                ProcessRequirementId = req.RequirementId,
                ResourceId = u.NodeId,
                NominalCostSeconds = service,
            };
            Relax(next, bestCost + service, u, step, dist, parent, open, settled);
        }

        private static void ExpandTurnaround(
            ConveyorGraph graph,
            SearchState u,
            float bestCost,
            ConveyorGraphNode node,
            Dictionary<SearchState, float> dist,
            Dictionary<SearchState, CameFrom> parent,
            List<SearchState> open,
            HashSet<SearchState> settled)
        {
            if (node.ReturnPolicy != ConveyorReturnPolicy.ReverseSameLane)
            {
                return;
            }

            // 进站端口 → 反向弧出端口；生成独立 Turnaround 步骤（含 ReverseSetupSeconds）。
            foreach (var incoming in graph.GetIncomingArcs(u.NodeId))
            {
                var arrivalPort = incoming.ToPortId;
                var entryMatches =
                    string.Equals(u.EntryPortId, arrivalPort, StringComparison.Ordinal)
                    || string.Equals(u.EntryPortId, ConveyorPortIds.Any, StringComparison.Ordinal)
                    || string.Equals(u.EntryPortId, ConveyorPortIds.Source, StringComparison.Ordinal);
                if (!entryMatches)
                {
                    continue;
                }

                if (!graph.TryGetReverseArc(incoming, out var reverse))
                {
                    continue;
                }

                var setup = node.ReverseSetupSeconds;
                if (graph.TryGetSegment(incoming.PhysicalSegmentId, out var seg))
                {
                    setup = Math.Max(setup, seg.ReverseSetupSeconds);
                }

                var exitPort = reverse.FromPortId;
                // 换向后入口状态设为反向弧的 FromPort，供后续 Move 使用。
                var next = new SearchState(u.NodeId, u.StageIndex, exitPort);
                if (next.Equals(u))
                {
                    continue;
                }

                var step = new RouteStep
                {
                    Kind = RouteStepKind.Turnaround,
                    FromNodeId = u.NodeId,
                    ToNodeId = u.NodeId,
                    ArcId = reverse.ArcId,
                    EntryPortId = u.EntryPortId,
                    ExitPortId = exitPort,
                    ToEntryPortId = exitPort,
                    PhysicalSegmentId = incoming.PhysicalSegmentId,
                    ResourceId = incoming.PhysicalSegmentId,
                    NominalCostSeconds = Math.Max(0f, setup),
                };
                Relax(next, bestCost + Math.Max(0f, setup), u, step, dist, parent, open, settled);
            }
        }

        private static void ExpandMoves(
            ConveyorGraph graph,
            IReadOnlyList<ProcessRequirement> ordered,
            SearchState u,
            float bestCost,
            ConveyorGraphNode node,
            Dictionary<SearchState, float> dist,
            Dictionary<SearchState, CameFrom> parent,
            List<SearchState> open,
            HashSet<SearchState> settled)
        {
            var outgoing = graph.GetOutgoingArcs(u.NodeId);
            for (var i = 0; i < outgoing.Count; i++)
            {
                var arc = outgoing[i];
                if (!TagsAllowed(arc.RequiredTags, ordered, u.StageIndex))
                {
                    continue;
                }

                if (!graph.TryGetNode(arc.ToNodeId, out var toNode))
                {
                    continue;
                }

                if (!CanEnterNode(toNode, ordered, u.StageIndex))
                {
                    continue;
                }

                if (!graph.CanTransfer(node, u.EntryPortId, arc.FromPortId))
                {
                    continue;
                }

                // ReverseSameLane + 保留端口：
                // - entry:* 表示刚进站，必须先 Turnaround，不能直接走反向出弧；
                // - exit:* 表示已换向，只允许匹配的出弧。
                if (node.ReturnPolicy == ConveyorReturnPolicy.ReverseSameLane && !node.CollapsePortsToAny)
                {
                    if (u.EntryPortId.StartsWith("entry:", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (u.EntryPortId.StartsWith("exit:", StringComparison.Ordinal)
                        && !string.Equals(u.EntryPortId, arc.FromPortId, StringComparison.Ordinal))
                    {
                        continue;
                    }
                }

                var turn = graph.GetTransferSeconds(node, u.EntryPortId, arc.FromPortId);
                var move = arc.TransitSeconds;
                var cost = turn + move;
                var toEntry = graph.NormalizeEntryPort(toNode, arc.ToPortId);
                var next = new SearchState(arc.ToNodeId, u.StageIndex, toEntry);
                var kind = toNode.Kind == ConveyorGraphNodeKind.ProcessStation
                           && u.StageIndex < ordered.Count
                           && string.Equals(toNode.ProcessTag?.Trim(), ordered[u.StageIndex].Tag, StringComparison.OrdinalIgnoreCase)
                    ? RouteStepKind.Move
                    : (toNode.Kind == ConveyorGraphNodeKind.ProcessStation && !IsMatchingNext(toNode, ordered, u.StageIndex)
                        ? RouteStepKind.PassThrough
                        : RouteStepKind.Move);

                var step = new RouteStep
                {
                    Kind = kind,
                    FromNodeId = arc.FromNodeId,
                    ToNodeId = arc.ToNodeId,
                    ArcId = arc.ArcId,
                    EntryPortId = u.EntryPortId,
                    ExitPortId = arc.FromPortId,
                    ToEntryPortId = toEntry,
                    PhysicalSegmentId = arc.PhysicalSegmentId,
                    NominalCostSeconds = cost,
                };
                Relax(next, bestCost + cost, u, step, dist, parent, open, settled);
            }
        }

        private static bool IsMatchingNext(
            ConveyorGraphNode toNode,
            IReadOnlyList<ProcessRequirement> ordered,
            int stageIndex)
        {
            if (stageIndex >= ordered.Count)
            {
                return false;
            }

            return string.Equals(
                toNode.ProcessTag?.Trim(),
                ordered[stageIndex].Tag,
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool CanEnterNode(
            ConveyorGraphNode toNode,
            IReadOnlyList<ProcessRequirement> ordered,
            int stageIndex)
        {
            if (toNode.Kind != ConveyorGraphNodeKind.ProcessStation)
            {
                return true;
            }

            if (IsMatchingNext(toNode, ordered, stageIndex))
            {
                return true;
            }

            return toNode.PassThroughAllowed;
        }

        private static bool TagsAllowed(
            string[] requiredTags,
            IReadOnlyList<ProcessRequirement> ordered,
            int stageIndex)
        {
            if (requiredTags == null || requiredTags.Length == 0)
            {
                return true;
            }

            for (var i = 0; i < requiredTags.Length; i++)
            {
                var tag = requiredTags[i]?.Trim();
                if (string.IsNullOrEmpty(tag))
                {
                    continue;
                }

                var allowed = false;
                for (var s = 0; s < ordered.Count; s++)
                {
                    if (string.Equals(ordered[s].Tag, tag, StringComparison.OrdinalIgnoreCase))
                    {
                        allowed = true;
                        break;
                    }
                }

                if (!allowed)
                {
                    return false;
                }
            }

            return true;
        }

        private static void Relax(
            SearchState next,
            float newCost,
            SearchState prev,
            RouteStep step,
            Dictionary<SearchState, float> dist,
            Dictionary<SearchState, CameFrom> parent,
            List<SearchState> open,
            HashSet<SearchState> settled)
        {
            if (settled.Contains(next))
            {
                return;
            }

            if (dist.TryGetValue(next, out var old) && newCost >= old - 1e-9f)
            {
                if (Math.Abs(newCost - old) <= 1e-9f)
                {
                    // 同成本：稳定父节点（字典序更小的 prev 优先）。
                    if (parent.TryGetValue(next, out var existing)
                        && existing.HasPrev
                        && CompareState(prev, existing.Prev) >= 0)
                    {
                        return;
                    }
                }
                else
                {
                    return;
                }
            }

            dist[next] = newCost;
            parent[next] = new CameFrom { HasPrev = true, Prev = prev, Step = step };
            if (!open.Contains(next))
            {
                open.Add(next);
            }
        }

        private static List<RouteStep> Reconstruct(
            Dictionary<SearchState, CameFrom> parent,
            SearchState goal)
        {
            var stack = new Stack<RouteStep>();
            var cur = goal;
            while (parent.TryGetValue(cur, out var cf) && cf.HasPrev)
            {
                stack.Push(cf.Step);
                cur = cf.Prev;
            }

            return new List<RouteStep>(stack);
        }

        private static void AssignOccurrenceIndices(List<RouteStep> steps)
        {
            for (var i = 0; i < steps.Count; i++)
            {
                steps[i].OccurrenceIndex = i;
            }
        }

        private static List<ProcessRequirement> NormalizeRequirements(
            IReadOnlyList<ProcessRequirement> requirements)
        {
            var list = new List<ProcessRequirement>();
            if (requirements == null)
            {
                return list;
            }

            for (var i = 0; i < requirements.Count; i++)
            {
                var r = requirements[i];
                if (r == null || string.IsNullOrWhiteSpace(r.Tag))
                {
                    continue;
                }

                list.Add(new ProcessRequirement
                {
                    RequirementId = string.IsNullOrWhiteSpace(r.RequirementId)
                        ? $"req:{r.Tag.Trim()}:{i}"
                        : r.RequirementId.Trim(),
                    Tag = r.Tag.Trim(),
                    Sequence = r.Sequence,
                    ServiceSeconds = r.ServiceSeconds,
                });
            }

            list.Sort((a, b) =>
            {
                var c = a.Sequence.CompareTo(b.Sequence);
                return c != 0 ? c : string.CompareOrdinal(a.RequirementId, b.RequirementId);
            });
            return list;
        }

        private static float ResolveServiceSeconds(
            ProcessRequirement req,
            ConveyorGraphNode node,
            float defaultService)
        {
            if (req.ServiceSeconds > 0f)
            {
                return req.ServiceSeconds;
            }

            if (node.ProcessServiceSeconds > 0f)
            {
                return node.ProcessServiceSeconds;
            }

            return Math.Max(0f, defaultService);
        }

        private static bool HasDeadEndNoReturnBlocking(
            ConveyorGraph graph,
            IReadOnlyList<ProcessRequirement> ordered)
        {
            for (var i = 0; i < ordered.Count; i++)
            {
                var stations = graph.GetProcessStationsWithTag(ordered[i].Tag);
                var anyReturnable = false;
                for (var s = 0; s < stations.Count; s++)
                {
                    var st = stations[s];
                    if (st.ReturnPolicy != ConveyorReturnPolicy.NoReturn)
                    {
                        anyReturnable = true;
                        break;
                    }

                    // LoopBack / 多出弧也可能可走，NoReturn 且出度为 0 才是死路。
                    if (graph.GetOutgoingArcs(st.NodeId).Count > 0)
                    {
                        anyReturnable = true;
                        break;
                    }
                }

                if (!anyReturnable && stations.Count > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static int CompareState(SearchState a, SearchState b)
        {
            var c = string.CompareOrdinal(a.NodeId, b.NodeId);
            if (c != 0)
            {
                return c;
            }

            c = a.StageIndex.CompareTo(b.StageIndex);
            if (c != 0)
            {
                return c;
            }

            return string.CompareOrdinal(a.EntryPortId, b.EntryPortId);
        }
    }
}
