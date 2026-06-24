#if UNITY_EDITOR
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Editor
{
    public sealed class ConveyorMapSegmentEdge : ConveyorMapStraightEdge
    {
        public string SegmentKey { get; }

        private readonly Label _segmentLabel;
        private bool _trackingBound;

        public ConveyorMapSegmentEdge(
            string segmentKey,
            Port output,
            Port input,
            SimConveyorMapEdge data,
            WarehouseConveyorMap map)
        {
            SegmentKey = segmentKey;
            this.output = output;
            this.input = input;

            var fromLabel = SimEntityNaming.FormatLogicalId(map, data.FromNodeId);
            var toLabel = SimEntityNaming.FormatLogicalId(map, data.ToNodeId);
            var bidirectional = ConveyorMapEditorEdgeUtility.IsBidirectionalSegment(map, data.FromNodeId, data.ToNodeId);
            var direction = bidirectional ? $"{fromLabel}↔{toLabel}" : $"{fromLabel}→{toLabel}";
            _segmentLabel = new Label($"{direction} {data.DistanceMeters:0.#}m");
            _segmentLabel.pickingMode = PickingMode.Ignore;
            _segmentLabel.style.position = Position.Absolute;
            _segmentLabel.style.fontSize = 9;
            _segmentLabel.style.color = new Color(0.85f, 0.85f, 0.85f);
            _segmentLabel.style.backgroundColor = new Color(0.12f, 0.12f, 0.12f, 0.75f);
            _segmentLabel.style.paddingLeft = 3;
            _segmentLabel.style.paddingRight = 3;
            _segmentLabel.style.borderTopLeftRadius = 2;
            _segmentLabel.style.borderTopRightRadius = 2;
            _segmentLabel.style.borderBottomLeftRadius = 2;
            _segmentLabel.style.borderBottomRightRadius = 2;
            Add(_segmentLabel);

            RegisterCallback<AttachToPanelEvent>(_ => BindEndpointTracking());
            RegisterCallback<DetachFromPanelEvent>(_ => UnbindEndpointTracking());
            RegisterCallback<GeometryChangedEvent>(_ => UpdateEdgeControl());
            edgeControl.RegisterCallback<GeometryChangedEvent>(OnEdgeGeometryChanged);
        }

        public override bool UpdateEdgeControl()
        {
            if (!UpdateStraightEdgeControl())
            {
                return false;
            }

            UpdateSegmentLabelPosition();
            return true;
        }

        private void OnEdgeGeometryChanged(GeometryChangedEvent evt) => UpdateEdgeControl();

        private void BindEndpointTracking()
        {
            UnbindEndpointTracking();

            if (output != null)
            {
                output.RegisterCallback<GeometryChangedEvent>(OnEdgeGeometryChanged);
            }

            if (input != null)
            {
                input.RegisterCallback<GeometryChangedEvent>(OnEdgeGeometryChanged);
            }

            output?.node?.RegisterCallback<GeometryChangedEvent>(OnEdgeGeometryChanged);
            input?.node?.RegisterCallback<GeometryChangedEvent>(OnEdgeGeometryChanged);

            var graphView = GetFirstAncestorOfType<GraphView>();
            if (graphView != null)
            {
                graphView.viewTransformChanged += OnGraphViewTransformChanged;
            }

            _trackingBound = true;
            schedule.Execute(() => { UpdateEdgeControl(); });
        }

        private void UnbindEndpointTracking()
        {
            if (!_trackingBound)
            {
                return;
            }

            if (output != null)
            {
                output.UnregisterCallback<GeometryChangedEvent>(OnEdgeGeometryChanged);
            }

            if (input != null)
            {
                input.UnregisterCallback<GeometryChangedEvent>(OnEdgeGeometryChanged);
            }

            output?.node?.UnregisterCallback<GeometryChangedEvent>(OnEdgeGeometryChanged);
            input?.node?.UnregisterCallback<GeometryChangedEvent>(OnEdgeGeometryChanged);

            var graphView = GetFirstAncestorOfType<GraphView>();
            if (graphView != null)
            {
                graphView.viewTransformChanged -= OnGraphViewTransformChanged;
            }

            _trackingBound = false;
        }

        private void OnGraphViewTransformChanged(GraphView graphView) => UpdateEdgeControl();

        private void UpdateSegmentLabelPosition()
        {
            if (_segmentLabel == null || panel == null)
            {
                return;
            }

            var mid = GetLabelAnchorInEdgeSpace();
            var layout = _segmentLabel.layout;
            var w = layout.width > 1f ? layout.width : _segmentLabel.resolvedStyle.width;
            var h = layout.height > 1f ? layout.height : _segmentLabel.resolvedStyle.height;
            if (w <= 1f)
            {
                w = 96f;
            }

            if (h <= 1f)
            {
                h = 14f;
            }

            _segmentLabel.style.left = mid.x - w * 0.5f;
            _segmentLabel.style.top = mid.y - h * 0.5f;
        }

        private Vector2 GetLabelAnchorInEdgeSpace() =>
            (edgeControl.from + edgeControl.to) * 0.5f;

        public void SetSelectedVisual(bool selected)
        {
            if (selected)
            {
                AddToClassList("conveyor-segment-selected");
            }
            else
            {
                RemoveFromClassList("conveyor-segment-selected");
            }
        }
    }
}
#endif
