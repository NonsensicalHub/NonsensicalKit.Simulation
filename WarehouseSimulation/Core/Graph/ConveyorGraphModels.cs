using System;
using System.Collections.Generic;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Core.Graph
{
    /// <summary>保留端口常量。起点入端口为 Source；无转向限制时可规范到 Any。</summary>
    public static class ConveyorPortIds
    {
        public const string Source = "Source";
        public const string Any = "Any";
    }

    public sealed class PhysicalSegment
    {
        public string PhysicalSegmentId { get; set; }
        public float LengthMeters { get; set; }
        public ConveyorFlowMode FlowMode { get; set; }
        public float ReverseSetupSeconds { get; set; }
        public float SafetyGapMeters { get; set; }
    }

    public sealed class ConveyorArc
    {
        public string ArcId { get; set; }
        public string PhysicalSegmentId { get; set; }
        public string FromNodeId { get; set; }
        public string ToNodeId { get; set; }
        public string FromPortId { get; set; }
        public string ToPortId { get; set; }
        public float LengthMeters { get; set; }
        public float SpeedMetersPerSecond { get; set; }
        public string[] RequiredTags { get; set; }

        public float TransitSeconds
        {
            get
            {
                var speed = SpeedMetersPerSecond > 0.01f ? SpeedMetersPerSecond : 0.01f;
                return Math.Max(0f, LengthMeters) / speed;
            }
        }
    }

    public sealed class MovementRule
    {
        public string EntryPortId { get; set; }
        public string ExitPortId { get; set; }
        public float TurnSeconds { get; set; }
        public string ActionType { get; set; }
    }

    public sealed class ConveyorGraphNode
    {
        public string NodeId { get; set; }
        public string LogicalId { get; set; }
        public ConveyorGraphNodeKind Kind { get; set; }
        public string ProcessTag { get; set; }
        public float ProcessServiceSeconds { get; set; }
        public bool PassThroughAllowed { get; set; }
        public StationPassThroughPolicy PassThroughPolicy { get; set; }
        public ConveyorReturnPolicy ReturnPolicy { get; set; }
        public float ReverseSetupSeconds { get; set; }
        public float TransferSeconds { get; set; }
        /// <summary>入端口 → 允许的出端口转移。空表示无转向限制（Any↔Any）。</summary>
        public List<MovementRule> MovementRules { get; set; } = new List<MovementRule>();
        /// <summary>无转向限制时为 true：所有入端口规范为 Any。</summary>
        public bool CollapsePortsToAny { get; set; } = true;
    }

    /// <summary>不可变运行时图：节点、弧、物理段与邻接。</summary>
    public sealed class ConveyorGraph
    {
        private readonly Dictionary<string, ConveyorGraphNode> _nodes;
        private readonly Dictionary<string, ConveyorArc> _arcs;
        private readonly Dictionary<string, PhysicalSegment> _segments;
        private readonly Dictionary<string, List<ConveyorArc>> _outArcs;
        private readonly Dictionary<string, List<ConveyorArc>> _inArcs;
        private readonly Dictionary<(string from, string to), ConveyorArc> _arcByEndpoints;

        public IReadOnlyDictionary<string, ConveyorGraphNode> Nodes => _nodes;
        public IReadOnlyDictionary<string, ConveyorArc> Arcs => _arcs;
        public IReadOnlyDictionary<string, PhysicalSegment> PhysicalSegments => _segments;
        public IReadOnlyList<string> InfeedNodeIds { get; }
        public IReadOnlyList<string> OutfeedNodeIds { get; }
        public IReadOnlyList<string> PickupNodeIds { get; }
        public IReadOnlyList<string> ProcessStationNodeIds { get; }
        public float DefaultProcessServiceSeconds { get; }
        public float DefaultSpeedMetersPerSecond { get; }

        internal ConveyorGraph(
            Dictionary<string, ConveyorGraphNode> nodes,
            Dictionary<string, ConveyorArc> arcs,
            Dictionary<string, PhysicalSegment> segments,
            List<string> infeeds,
            List<string> outfeeds,
            List<string> pickups,
            List<string> processStations,
            float defaultProcessServiceSeconds,
            float defaultSpeedMetersPerSecond)
        {
            _nodes = nodes;
            _arcs = arcs;
            _segments = segments;
            InfeedNodeIds = infeeds;
            OutfeedNodeIds = outfeeds;
            PickupNodeIds = pickups;
            ProcessStationNodeIds = processStations;
            DefaultProcessServiceSeconds = defaultProcessServiceSeconds;
            DefaultSpeedMetersPerSecond = defaultSpeedMetersPerSecond;

            _outArcs = new Dictionary<string, List<ConveyorArc>>(StringComparer.Ordinal);
            _inArcs = new Dictionary<string, List<ConveyorArc>>(StringComparer.Ordinal);
            _arcByEndpoints = new Dictionary<(string, string), ConveyorArc>();

            foreach (var id in nodes.Keys)
            {
                _outArcs[id] = new List<ConveyorArc>();
                _inArcs[id] = new List<ConveyorArc>();
            }

            foreach (var arc in arcs.Values)
            {
                _outArcs[arc.FromNodeId].Add(arc);
                _inArcs[arc.ToNodeId].Add(arc);
                _arcByEndpoints[(arc.FromNodeId, arc.ToNodeId)] = arc;
            }
        }

        public bool TryGetNode(string nodeId, out ConveyorGraphNode node) =>
            _nodes.TryGetValue(nodeId ?? string.Empty, out node);

        public bool TryGetArc(string arcId, out ConveyorArc arc) =>
            _arcs.TryGetValue(arcId ?? string.Empty, out arc);

        public bool TryGetArc(string fromNodeId, string toNodeId, out ConveyorArc arc) =>
            _arcByEndpoints.TryGetValue((fromNodeId ?? string.Empty, toNodeId ?? string.Empty), out arc);

        public bool TryGetSegment(string segmentId, out PhysicalSegment segment) =>
            _segments.TryGetValue(segmentId ?? string.Empty, out segment);

        public IReadOnlyList<ConveyorArc> GetOutgoingArcs(string nodeId) =>
            _outArcs.TryGetValue(nodeId ?? string.Empty, out var list) ? list : Array.Empty<ConveyorArc>();

        public IReadOnlyList<ConveyorArc> GetIncomingArcs(string nodeId) =>
            _inArcs.TryGetValue(nodeId ?? string.Empty, out var list) ? list : Array.Empty<ConveyorArc>();

        /// <summary>查找与给定弧共享物理段的反向弧（From/To 对调）。</summary>
        public bool TryGetReverseArc(ConveyorArc arc, out ConveyorArc reverse)
        {
            reverse = null;
            if (arc == null)
            {
                return false;
            }

            if (!TryGetArc(arc.ToNodeId, arc.FromNodeId, out reverse))
            {
                return false;
            }

            return string.Equals(reverse.PhysicalSegmentId, arc.PhysicalSegmentId, StringComparison.Ordinal);
        }

        public string NormalizeEntryPort(ConveyorGraphNode node, string entryPortId)
        {
            if (node != null && node.CollapsePortsToAny)
            {
                return string.Equals(entryPortId, ConveyorPortIds.Source, StringComparison.Ordinal)
                    ? ConveyorPortIds.Source
                    : ConveyorPortIds.Any;
            }

            return string.IsNullOrEmpty(entryPortId) ? ConveyorPortIds.Any : entryPortId;
        }

        public bool CanTransfer(ConveyorGraphNode node, string entryPortId, string exitPortId)
        {
            if (node == null)
            {
                return false;
            }

            var entry = NormalizeEntryPort(node, entryPortId);
            var exit = exitPortId ?? string.Empty;

            if (node.CollapsePortsToAny || node.MovementRules == null || node.MovementRules.Count == 0)
            {
                return true;
            }

            for (var i = 0; i < node.MovementRules.Count; i++)
            {
                var rule = node.MovementRules[i];
                if (string.Equals(rule.EntryPortId, entry, StringComparison.Ordinal)
                    && string.Equals(rule.ExitPortId, exit, StringComparison.Ordinal))
                {
                    return true;
                }

                if (string.Equals(rule.EntryPortId, ConveyorPortIds.Any, StringComparison.Ordinal)
                    && string.Equals(rule.ExitPortId, ConveyorPortIds.Any, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        public float GetTransferSeconds(ConveyorGraphNode node, string entryPortId, string exitPortId)
        {
            if (node?.MovementRules == null || node.MovementRules.Count == 0 || node.CollapsePortsToAny)
            {
                return 0f;
            }

            var entry = NormalizeEntryPort(node, entryPortId);
            for (var i = 0; i < node.MovementRules.Count; i++)
            {
                var rule = node.MovementRules[i];
                if (string.Equals(rule.EntryPortId, entry, StringComparison.Ordinal)
                    && string.Equals(rule.ExitPortId, exitPortId, StringComparison.Ordinal))
                {
                    return Math.Max(0f, rule.TurnSeconds);
                }
            }

            return 0f;
        }

        public IReadOnlyList<ConveyorGraphNode> GetProcessStationsWithTag(string tag)
        {
            var trimmed = tag?.Trim();
            if (string.IsNullOrEmpty(trimmed) || ProcessStationNodeIds == null)
            {
                return Array.Empty<ConveyorGraphNode>();
            }

            var result = new List<ConveyorGraphNode>();
            for (var i = 0; i < ProcessStationNodeIds.Count; i++)
            {
                if (!TryGetNode(ProcessStationNodeIds[i], out var node))
                {
                    continue;
                }

                if (string.Equals(node.ProcessTag?.Trim(), trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(node);
                }
            }

            return result;
        }
    }
}
