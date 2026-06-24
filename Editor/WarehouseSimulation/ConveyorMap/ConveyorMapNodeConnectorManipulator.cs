#if UNITY_EDITOR
using System.Linq;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Editor
{
    /// <summary>
    /// 状态机式连线：节点本身即端点，右键或 Alt+左键从节点拖向另一节点创建路段。
    /// </summary>
    internal sealed class ConveyorMapNodeConnectorManipulator : MouseManipulator
    {
        private const float DragThresholdSq = 36f;

        private ConveyorMapNodeView _sourceNode;
        private ConveyorMapGraphView _graphView;
        private GhostPortNode _ghostNode;
        private Edge _ghostEdge;
        private bool _pointerDown;
        private bool _dragging;
        private Vector2 _pointerDownPos;

        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<MouseDownEvent>(OnMouseDown);
            target.RegisterCallback<MouseMoveEvent>(OnMouseMove);
            target.RegisterCallback<MouseUpEvent>(OnMouseUp);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<MouseDownEvent>(OnMouseDown);
            target.UnregisterCallback<MouseMoveEvent>(OnMouseMove);
            target.UnregisterCallback<MouseUpEvent>(OnMouseUp);
            EndGhost();
        }

        private void OnMouseDown(MouseDownEvent evt)
        {
            var isRightDrag = evt.button == (int)MouseButton.RightMouse;
            var isAltLeftDrag = evt.button == (int)MouseButton.LeftMouse && evt.altKey;
            if (!isRightDrag && !isAltLeftDrag)
            {
                return;
            }

            if (target is not ConveyorMapNodeView node)
            {
                return;
            }

            _graphView = node.GetFirstAncestorOfType<ConveyorMapGraphView>();
            if (_graphView == null)
            {
                return;
            }

            _sourceNode = node;
            _pointerDown = true;
            _dragging = false;
            _pointerDownPos = evt.mousePosition;
        }

        private void OnMouseMove(MouseMoveEvent evt)
        {
            if (!_pointerDown)
            {
                return;
            }

            if (!_dragging)
            {
                var delta = evt.mousePosition - _pointerDownPos;
                if (delta.sqrMagnitude < DragThresholdSq)
                {
                    return;
                }

                target.CaptureMouse();
                StartGhost();
                _dragging = true;
            }

            UpdateGhostPosition(evt.mousePosition);
            evt.StopPropagation();
        }

        private void OnMouseUp(MouseUpEvent evt)
        {
            if (!_pointerDown)
            {
                return;
            }

            var isRightDrag = evt.button == (int)MouseButton.RightMouse;
            var isAltLeftDrag = evt.button == (int)MouseButton.LeftMouse && evt.altKey;
            if (!isRightDrag && !isAltLeftDrag)
            {
                return;
            }

            _pointerDown = false;

            if (_dragging)
            {
                if (target.HasMouseCapture())
                {
                    target.ReleaseMouse();
                }

                var targetNode = PickTargetNode(evt.mousePosition);
                if (targetNode != null && targetNode != _sourceNode)
                {
                    _graphView.ConnectNodes(_sourceNode, targetNode);
                }

                EndGhost();
                evt.StopPropagation();
            }

            _dragging = false;
            _sourceNode = null;
        }

        private void StartGhost()
        {
            if (_graphView == null || _sourceNode == null)
            {
                return;
            }

            _ghostNode = new GhostPortNode();
            _graphView.AddElement(_ghostNode);
            _ghostEdge = new ConveyorMapStraightEdge
            {
                output = _sourceNode.OutputAnchor,
                input = _ghostNode.InputAnchor,
                pickingMode = PickingMode.Ignore,
            };
            _graphView.AddElement(_ghostEdge);
            _ghostEdge.UpdateEdgeControl();
        }

        private void UpdateGhostPosition(Vector2 screenMousePos)
        {
            if (_ghostNode == null || _graphView == null)
            {
                return;
            }

            var local = _graphView.contentViewContainer.WorldToLocal(screenMousePos);
            _ghostNode.SetPosition(new Rect(local.x, local.y, 1f, 1f));
            _ghostEdge?.UpdateEdgeControl();
        }

        private void EndGhost()
        {
            if (_ghostEdge != null && _graphView != null)
            {
                _graphView.RemoveElement(_ghostEdge);
                _ghostEdge = null;
            }

            if (_ghostNode != null && _graphView != null)
            {
                _graphView.RemoveElement(_ghostNode);
                _ghostNode = null;
            }
        }

        private ConveyorMapNodeView PickTargetNode(Vector2 screenMousePos)
        {
            if (_graphView == null)
            {
                return null;
            }

            var local = _graphView.contentViewContainer.WorldToLocal(screenMousePos);
            return _graphView.nodes.OfType<ConveyorMapNodeView>()
                .FirstOrDefault(node => node.GetPosition().Contains(local));
        }

        private sealed class GhostPortNode : Node
        {
            public Port InputAnchor { get; }

            public GhostPortNode()
            {
                InputAnchor = InstantiatePort(
                    Orientation.Horizontal,
                    Direction.Input,
                    UnityEditor.Experimental.GraphView.Port.Capacity.Single,
                    typeof(float));
                ConveyorMapNodeView.ConfigureHiddenAnchorPort(InputAnchor);
                inputContainer.Add(InputAnchor);
                inputContainer.style.width = 0;
                inputContainer.style.minWidth = 0;
                outputContainer.style.width = 0;
                outputContainer.style.minWidth = 0;
                style.width = 1;
                style.height = 1;
                pickingMode = PickingMode.Ignore;
                visible = false;
            }
        }
    }
}
#endif
