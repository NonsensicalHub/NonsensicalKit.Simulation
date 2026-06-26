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
        private int _activeButton = -1;

        public ConveyorMapNodeConnectorManipulator()
        {
            activators.Add(new ManipulatorActivationFilter { button = MouseButton.RightMouse });
            activators.Add(new ManipulatorActivationFilter
            {
                button = MouseButton.LeftMouse,
                modifiers = EventModifiers.Alt,
            });
        }

        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<MouseDownEvent>(OnMouseDown);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<MouseDownEvent>(OnMouseDown);
            EndInteraction();
        }

        private void OnMouseDown(MouseDownEvent evt)
        {
            if (!CanStartConnection(evt))
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
            _activeButton = evt.button;
            _pointerDownPos = evt.mousePosition;

            _graphView.RegisterCallback<MouseMoveEvent>(OnGraphMouseMove);
            _graphView.RegisterCallback<MouseUpEvent>(OnGraphMouseUp);

            if (evt.button == (int)MouseButton.RightMouse)
            {
                evt.StopPropagation();
            }
        }

        private void OnGraphMouseMove(MouseMoveEvent evt)
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

        private void OnGraphMouseUp(MouseUpEvent evt)
        {
            if (!_pointerDown || evt.button != _activeButton)
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

            EndInteraction();
        }

        private void EndInteraction()
        {
            _graphView?.UnregisterCallback<MouseMoveEvent>(OnGraphMouseMove);
            _graphView?.UnregisterCallback<MouseUpEvent>(OnGraphMouseUp);

            if (target != null && target.HasMouseCapture())
            {
                target.ReleaseMouse();
            }

            EndGhost();
            _dragging = false;
            _pointerDown = false;
            _activeButton = -1;
            _sourceNode = null;
            _graphView = null;
        }

        private static bool CanStartConnection(MouseDownEvent evt)
        {
            var isRightDrag = evt.button == (int)MouseButton.RightMouse;
            var isAltLeftDrag = evt.button == (int)MouseButton.LeftMouse && evt.altKey;
            return isRightDrag || isAltLeftDrag;
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
            _sourceNode.OutputAnchor?.Connect(_ghostEdge);
            _ghostNode.InputAnchor?.Connect(_ghostEdge);
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
            if (_ghostEdge != null)
            {
                _sourceNode?.OutputAnchor?.Disconnect(_ghostEdge);
                _ghostNode?.InputAnchor?.Disconnect(_ghostEdge);

                if (_graphView != null)
                {
                    _graphView.RemoveElement(_ghostEdge);
                }

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
