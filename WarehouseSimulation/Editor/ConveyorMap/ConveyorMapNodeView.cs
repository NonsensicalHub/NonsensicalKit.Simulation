#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Editor
{
    public sealed class ConveyorMapNodeView : Node
    {
        public const float DefaultWidth = 120f;
        public const float DefaultHeight = 44f;

        private static readonly Color InfeedColor = new(0.35f, 0.75f, 0.45f, 1f);
        private static readonly Color JunctionColor = new(0.95f, 0.78f, 0.25f, 1f);
        private static readonly Color PickupBothColor = new(0.45f, 0.65f, 0.95f, 1f);
        private static readonly Color PickupInboundColor = new(0.38f, 0.72f, 0.88f, 1f);
        private static readonly Color PickupOutboundColor = new(0.52f, 0.58f, 0.95f, 1f);
        private static readonly Color OutfeedColor = new(0.85f, 0.45f, 0.35f, 1f);
        private static readonly Color ProcessStationColor = new(0.72f, 0.42f, 0.88f, 1f);
        private static readonly Color VerticalTransferColor = new(0.35f, 0.78f, 0.82f, 1f);
        private static readonly Color BorderColor = new(0.15f, 0.15f, 0.15f, 0.85f);

        private static GUIStyle s_TitleStyle;

        public int NodeIndex { get; }
        public string NodeId { get; }
        public string DisplayLabel { get; private set; }
        public SimConveyorNodeKind Kind { get; }

        /// <summary>仅用于路段几何锚点，不向用户展示。</summary>
        public Port OutputAnchor { get; private set; }

        /// <summary>仅用于路段几何锚点，不向用户展示。</summary>
        public Port InputAnchor { get; private set; }

        private IMGUIContainer _titleImgui;

        public ConveyorMapNodeView(int nodeIndex, SimConveyorMapNode node)
        {
            ConveyorMapGraphViewStyles.ApplyTo(this);
            UseDefaultStyling();

            NodeIndex = nodeIndex;
            Kind = node.Kind;
            NodeId = node.NodeId ?? $"node-{nodeIndex}";
            DisplayLabel = ConveyorMapNodeIdentityUtility.ResolveDisplayLabel(in node, nodeIndex);
            viewDataKey = NodeId;
            title = string.Empty;

            AddToClassList("conveyor-map-node");
            capabilities &= ~Capabilities.Collapsible;
            expanded = false;

            ConfigureLayout();
            HideSidePortContainers();
            CreateCenterAnchors();
            CreateTitleImgui();
            ApplyNodeStyle(in node);

            RefreshPorts();
            RefreshExpandedState();
            RefreshChrome();

            RegisterCallback<AttachToPanelEvent>(_ => RefreshChrome());
            RegisterCallback<GeometryChangedEvent>(_ => RefreshChrome());
            schedule.Execute(RefreshChrome);
        }

        public void RefreshDisplay(in SimConveyorMapNode node)
        {
            DisplayLabel = ConveyorMapNodeIdentityUtility.ResolveDisplayLabel(in node, NodeIndex);
            ApplyNodeStyle(in node);
            _titleImgui?.MarkDirtyRepaint();
        }

        public void RegisterConnectionManipulator()
        {
            ((VisualElement)this).AddManipulator(new ConveyorMapNodeConnectorManipulator());
        }

        internal static void ConfigureHiddenAnchorPort(UnityEditor.Experimental.GraphView.Port port)
        {
            port.portName = string.Empty;
            port.pickingMode = PickingMode.Ignore;
            port.style.opacity = 0;
            port.style.width = 0;
            port.style.minWidth = 0;
            port.style.height = 0;
            port.style.minHeight = 0;
            port.style.marginLeft = 0;
            port.style.marginRight = 0;
            port.style.borderTopWidth = 0;
            port.style.borderBottomWidth = 0;

            var connector = port.Q("connector");
            if (connector != null)
            {
                connector.style.opacity = 0;
                connector.style.width = 0;
                connector.style.height = 0;
            }
        }

        private void ConfigureLayout()
        {
            titleContainer.style.display = DisplayStyle.None;

            topContainer.style.display = DisplayStyle.None;
            topContainer.style.height = 0;
            topContainer.style.minHeight = 0;
            topContainer.style.marginTop = 0;
            topContainer.style.marginBottom = 0;
            topContainer.style.paddingTop = 0;
            topContainer.style.paddingBottom = 0;

            mainContainer.style.flexGrow = 1;
            mainContainer.style.flexShrink = 0;
            mainContainer.style.minHeight = DefaultHeight;
            mainContainer.style.height = DefaultHeight;
            mainContainer.style.backgroundColor = new StyleColor(Color.clear);
            mainContainer.style.paddingTop = 0;
            mainContainer.style.paddingBottom = 0;
            mainContainer.style.marginTop = 0;
            mainContainer.style.marginBottom = 0;
            mainContainer.style.borderTopWidth = 0;
            mainContainer.style.borderBottomWidth = 0;
            mainContainer.style.borderLeftWidth = 0;
            mainContainer.style.borderRightWidth = 0;

            extensionContainer.style.display = DisplayStyle.None;
            ClearInnerChrome();
        }

        private void ClearInnerChrome()
        {
            var nodeBorder = this.Q("node-border");
            if (nodeBorder == null)
            {
                return;
            }

            nodeBorder.style.backgroundColor = new StyleColor(Color.clear);
            nodeBorder.style.borderTopWidth = 0;
            nodeBorder.style.borderBottomWidth = 0;
            nodeBorder.style.borderLeftWidth = 0;
            nodeBorder.style.borderRightWidth = 0;
            nodeBorder.style.borderTopLeftRadius = 0;
            nodeBorder.style.borderTopRightRadius = 0;
            nodeBorder.style.borderBottomLeftRadius = 0;
            nodeBorder.style.borderBottomRightRadius = 0;
        }

        private void HideSidePortContainers()
        {
            inputContainer.style.width = 0;
            inputContainer.style.minWidth = 0;
            inputContainer.style.paddingLeft = 0;
            inputContainer.style.paddingRight = 0;
            inputContainer.style.marginLeft = 0;
            inputContainer.style.marginRight = 0;
            inputContainer.style.borderLeftWidth = 0;
            inputContainer.style.borderRightWidth = 0;

            outputContainer.style.width = 0;
            outputContainer.style.minWidth = 0;
            outputContainer.style.paddingLeft = 0;
            outputContainer.style.paddingRight = 0;
            outputContainer.style.marginLeft = 0;
            outputContainer.style.marginRight = 0;
            outputContainer.style.borderLeftWidth = 0;
            outputContainer.style.borderRightWidth = 0;
        }

        private void CreateCenterAnchors()
        {
            var anchorHost = new VisualElement
            {
                name = "anchor-host",
                pickingMode = PickingMode.Ignore,
            };
            anchorHost.style.position = Position.Absolute;
            anchorHost.style.left = Length.Percent(50);
            anchorHost.style.top = Length.Percent(50);
            anchorHost.style.width = 0;
            anchorHost.style.height = 0;

            OutputAnchor = InstantiatePort(
                Orientation.Horizontal,
                Direction.Output,
                UnityEditor.Experimental.GraphView.Port.Capacity.Multi,
                typeof(float));
            ConfigureHiddenAnchorPort(OutputAnchor);

            InputAnchor = InstantiatePort(
                Orientation.Horizontal,
                Direction.Input,
                UnityEditor.Experimental.GraphView.Port.Capacity.Multi,
                typeof(float));
            ConfigureHiddenAnchorPort(InputAnchor);

            anchorHost.Add(OutputAnchor);
            anchorHost.Add(InputAnchor);
            Add(anchorHost);
        }

        private void CreateTitleImgui()
        {
            _titleImgui = new IMGUIContainer(DrawTitleImgui)
            {
                name = "node-title-imgui",
                pickingMode = PickingMode.Ignore,
            };
            _titleImgui.style.position = Position.Absolute;
            _titleImgui.style.left = 0;
            _titleImgui.style.top = 0;
            _titleImgui.style.right = 0;
            _titleImgui.style.bottom = 0;
            _titleImgui.style.width = Length.Percent(100);
            _titleImgui.style.height = Length.Percent(100);
            _titleImgui.style.marginLeft = 0;
            _titleImgui.style.marginTop = 0;
            _titleImgui.style.marginRight = 0;
            _titleImgui.style.marginBottom = 0;
            _titleImgui.style.paddingLeft = 0;
            _titleImgui.style.paddingTop = 0;
            _titleImgui.style.paddingRight = 0;
            _titleImgui.style.paddingBottom = 0;
            _titleImgui.style.backgroundColor = new StyleColor(Color.clear);
            _titleImgui.style.borderTopWidth = 0;
            _titleImgui.style.borderBottomWidth = 0;
            _titleImgui.style.borderLeftWidth = 0;
            _titleImgui.style.borderRightWidth = 0;
            Add(_titleImgui);
        }

        private void DrawTitleImgui()
        {
            if (s_TitleStyle == null)
            {
                s_TitleStyle = new GUIStyle
                {
                    alignment = TextAnchor.UpperLeft,
                    fontSize = 14,
                    fontStyle = FontStyle.Bold,
                    clipping = TextClipping.Overflow,
                    wordWrap = false,
                    border = new RectOffset(0, 0, 0, 0),
                    padding = new RectOffset(0, 0, 0, 0),
                    margin = new RectOffset(0, 0, 0, 0),
                };
                s_TitleStyle.normal.textColor = Color.black;
                s_TitleStyle.normal.background = null;
                s_TitleStyle.hover.background = null;
                s_TitleStyle.active.background = null;
                s_TitleStyle.focused.background = null;
                s_TitleStyle.onNormal.background = null;
            }

            var area = _titleImgui.contentRect;
            if (area.width < 1f)
            {
                area.width = DefaultWidth;
            }

            if (area.height < 1f)
            {
                area.height = DefaultHeight;
            }

            var content = new GUIContent(DisplayLabel);
            var textSize = s_TitleStyle.CalcSize(content);
            var x = (area.width - textSize.x) * 0.5f;
            var y = (area.height - textSize.y) * 0.5f;
            GUI.Label(new Rect(x, y, textSize.x, textSize.y), content, s_TitleStyle);
        }

        private void RefreshChrome()
        {
            ClearInnerChrome();
            _titleImgui?.BringToFront();
        }

        private static Color ResolvePickupColor(SimStackerInteractionMode mode) => mode switch
        {
            SimStackerInteractionMode.InboundOnly => PickupInboundColor,
            SimStackerInteractionMode.OutboundOnly => PickupOutboundColor,
            _ => PickupBothColor,
        };

        private static Color ResolveNodeColor(in SimConveyorMapNode node) => node.Kind switch
        {
            SimConveyorNodeKind.InfeedPort => InfeedColor,
            SimConveyorNodeKind.PickupPoint => ResolvePickupColor(node.StackerInteractionMode),
            SimConveyorNodeKind.OutfeedPort => OutfeedColor,
            SimConveyorNodeKind.ProcessStation => ProcessStationColor,
            SimConveyorNodeKind.VerticalTransfer => VerticalTransferColor,
            _ => JunctionColor,
        };

        private void ApplyNodeStyle(in SimConveyorMapNode node)
        {
            var bg = ResolveNodeColor(in node);

            var opaqueBg = new Color(bg.r, bg.g, bg.b, 1f);
            style.backgroundColor = new StyleColor(opaqueBg);

            style.minWidth = DefaultWidth;
            style.minHeight = DefaultHeight;
            style.width = DefaultWidth;
            style.height = DefaultHeight;
            style.borderTopLeftRadius = 6;
            style.borderTopRightRadius = 6;
            style.borderBottomLeftRadius = 6;
            style.borderBottomRightRadius = 6;
            style.borderTopWidth = 1;
            style.borderBottomWidth = 1;
            style.borderLeftWidth = 1;
            style.borderRightWidth = 1;
            style.borderTopColor = new StyleColor(BorderColor);
            style.borderBottomColor = new StyleColor(BorderColor);
            style.borderLeftColor = new StyleColor(BorderColor);
            style.borderRightColor = new StyleColor(BorderColor);
            style.opacity = 1f;
        }
    }
}
#endif
