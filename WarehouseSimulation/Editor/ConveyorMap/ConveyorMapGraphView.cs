#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Editor
{
    public sealed class ConveyorMapGraphView : GraphView
    {
        private readonly ConveyorMapEditorWindow _window;
        private WarehouseConveyorMap _map;

        public ConveyorMapGraphView(ConveyorMapEditorWindow window)
        {
            _window = window;
            style.flexGrow = 1;
            ConveyorMapGraphViewStyles.ApplyTo(this);

            Insert(0, new GridBackground());

            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());

            SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);

            graphViewChanged += OnGraphChanged;

            this.AddManipulator(new ContextualMenuManipulator(BuildContextMenu));
        }

        public override void AddToSelection(ISelectable selectable)
        {
            base.AddToSelection(selectable);
            NotifySelectionChanged();
        }

        public override void RemoveFromSelection(ISelectable selectable)
        {
            base.RemoveFromSelection(selectable);
            NotifySelectionChanged();
        }

        public override void ClearSelection()
        {
            base.ClearSelection();
            NotifySelectionChanged();
        }

        public void BindMap(WarehouseConveyorMap map)
        {
            _map = map;
            if (_map == null)
            {
                DeleteElements(graphElements.ToList());
                return;
            }

            ConveyorMapNodeIdentityUtility.EnsureNodeIdentity(_map);
            RebuildGraph();
        }

        public void RebuildGraph()
        {
            if (_map == null)
            {
                return;
            }

            graphViewChanged -= OnGraphChanged;
            DeleteElements(graphElements.ToList());
            graphViewChanged += OnGraphChanged;

            if (_map.Nodes == null)
            {
                return;
            }

            var idToIndex = BuildIdIndex();

            for (var i = 0; i < _map.Nodes.Length; i++)
            {
                var node = _map.Nodes[i];
                var view = new ConveyorMapNodeView(i, node);
                view.RegisterConnectionManipulator();
                var pos = _map.GetNodePosition(node.NodeId, i);
                view.SetPosition(new Rect(pos.x, pos.y, ConveyorMapNodeView.DefaultWidth, ConveyorMapNodeView.DefaultHeight));
                AddElement(view);
            }

            if (_map.Edges == null)
            {
                return;
            }

            var drawnUndirected = new HashSet<string>(StringComparer.Ordinal);
            foreach (var edge in _map.Edges)
            {
                var fromId = edge.FromNodeId?.Trim();
                var toId = edge.ToNodeId?.Trim();
                if (ConveyorMapEditorEdgeUtility.IsBidirectionalSegment(_map, fromId, toId))
                {
                    var pairKey = SimEntityNaming.CanonicalSegmentKey(fromId, toId);
                    if (!drawnUndirected.Add(pairKey))
                    {
                        continue;
                    }
                }

                if (!TryResolveEdgeEndpoint(edge.FromNodeId, idToIndex, out var from)
                    || !TryResolveEdgeEndpoint(edge.ToNodeId, idToIndex, out var to))
                {
                    continue;
                }

                var fromView = FindNodeView(from);
                var toView = FindNodeView(to);
                if (fromView?.OutputAnchor == null || toView?.InputAnchor == null)
                {
                    continue;
                }

                var segmentKey = ConveyorMapEditorEdgeUtility.DirectedSegmentKey(edge.FromNodeId, edge.ToNodeId);
                AddElement(CreateSegmentEdge(
                    segmentKey,
                    fromView.OutputAnchor,
                    toView.InputAnchor,
                    edge));
            }
        }

        public ConveyorMapNodeView FindNodeView(int nodeIndex)
        {
            return nodes.OfType<ConveyorMapNodeView>().FirstOrDefault(n => n.NodeIndex == nodeIndex);
        }

        public ConveyorMapNodeView FindNodeViewById(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId))
            {
                return null;
            }

            return nodes.OfType<ConveyorMapNodeView>()
                .FirstOrDefault(n => string.Equals(n.NodeId, nodeId.Trim(), StringComparison.Ordinal));
        }

        public void SelectNode(int nodeIndex)
        {
            ClearSelection();
            var view = FindNodeView(nodeIndex);
            if (view != null)
            {
                AddToSelection(view);
            }
        }

        public void SelectSegment(string segmentKey)
        {
            ClearSelection();
            var edge = edges.OfType<ConveyorMapSegmentEdge>()
                .FirstOrDefault(e => e.SegmentKey == segmentKey);
            if (edge != null)
            {
                AddToSelection(edge);
            }
        }

        public void FrameAllNodes()
        {
            if (nodes == null || nodes.Count() == 0)
            {
                return;
            }

            FrameAll();
        }

        public void ConnectNodes(ConveyorMapNodeView from, ConveyorMapNodeView to)
        {
            if (from == null || to == null || from == to)
            {
                return;
            }

            if (!TryAddDirectedEdge(from.NodeIndex, to.NodeIndex, out var segmentKey, out var message))
            {
                _window.SetStatusMessage(message);
                return;
            }

            AddElement(CreateSegmentEdge(
                segmentKey,
                from.OutputAnchor,
                to.InputAnchor,
                FindEdgeData(segmentKey)));
            _window.OnGraphSegmentSelected(segmentKey);
            _window.SetStatusMessage(message);
        }

        public void AddNodeOfKind(SimConveyorNodeKind kind)
        {
            if (_map == null)
            {
                return;
            }

            RecordUndo("添加节点");
            var list = _map.Nodes != null
                ? new List<SimConveyorMapNode>(_map.Nodes)
                : new List<SimConveyorMapNode>();

            var logicalId = ConveyorMapEditorWindow.GenerateLogicalId(kind, list);
            var node = new SimConveyorMapNode
            {
                NodeId = ConveyorMapEditorWindow.GenerateNodeGuid(),
                LogicalId = logicalId,
                Kind = kind,
            };
            switch (kind)
            {
                case SimConveyorNodeKind.PickupPoint:
                    node.StackerId = 0;
                    node.StackerInteractionMode = SimStackerInteractionMode.Both;
                    break;
                case SimConveyorNodeKind.ProcessStation:
                    node.ProcessMode = SimConveyorProcessMode.Dwell;
                    node.ProcessTag = "wrap";
                    break;
                case SimConveyorNodeKind.VerticalTransfer:
                    node.TransferMotion = SimConveyorVerticalTransferMotion.LinearVertical;
                    break;
            }

            var pos = contentViewContainer.WorldToLocal(
                new Vector2(layout.width * 0.5f, layout.height * 0.5f));
            if (float.IsNaN(pos.x) || float.IsNaN(pos.y))
            {
                pos = new Vector2(80f + list.Count * 24f, 80f + list.Count * 18f);
            }

            node.EditorPosition = pos;
            list.Add(node);
            _map.Nodes = list.ToArray();
            MarkDirty();
            RebuildGraph();
            _window.OnGraphNodeSelected(list.Count - 1);
            _window.SetStatusMessage($"已添加{ConveyorMapEditorWindow.KindLabel(kind)}：{logicalId}");
        }

        public void AutoLayout()
        {
            if (_map?.Nodes == null || _map.Nodes.Length == 0)
            {
                return;
            }

            RecordUndo("自动排版");
            ConveyorMapEditorWindow.RunAutoLayout(_map);
            MarkDirty();
            RebuildGraph();
            FrameAllNodes();
            _window.SetStatusMessage("已按拓扑层级自动排版。");
        }

        private void BuildContextMenu(ContextualMenuPopulateEvent evt)
        {
            if (_map == null)
            {
                return;
            }

            evt.menu.AppendAction("添加入库口", _ => AddNodeOfKind(SimConveyorNodeKind.InfeedPort));
            evt.menu.AppendAction("添加路口", _ => AddNodeOfKind(SimConveyorNodeKind.Junction));
            evt.menu.AppendAction("添加堆垛机交互点", _ => AddNodeOfKind(SimConveyorNodeKind.PickupPoint));
            evt.menu.AppendAction("添加出库口", _ => AddNodeOfKind(SimConveyorNodeKind.OutfeedPort));
            evt.menu.AppendAction("添加加工站点", _ => AddNodeOfKind(SimConveyorNodeKind.ProcessStation));
            evt.menu.AppendAction("添加垂直提升机", _ => AddNodeOfKind(SimConveyorNodeKind.VerticalTransfer));
            evt.menu.AppendSeparator();

            var targetNode = evt.target is VisualElement ve
                ? ve.GetFirstAncestorOfType<ConveyorMapNodeView>()
                : null;
            if (targetNode != null)
            {
                evt.menu.AppendAction("删除节点", _ => DeleteNode(targetNode.NodeIndex));
            }
        }

        private GraphViewChange OnGraphChanged(GraphViewChange change)
        {
            if (_map == null)
            {
                return change;
            }

            if (change.edgesToCreate != null)
            {
                foreach (var edge in change.edgesToCreate.ToList())
                {
                    change.edgesToCreate.Remove(edge);
                }
            }

            if (change.elementsToRemove != null)
            {
                RecordUndo("删除图元");
                var nodesToDelete = change.elementsToRemove
                    .OfType<ConveyorMapNodeView>()
                    .Select(nodeView => nodeView.NodeIndex)
                    .OrderByDescending(index => index)
                    .ToList();
                var segmentsToDelete = change.elementsToRemove
                    .OfType<ConveyorMapSegmentEdge>()
                    .Select(segmentEdge => segmentEdge.SegmentKey)
                    .ToList();

                foreach (var nodeIndex in nodesToDelete)
                {
                    ConveyorMapEditorWindow.DeleteNodeAtIndex(_map, nodeIndex);
                }

                if (nodesToDelete.Count > 0)
                {
                    _window.OnGraphNodeSelected(-1);
                }

                foreach (var segmentKey in segmentsToDelete)
                {
                    ConveyorMapEditorWindow.RemoveSegment(_map, segmentKey);
                }

                if (segmentsToDelete.Count > 0)
                {
                    _window.OnGraphSegmentSelected(null);
                }

                MarkDirty();
                schedule.Execute(RebuildGraph);
            }

            if (change.movedElements != null)
            {
                RecordUndo("移动节点");
                foreach (var el in change.movedElements)
                {
                    if (el is not ConveyorMapNodeView nodeView)
                    {
                        continue;
                    }

                    var rect = nodeView.GetPosition();
                    _map.SetNodePosition(nodeView.NodeId, rect.position);
                }

                MarkDirty();
            }

            return change;
        }

        private void NotifySelectionChanged()
        {
            var nodeView = selection.OfType<ConveyorMapNodeView>().FirstOrDefault();
            if (nodeView != null)
            {
                _window.OnGraphNodeSelected(nodeView.NodeIndex);
                return;
            }

            var segmentEdge = selection.OfType<ConveyorMapSegmentEdge>().FirstOrDefault();
            if (segmentEdge != null)
            {
                _window.OnGraphSegmentSelected(segmentEdge.SegmentKey);
                return;
            }

            if (!selection.Any())
            {
                _window.OnGraphSelectionCleared();
            }
        }

        private ConveyorMapSegmentEdge CreateSegmentEdge(
            string segmentKey,
            Port output,
            Port input,
            SimConveyorMapEdge data)
        {
            var edge = new ConveyorMapSegmentEdge(segmentKey, output, input, data, _map);
            if (output != null)
            {
                output.Connect(edge);
            }

            if (input != null)
            {
                input.Connect(edge);
            }

            return edge;
        }

        private SimConveyorMapEdge FindEdgeData(string segmentKey)
        {
            if (_map.Edges == null
                || !ConveyorMapEditorEdgeUtility.TryParseDirectedSegmentKey(segmentKey, out var from, out var to))
            {
                return default;
            }

            foreach (var e in _map.Edges)
            {
                if (e.FromNodeId?.Trim() == from && e.ToNodeId?.Trim() == to)
                {
                    return e;
                }
            }

            return default;
        }

        private bool TryAddDirectedEdge(int fromIndex, int toIndex, out string segmentKey, out string message)
        {
            segmentKey = null;
            message = string.Empty;
            if (_map.Nodes == null || fromIndex == toIndex)
            {
                return false;
            }

            var fromId = _map.Nodes[fromIndex].NodeId?.Trim();
            var toId = _map.Nodes[toIndex].NodeId?.Trim();
            if (string.IsNullOrEmpty(fromId) || string.IsNullOrEmpty(toId))
            {
                message = "节点缺少内部 ID，无法创建路段。";
                return false;
            }

            segmentKey = ConveyorMapEditorEdgeUtility.DirectedSegmentKey(fromId, toId);
            var edges = _map.Edges != null
                ? new List<SimConveyorMapEdge>(_map.Edges)
                : new List<SimConveyorMapEdge>();

            if (ConveyorMapEditorEdgeUtility.HasDirectedEdge(edges, fromId, toId))
            {
                message = "该方向的路段已存在。";
                return false;
            }

            RecordUndo("连线");
            var distance = _map.DefaultEdgeDistanceMeters > 0f ? _map.DefaultEdgeDistanceMeters : 4f;
            edges.Add(new SimConveyorMapEdge
            {
                FromNodeId = fromId,
                ToNodeId = toId,
                DistanceMeters = distance,
            });

            _map.Edges = edges.ToArray();
            MarkDirty();
            message = $"已创建路段：{fromId} → {toId}，默认距离 {distance:0.##} m";
            return true;
        }

        public void DeleteNode(int index)
        {
            RecordUndo("删除节点");
            ConveyorMapEditorWindow.DeleteNodeAtIndex(_map, index);
            MarkDirty();
            RebuildGraph();
            _window.OnGraphNodeSelected(-1);
        }

        private Dictionary<string, int> BuildIdIndex()
        {
            var dict = new Dictionary<string, int>(StringComparer.Ordinal);
            if (_map.Nodes == null)
            {
                return dict;
            }

            for (var i = 0; i < _map.Nodes.Length; i++)
            {
                var id = _map.Nodes[i].NodeId?.Trim();
                if (!string.IsNullOrEmpty(id))
                {
                    dict[id] = i;
                }

                var logicalId = _map.Nodes[i].LogicalId?.Trim();
                if (!string.IsNullOrEmpty(logicalId) && !dict.ContainsKey(logicalId))
                {
                    dict[logicalId] = i;
                }
            }

            return dict;
        }

        private static bool TryResolveEdgeEndpoint(
            string endpointId,
            Dictionary<string, int> idToIndex,
            out int nodeIndex)
        {
            nodeIndex = -1;
            var key = endpointId?.Trim() ?? string.Empty;
            return !string.IsNullOrEmpty(key) && idToIndex.TryGetValue(key, out nodeIndex);
        }

        private void RecordUndo(string actionName)
        {
            if (_map != null)
            {
                Undo.RecordObject(_map, actionName);
            }
        }

        private void MarkDirty()
        {
            if (_map != null)
            {
                EditorUtility.SetDirty(_map);
            }
        }
    }
}
#endif
