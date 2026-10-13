#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using GlobalObjectId = UnityEditor.GlobalObjectId;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Editor
{
    /// <summary>
    /// 输送地图俯视编辑器。交互对齐工位俯视窗口：平移、缩放、拖拽连线、框选路段。
    /// 画布模式使用节点的排版坐标；场景俯视把同名锚点按世界 XZ 摆开。
    /// 视图旋转只改看图方向，方便对照 CAD，不改节点坐标。
    /// </summary>
    public sealed class ConveyorMapEditorWindow : EditorWindow
    {
        private const float ConfigPanelWidth = 320f;
        private const float LegacyWidth = 88f;
        private const float LegacyHeight = 28f;
        private const float BaseNodeRadius = 9f;
        private const float DragThreshold = 4f;
        private const float ConnectSnapPadding = 22f;
        private const float EdgeHitWidth = 10f;
        private const float MinZoom = 0.02f;
        private const float MaxZoom = 80f;
        private const float SceneMinZoom = 0.05f;
        private const float SceneMaxZoom = 400f;
        private const float FitPadding = 64f;
        private const float TargetNeighborPixels = 56f;
        private const float ViewCullPadding = 96f;
        private const int DiscTextureSize = 64;
        private const int ArrowTextureWidth = 64;
        private const int ArrowTextureHeight = 58;
        private const string PrefsViewYaw = "WarehouseSim.ConveyorMapEditor.ViewYaw";
        private const string PrefsSceneView = "WarehouseSim.ConveyorMapEditor.SceneView";
        private const string PrefsShowLabels = "WarehouseSim.ConveyorMapEditor.ShowLabels";
        private const string PrefsShowTopology = "WarehouseSim.ConveyorMapEditor.ShowTopology";
        private const string PrefsShowFlow = "WarehouseSim.ConveyorMapEditor.ShowFlow";
        private const string PrefsBidirectional = "WarehouseSim.ConveyorMapEditor.Bidirectional";
        private const string SceneAnchorRootPrefsKey = "WarehouseSim.ConveyorMapEditor.SceneAnchorRoot";

        private static readonly Vector2 LegacyHalf = new Vector2(LegacyWidth, LegacyHeight) * 0.5f;
        private static readonly int CanvasControlHint = "ConveyorMapCanvas".GetHashCode();
        private static readonly Color CanvasBg = new Color(0.15f, 0.16f, 0.17f, 1f);
        private static readonly Color GridColor = new Color(1f, 1f, 1f, 0.045f);
        private static readonly Color InfeedColor = new Color(0.22f, 0.62f, 0.38f, 1f);
        private static readonly Color JunctionColor = new Color(0.86f, 0.68f, 0.18f, 1f);
        private static readonly Color PickupBothColor = new Color(0.28f, 0.50f, 0.94f, 1f);
        private static readonly Color PickupInboundColor = new Color(0.16f, 0.66f, 0.78f, 1f);
        private static readonly Color PickupOutboundColor = new Color(0.50f, 0.38f, 0.90f, 1f);
        private static readonly Color OutfeedColor = new Color(0.78f, 0.36f, 0.28f, 1f);
        private static readonly Color ProcessColor = new Color(0.62f, 0.36f, 0.78f, 1f);
        private static readonly Color VerticalColor = new Color(0.22f, 0.66f, 0.70f, 1f);
        private static readonly Color ForwardColor = new Color(0.25f, 0.92f, 0.48f, 0.95f);
        private static readonly Color ReverseColor = new Color(1f, 0.48f, 0.25f, 0.95f);
        private static readonly Color SelectedEdgeColor = new Color(1f, 0.92f, 0.25f, 1f);
        private static readonly Color OneWayPathColor = new Color(1f, 0.62f, 0.12f, 0.98f);
        private static readonly Color BoxSelectFill = new Color(0.35f, 0.65f, 1f, 0.12f);
        private static readonly Color BoxSelectBorder = new Color(0.45f, 0.78f, 1f, 0.95f);
        private static readonly Color AxisOverlay = new Color(0.78f, 0.82f, 0.86f, 0.9f);

        private enum DragMode
        {
            None,
            Pan,
            Connect,
            Move,
            BoxSelect,
        }

        private struct Segment
        {
            public int Lo;
            public int Hi;
            public bool LoToHi;
            public bool HiToLo;
            public string ProcessTag;

            public bool IsValid => Lo >= 0 && Hi >= 0 && Lo != Hi;
            public bool IsOneWay => LoToHi != HiToLo;
            public long Key => Pack(Lo, Hi);
        }

        private readonly List<Segment> _segments = new List<Segment>(128);
        private readonly Dictionary<long, Segment> _segmentByKey = new Dictionary<long, Segment>();
        private readonly List<long> _selectedKeys = new List<long>(32);
        private readonly HashSet<long> _selectedKeySet = new HashSet<long>();
        private readonly Dictionary<string, Transform> _anchors =
            new Dictionary<string, Transform>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _nodeIndexByNodeId =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _nodeIndexByLogicalId =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly HashSet<long> _gatedSegmentKeys = new HashSet<long>();
        private readonly List<int> _oneWayPreviewPath = new List<int>(32);
        private readonly HashSet<long> _oneWayPreviewKeys = new HashSet<long>();
        private string[] _nodeLabels = Array.Empty<string>();
        private Vector2[] _graphPoints = Array.Empty<Vector2>();
        private bool[] _graphValid = Array.Empty<bool>();
        private int _topologyStamp = int.MinValue;
        private int _graphStamp = int.MinValue;
        private bool _topologyReady;
        private bool _graphReady;
        private int _canvasHotId;
        private static Texture2D _whiteDisc;
        private static Texture2D _whiteArrow;
        private static readonly GUIContent InboundMark = new GUIContent("入");
        private static readonly GUIContent OutboundMark = new GUIContent("出");
        private static readonly GUIContent BothMark = new GUIContent("双");

        private WarehouseConveyorMap _map;
        private Transform _sceneAnchorRoot;
        private SerializedObject _serializedMap;
        private Vector2 _configScroll;
        private Vector2 _pan;
        private float _zoom = 1.2f;
        private float _viewYawDegrees;
        private bool _fitPending = true;
        private bool _showLabels = true;
        private bool _showTopology = true;
        private bool _showEdgeFlow = true;
        private bool _connectBidirectional;
        private bool _moveTool;
        private bool _oneWayPathTool;
        private int _oneWayPathFrom = -1;
        private int _oneWayPreviewTo = -1;
        private bool _oneWayPreviewReachable;
        private bool _sceneView;
        private bool _anchorsDirty = true;
        private string _statusMessage = string.Empty;
        private int _hiddenAnchorCount;
        private bool _deferApply;

        private SimConveyorNodeKind _inspectorKind;
        private Rect _canvasRect;
        private int _selectedNode = -1;
        private int _hoverIndex = -1;
        private int _connectFrom = -1;
        private bool _activeReverse;
        private bool _boxSelectAdditive;
        private Segment _hoverSegment;
        private bool _hasHoverSegment;
        private GUIStyle _labelStyle;
        private GUIStyle _axisStyle;

        private DragMode _dragMode;
        private Vector2 _dragStartLocal;
        private Vector2 _dragCurrentLocal;
        private bool _dragMoved;
        private int _moveIndex = -1;
        private Vector2 _moveOriginGraph;
        private Vector2 _moveOriginLocal;
        private bool _moveUndoRecorded;

        [MenuItem("Window/Warehouse Simulation/输送地图编辑器")]
        public static void OpenFromMenu()
        {
            Open(Selection.activeObject as WarehouseConveyorMap);
        }

        public static void Open(WarehouseConveyorMap map)
        {
            var window = GetWindow<ConveyorMapEditorWindow>("输送地图");
            window.minSize = new Vector2(880f, 480f);
            window.SetMap(map);
            window.Show();
            window.Focus();
        }

        private void OnEnable()
        {
            wantsMouseMove = true;
            wantsMouseEnterLeaveWindow = true;
            titleContent = new GUIContent("输送地图");
            _viewYawDegrees = SnapYaw90(EditorPrefs.GetFloat(PrefsViewYaw, 0f));
            _sceneView = EditorPrefs.GetBool(PrefsSceneView, false);
            autoRepaintOnSceneChange = false;
            _showLabels = EditorPrefs.GetBool(PrefsShowLabels, true);
            _showTopology = EditorPrefs.GetBool(PrefsShowTopology, true);
            _showEdgeFlow = EditorPrefs.GetBool(PrefsShowFlow, true);
            _connectBidirectional = EditorPrefs.GetBool(PrefsBidirectional, false);
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            EditorApplication.update += OnEditorUpdate;
            LoadSceneAnchorRoot();
            _anchorsDirty = true;
            _fitPending = true;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
            EditorApplication.update -= OnEditorUpdate;
        }

        private void OnSelectionChange()
        {
            if (Selection.activeObject is WarehouseConveyorMap map && map != _map)
            {
                SetMap(map);
            }
        }

        private void OnUndoRedo()
        {
            _serializedMap = _map != null ? new SerializedObject(_map) : null;
            _anchorsDirty = true;
            _topologyReady = false;
            _graphReady = false;
            Repaint();
        }

        private double _nextSceneViewRepaint;

        private void OnEditorUpdate()
        {
            if (!_sceneView || !hasFocus || EditorApplication.isPlaying)
            {
                return;
            }

            var now = EditorApplication.timeSinceStartup;
            if (now < _nextSceneViewRepaint)
            {
                return;
            }

            _nextSceneViewRepaint = now + 0.25d;
            Repaint();
        }

        private void OnHierarchyChanged()
        {
            _anchorsDirty = true;
            if (_sceneView)
            {
                Repaint();
            }
        }

        private void SetMap(WarehouseConveyorMap map)
        {
            var changed = _map != map;
            _map = map;
            if (_map != null)
            {
                ConveyorMapNodeIdentityUtility.EnsureNodeIdentity(_map);
            }

            _serializedMap = _map != null ? new SerializedObject(_map) : null;
            _selectedNode = -1;
            ClearEdgeSelection();
            ClearOneWayPathPick();
            _statusMessage = string.Empty;
            _anchorsDirty = true;
            _topologyReady = false;
            _graphReady = false;
            titleContent = new GUIContent(_map != null ? $"输送地图 · {_map.name}" : "输送地图");
            if (changed)
            {
                _fitPending = true;
            }

            Repaint();
        }

        private void OnGUI()
        {
            _deferApply = false;
            if (_map == null)
            {
                DrawMissingMap();
                return;
            }

            if (_dragMode != DragMode.None && GUIUtility.hotControl == 0)
            {
                _dragMode = DragMode.None;
                _connectFrom = -1;
                _moveIndex = -1;
                _canvasHotId = 0;
            }

            if (TryConsumeCanvasPointer())
            {
                return;
            }

            DrawToolbar();
            if (_map == null)
            {
                EditorGUILayout.HelpBox("请选择或创建一个 WarehouseConveyorMap 资源。", MessageType.Info);
                return;
            }

            EditorGUILayout.BeginHorizontal();
            DrawConfigPanel();
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            DrawHint();
            DrawCanvas();
            DrawStatusBar();
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawMissingMap()
        {
            EditorGUILayout.Space(12f);
            EditorGUILayout.HelpBox("请选择或创建一个 WarehouseConveyorMap 资源。", MessageType.Info);
            EditorGUI.BeginChangeCheck();
            var next = (WarehouseConveyorMap)EditorGUILayout.ObjectField(
                "地图资源", _map, typeof(WarehouseConveyorMap), false);
            if (EditorGUI.EndChangeCheck())
            {
                SetMap(next);
            }
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            EditorGUI.BeginChangeCheck();
            var nextMap = (WarehouseConveyorMap)EditorGUILayout.ObjectField(
                _map, typeof(WarehouseConveyorMap), false, GUILayout.MinWidth(160f), GUILayout.MaxWidth(260f));
            if (EditorGUI.EndChangeCheck())
            {
                SetMap(nextMap);
            }

            if (GUILayout.Button("刷新", EditorStyles.toolbarButton, GUILayout.Width(44f)))
            {
                _anchorsDirty = true;
                _serializedMap = _map != null ? new SerializedObject(_map) : null;
                Repaint();
            }

            if (GUILayout.Button("适应全部", EditorStyles.toolbarButton, GUILayout.Width(68f)))
            {
                FitView(false);
                Repaint();
            }

            if (GUILayout.Button("聚焦选中", EditorStyles.toolbarButton, GUILayout.Width(68f)))
            {
                FitView(true);
                Repaint();
            }

            GUILayout.Space(6f);
            _showLabels = ToolbarToggle(_showLabels, "名称", 44f, PrefsShowLabels);
            _showTopology = ToolbarToggle(_showTopology, "路段", 44f, PrefsShowTopology);
            _showEdgeFlow = ToolbarToggle(_showEdgeFlow, "流向", 44f, PrefsShowFlow);

            GUILayout.Space(6f);
            var connectOn = !_moveTool && !_oneWayPathTool;
            var nextConnect = GUILayout.Toggle(connectOn, "拖:连线", EditorStyles.toolbarButton, GUILayout.Width(64f));
            if (nextConnect && !connectOn)
            {
                _moveTool = false;
                _oneWayPathTool = false;
            }

            using (new EditorGUI.DisabledScope(_sceneView))
            {
                var nextMove = GUILayout.Toggle(_moveTool && !_sceneView, "移动", EditorStyles.toolbarButton, GUILayout.Width(44f));
                if (nextMove && !_moveTool && !_sceneView)
                {
                    _moveTool = true;
                    _oneWayPathTool = false;
                }
            }

            var nextBi = GUILayout.Toggle(
                _connectBidirectional, "双向", EditorStyles.toolbarButton, GUILayout.Width(44f));
            if (nextBi != _connectBidirectional)
            {
                _connectBidirectional = nextBi;
                EditorPrefs.SetBool(PrefsBidirectional, _connectBidirectional);
            }

            var nextOneWay = GUILayout.Toggle(
                _oneWayPathTool, "路径单向", EditorStyles.toolbarButton, GUILayout.Width(72f));
            if (nextOneWay != _oneWayPathTool)
            {
                _oneWayPathTool = nextOneWay;
                if (_oneWayPathTool)
                {
                    _moveTool = false;
                    if (_selectedNode >= 0)
                    {
                        _oneWayPathFrom = _selectedNode;
                    }
                }
                else
                {
                    ClearOneWayPathPick();
                }
            }

            GUILayout.Space(6f);
            var nextScene = GUILayout.Toggle(_sceneView, "场景俯视", EditorStyles.toolbarButton, GUILayout.Width(72f));
            if (nextScene != _sceneView)
            {
                _sceneView = nextScene;
                if (_sceneView)
                {
                    _moveTool = false;
                }

                EditorPrefs.SetBool(PrefsSceneView, _sceneView);
                autoRepaintOnSceneChange = false;
                _fitPending = true;
                Repaint();
            }

            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(!_hasEdgeSelection))
            {
                if (GUILayout.Button("删除路段", EditorStyles.toolbarButton, GUILayout.Width(68f)))
                {
                    DeleteSelectedSegments();
                }

                if (GUILayout.Button("反转方向", EditorStyles.toolbarButton, GUILayout.Width(68f)))
                {
                    ReverseSelectedOneWay();
                }
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            DrawAddNodeButton("入库口", SimConveyorNodeKind.InfeedPort, 64f);
            DrawAddNodeButton("路口", SimConveyorNodeKind.Junction, 52f);
            DrawAddNodeButton("堆垛机交互点", SimConveyorNodeKind.PickupPoint, 108f);
            DrawAddNodeButton("出库口", SimConveyorNodeKind.OutfeedPort, 64f);
            DrawAddNodeButton("加工站", SimConveyorNodeKind.ProcessStation, 64f);
            DrawAddNodeButton("提升机", SimConveyorNodeKind.VerticalTransfer, 64f);
            GUILayout.Space(8f);
            using (new EditorGUI.DisabledScope(_sceneView))
            {
                if (GUILayout.Button("自动排版", EditorStyles.toolbarButton, GUILayout.Width(72f)))
                {
                    RunAutoLayout();
                }
            }

            GUILayout.Space(8f);
            if (GUILayout.Button("↺90°", EditorStyles.toolbarButton, GUILayout.Width(44f)))
            {
                RotateView(-90f);
            }

            if (GUILayout.Button("↻90°", EditorStyles.toolbarButton, GUILayout.Width(44f)))
            {
                RotateView(90f);
            }

            if (HasViewYaw() && GUILayout.Button("复位", EditorStyles.toolbarButton, GUILayout.Width(44f)))
            {
                SetViewYaw(0f);
            }

            if (HasViewYaw())
            {
                GUILayout.Label($"{_viewYawDegrees:0}°", EditorStyles.miniLabel, GUILayout.Width(36f));
            }

            EditorGUILayout.EndHorizontal();
        }

        private static bool ToolbarToggle(bool value, string label, float width, string prefsKey)
        {
            var next = GUILayout.Toggle(value, label, EditorStyles.toolbarButton, GUILayout.Width(width));
            if (next != value)
            {
                EditorPrefs.SetBool(prefsKey, next);
            }

            return next;
        }

        private void DrawAddNodeButton(string label, SimConveyorNodeKind kind, float width)
        {
            if (GUILayout.Button(label, EditorStyles.toolbarButton, GUILayout.Width(width)))
            {
                AddNode(kind);
            }
        }

        private void DrawHint()
        {
            var place = _sceneView
                ? "场景俯视：节点位置来自同名锚点"
                : "画布：节点位置是排版坐标，可用「移动」拖动";
            var link = _oneWayPathTool
                ? "依次点两个节点，最短路变成起点→终点的单向通路"
                : _connectBidirectional
                    ? "拖到另一节点会建双向路段"
                    : "拖到另一节点建单向路段，按住 Shift 建反向";
            EditorGUILayout.LabelField(
                $"{place} · {link} · 空白处框选路段 · Shift 加选 · 中键或 Alt 平移 · Q/E 或工具栏转 90° · 滚轮缩放",
                EditorStyles.miniLabel);
        }

        private void DrawStatusBar()
        {
            EnsureTopology();
            var nodeCount = _map.Nodes != null ? _map.Nodes.Length : 0;
            var sel = _selectedNode >= 0 ? $" · 选中 {NodeLabel(_selectedNode)}" : string.Empty;
            var hover = _hoverIndex >= 0 ? $" · 悬停 {NodeLabel(_hoverIndex)}" : string.Empty;
            var edge = _selectedKeys.Count > 1
                ? $" · 选中 {_selectedKeys.Count} 条路段"
                : _selectedKeys.Count == 1 && _segmentByKey.TryGetValue(_selectedKeys[0], out var one)
                    ? $" · 选中 {Describe(one)}"
                    : string.Empty;
            var connect = _dragMode == DragMode.Connect && _dragMoved && _connectFrom >= 0
                ? $" · 连线 {NodeLabel(_connectFrom)}"
                : string.Empty;
            var hidden = _sceneView && _hiddenAnchorCount > 0
                ? $" · {_hiddenAnchorCount} 个节点无锚点"
                : string.Empty;
            var mode = _sceneView ? "场景" : "画布";
            var tool = _oneWayPathTool
                ? "路径单向"
                : _moveTool && !_sceneView
                    ? "移动"
                    : _connectBidirectional
                        ? "双向连线"
                        : "单向连线";
            var oneWayPick = _oneWayPathTool && _oneWayPathFrom >= 0
                ? $" · 起点 {NodeLabel(_oneWayPathFrom)}"
                : string.Empty;
            var yaw = HasViewYaw() ? $" · 旋转 {_viewYawDegrees:0}°" : string.Empty;
            EditorGUILayout.LabelField(
                $"{mode} · {tool} · 节点 {nodeCount} · 路段 {_segments.Count} · 缩放 {_zoom:0.##}{yaw}{sel}{oneWayPick}{hover}{connect}{edge}{hidden}",
                EditorStyles.miniLabel);
        }

        private bool _hasEdgeSelection => _selectedKeys.Count > 0;

        private void DrawConfigPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(ConfigPanelWidth), GUILayout.ExpandHeight(true));
            _configScroll = EditorGUILayout.BeginScrollView(_configScroll, GUILayout.ExpandHeight(true));
            var previousLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 132f;
            try
            {
                DrawConfigContents();
            }
            finally
            {
                EditorGUIUtility.labelWidth = previousLabelWidth;
                EditorGUILayout.EndScrollView();
                EditorGUILayout.EndVertical();
            }
        }

        private void DrawConfigContents()
        {
            if (_serializedMap == null || _serializedMap.targetObject != _map)
            {
                _serializedMap = new SerializedObject(_map);
            }

            var canvasDrag = _dragMode != DragMode.None;
            if (!canvasDrag)
            {
                _serializedMap.Update();
            }

            EditorGUILayout.LabelField("选中对象", EditorStyles.boldLabel);
            if (_selectedKeys.Count > 0)
            {
                DrawEdgeSelectionPanel();
            }
            else if (_selectedNode < 0 || _map.Nodes == null || _selectedNode >= _map.Nodes.Length)
            {
                EditorGUILayout.HelpBox(
                    _oneWayPathTool
                        ? "路径单向：先点起点，再点终点。两点间最短路会改成单向。\nEsc 取消起点。"
                        : "从节点拖到另一个节点创建路段。\n空白处拖拽可框选路段，Delete 删除。\nShift 点选或框选为加选。\n画布模式下用「移动」拖动节点。",
                    MessageType.Info);
            }
            else
            {
                DrawNodeInspector();
            }

            EditorGUILayout.Space(8f);
            DrawMapParameters();
            EditorGUILayout.Space(8f);
            DrawSceneSection();
            EditorGUILayout.Space(8f);
            if (GUILayout.Button("校验拓扑", GUILayout.Height(28f)))
            {
                ValidateMap();
            }

            if (!string.IsNullOrEmpty(_statusMessage))
            {
                EditorGUILayout.Space(6f);
                EditorGUILayout.HelpBox(_statusMessage, MessageType.None);
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("图例", EditorStyles.boldLabel);
            DrawLegendRow(InfeedColor, "入库口");
            DrawLegendRow(JunctionColor, "路口");
            DrawLegendRow(PickupBothColor, "堆垛机交互点 · 出入库皆可");
            DrawLegendRow(PickupInboundColor, "堆垛机交互点 · 只能入库");
            DrawLegendRow(PickupOutboundColor, "堆垛机交互点 · 只能出库");
            DrawLegendRow(OutfeedColor, "出库口");
            DrawLegendRow(ProcessColor, "加工站");
            DrawLegendRow(VerticalColor, "提升机");
            DrawLegendRow(ForwardColor, "路段（箭头为方向）");
            DrawLegendRow(ProcessColor, "加工专用路段");
            DrawLegendRow(SelectedEdgeColor, "选中的路段");
            DrawLegendRow(OneWayPathColor, "路径单向预览（将改为单向）");

            if (!canvasDrag && !_deferApply && _serializedMap.ApplyModifiedProperties())
            {
                EditorUtility.SetDirty(_map);
                _anchorsDirty = true;
                _topologyReady = false;
                _graphReady = false;
            }
        }

        private void DrawNodeInspector()
        {
            var nodesProp = _serializedMap.FindProperty("Nodes");
            var element = nodesProp.GetArrayElementAtIndex(_selectedNode);
            var kindProp = element.FindPropertyRelative("Kind");
            if (Event.current.type == EventType.Layout)
            {
                _inspectorKind = (SimConveyorNodeKind)kindProp.intValue;
            }

            var kind = _inspectorKind;

            EditorGUILayout.LabelField(NodeLabel(_selectedNode), EditorStyles.boldLabel);
            DrawProp(element, "Kind", "节点类型");

            EditorGUILayout.HelpBox(
                "逻辑 ID 用于显示和场景锚点，可以修改。路段记录的是创建时的节点 ID，改逻辑 ID 不会断开连线。",
                MessageType.None);
            DrawProp(element, "LogicalId", "逻辑 ID");
            using (new EditorGUI.DisabledScope(true))
            {
                DrawProp(element, "NodeId", "节点 ID");
            }

            EditorGUILayout.Space(4f);
            switch (kind)
            {
                case SimConveyorNodeKind.InfeedPort:
                    EditorGUILayout.LabelField("入库口", EditorStyles.boldLabel);
                    EditorGUILayout.HelpBox("入库口在输送远端。货物经路段到达堆垛机交互点后，货位由仿真分配。", MessageType.None);
                    DrawProp(element, "InfeedServiceSeconds", "放货服务时间（秒）");
                    break;
                case SimConveyorNodeKind.PickupPoint:
                    EditorGUILayout.LabelField("堆垛机交互点", EditorStyles.boldLabel);
                    EditorGUILayout.HelpBox(
                        "入库时是输送终点，出库时是输送起点。交互点可以落在两列中间。\n伸叉深度是相对巷道中心的货叉伸出距离，正负表示两侧。参考列只用于货位距离和底层排除。",
                        MessageType.None);
                    DrawProp(element, "StackerInteractionMode", "交互模式");
                    DrawProp(element, "StackerId", "堆垛机编号");
                    DrawProp(element, "ForkDepthMeters", "伸叉深度（米）");
                    DrawProp(element, "PickupColumn", "参考列");
                    DrawProp(element, "PickupRow", "交互排");
                    break;
                case SimConveyorNodeKind.OutfeedPort:
                    EditorGUILayout.LabelField("出库口", EditorStyles.boldLabel);
                    EditorGUILayout.HelpBox("出库输送终点。货物完成发运服务后离开系统。", MessageType.None);
                    DrawProp(element, "OutfeedServiceSeconds", "发运服务时间（秒）");
                    break;
                case SimConveyorNodeKind.ProcessStation:
                    EditorGUILayout.LabelField("加工站点", EditorStyles.boldLabel);
                    EditorGUILayout.HelpBox("停留加工时，带相同标签的货物会在此等待服务时间。", MessageType.None);
                    DrawProp(element, "ProcessMode", "加工模式");
                    DrawProp(element, "ProcessTag", "加工标签");
                    DrawProp(element, "ProcessServiceSeconds", "加工服务时间（秒）");
                    break;
                case SimConveyorNodeKind.VerticalTransfer:
                    EditorGUILayout.LabelField("垂直提升机", EditorStyles.boldLabel);
                    EditorGUILayout.HelpBox("各层节点共用提升机组 ID，以便预约同一台设备。目标层留空时用路径下一节点。", MessageType.None);
                    DrawProp(element, "TransferGroupId", "提升机组 ID");
                    DrawProp(element, "TransferSeconds", "提升时间（秒）");
                    DrawProp(element, "TransferTargetLogicalId", "目标层节点");
                    DrawProp(element, "TransferMotion", "移动动画");
                    break;
            }

            EditorGUILayout.Space(4f);
            DrawProp(element, "MaxReservations", "最大排队数");

            EditorGUILayout.Space(6f);
            if (_oneWayPathFrom >= 0 && _oneWayPathFrom != _selectedNode)
            {
                if (GUILayout.Button($"最短路改为单向：{NodeLabel(_oneWayPathFrom)} → {NodeLabel(_selectedNode)}", GUILayout.Height(28f)))
                {
                    ApplyOneWayShortestPath(_oneWayPathFrom, _selectedNode);
                }
            }
            else if (GUILayout.Button("设为路径单向起点", GUILayout.Height(24f)))
            {
                _oneWayPathTool = true;
                _moveTool = false;
                _oneWayPathFrom = _selectedNode;
                SetStatus($"已选起点 {NodeLabel(_selectedNode)}，再点终点即可改单向。");
                Repaint();
            }

            if (GUILayout.Button("聚焦场景锚点"))
            {
                FrameAnchor(_selectedNode);
            }

            if (GUILayout.Button("删除节点", GUILayout.Height(24f)))
            {
                DeleteSelectedNode();
            }
        }

        private void DrawEdgeSelectionPanel()
        {
            EnsureTopology();
            PruneEdgeSelection();
            if (_selectedKeys.Count == 0)
            {
                return;
            }

            if (_selectedKeys.Count == 1 && _segmentByKey.TryGetValue(_selectedKeys[0], out var edge))
            {
                DrawSingleEdgeInspector(edge);
                return;
            }

            var oneWay = 0;
            for (var i = 0; i < _selectedKeys.Count; i++)
            {
                if (_segmentByKey.TryGetValue(_selectedKeys[i], out var seg) && seg.IsOneWay)
                {
                    oneWay++;
                }
            }

            EditorGUILayout.HelpBox(
                $"已选 {_selectedKeys.Count} 条路段，其中单向 {oneWay} 条。可批量反转单向路段或删除。",
                MessageType.Info);
            const int previewMax = 8;
            var shown = 0;
            for (var i = 0; i < _selectedKeys.Count && shown < previewMax; i++)
            {
                if (!_segmentByKey.TryGetValue(_selectedKeys[i], out var seg))
                {
                    continue;
                }

                EditorGUILayout.LabelField("· " + Describe(seg), EditorStyles.miniLabel);
                shown++;
            }

            if (_selectedKeys.Count > previewMax)
            {
                EditorGUILayout.LabelField($"… 另有 {_selectedKeys.Count - previewMax} 条", EditorStyles.miniLabel);
            }

            EditorGUILayout.Space(4f);
            using (new EditorGUI.DisabledScope(oneWay == 0))
            {
                if (GUILayout.Button($"反转单向路段（{oneWay}）", GUILayout.Height(28f)))
                {
                    ReverseSelectedOneWay();
                }
            }

            if (GUILayout.Button($"删除选中路段（{_selectedKeys.Count}）", GUILayout.Height(28f)))
            {
                DeleteSelectedSegments();
            }

            if (GUILayout.Button("清除路段选择"))
            {
                ClearEdgeSelection();
            }
        }

        private void DrawSingleEdgeInspector(Segment edge)
        {
            if (!TryGetNodeId(edge.Lo, out var loId) || !TryGetNodeId(edge.Hi, out var hiId))
            {
                EditorGUILayout.HelpBox("路段端点无效。", MessageType.Warning);
                return;
            }

            var loLabel = NodeLabel(edge.Lo);
            var hiLabel = NodeLabel(edge.Hi);
            EditorGUILayout.HelpBox(
                Describe(edge) + "\n删除会去掉这两个节点之间的全部方向。",
                MessageType.None);

            var flowOptions = new[] { "双向", $"{loLabel} → {hiLabel}", $"{hiLabel} → {loLabel}" };
            var flowMode = ConveyorMapEditorEdgeUtility.GetFlowModePopupIndex(_map, loId, hiId);
            EditorGUI.BeginChangeCheck();
            var newFlow = EditorGUILayout.Popup("通行方向", flowMode, flowOptions);
            if (EditorGUI.EndChangeCheck() && newFlow != flowMode)
            {
                Undo.RecordObject(_map, "修改通行方向");
                ConveyorMapEditorEdgeUtility.ApplySegmentFlowMode(_map, loId, hiId, newFlow);
                EditorUtility.SetDirty(_map);
                _serializedMap.Update();
                _deferApply = true;
                Repaint();
                return;
            }

            var editFrom = flowMode == 2 ? hiId : loId;
            var editTo = flowMode == 2 ? loId : hiId;
            if (!ConveyorMapEditorEdgeUtility.TryFindEdgeIndex(
                    _map,
                    ConveyorMapEditorEdgeUtility.DirectedSegmentKey(editFrom, editTo),
                    out var edgeIndex))
            {
                EditorGUILayout.HelpBox("未找到对应的有向路段。", MessageType.Warning);
                return;
            }

            var edgesProp = _serializedMap.FindProperty("Edges");
            var edgeProp = edgesProp.GetArrayElementAtIndex(edgeIndex);
            var data = _map.Edges[edgeIndex];
            DrawSceneDistanceFor(editFrom, editTo, data);

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(edgeProp.FindPropertyRelative("DistanceMeters"), new GUIContent("距离（米）"));
            EditorGUILayout.PropertyField(
                edgeProp.FindPropertyRelative("SpeedOverrideMetersPerSecond"),
                new GUIContent("速度覆盖（米/秒）"));
            EditorGUILayout.PropertyField(
                edgeProp.FindPropertyRelative("GateWaitSeconds"),
                new GUIContent("开门等待（秒）", "货物到达这段中间门前方的停留点后，等待开门的时间。0 表示没有门。"));
            EditorGUILayout.PropertyField(
                edgeProp.FindPropertyRelative("GateNormalizedPosition"),
                new GUIContent("门位置（0–1）", "沿当前行进方向，靠近 0 为起点侧，靠近 1 为终点侧。填 0 时按路段中点。双向路段会把反向位置对称过去；正向在终点时，反向落在起点侧。"));
            EditorGUILayout.PropertyField(
                edgeProp.FindPropertyRelative("ProcessTag"),
                new GUIContent("加工专用标签", "留空表示普通路段。填写后，只有带相同加工标签的货物才能走这条路段。"));
            if (EditorGUI.EndChangeCheck())
            {
                _serializedMap.ApplyModifiedProperties();
                ConveyorMapEditorEdgeUtility.SyncReverseEdge(_map, _map.Edges[edgeIndex]);
                EditorUtility.SetDirty(_map);
                _serializedMap.Update();
                data = _map.Edges[edgeIndex];
            }

            var speed = ConveyorMapMath.ResolveSpeedMetersPerSecond(_map, data);
            var seconds = _map.GetEdgeTransitSeconds(data);
            var capacity = _map.GetEdgeCapacity(data);
            var gateNote = string.Empty;
            if (ConveyorMapMath.TryGetGateHoldSlot(_map, data, out var gateSlot, out var gateWait))
            {
                var gatePercent = ConveyorMapMath.ResolveGateNormalizedPosition(data) * 100f;
                gateNote =
                    $"\n中间门 = 行进方向 {gatePercent:0.#}% ，在停留点 S{gateSlot} 等待 {gateWait:0.##} s 后放行";
            }

            EditorGUILayout.HelpBox(
                $"输送时长 = {data.DistanceMeters:0.##} m ÷ {speed:0.##} m/s = {seconds:0.##} s\n" +
                $"拥堵容量 = floor({data.DistanceMeters:0.##} ÷ {_map.CargoUnitLengthMeters:0.##}) = {capacity} 件" +
                gateNote,
                MessageType.None);

            using (new EditorGUI.DisabledScope(!edge.IsOneWay))
            {
                if (GUILayout.Button("反转方向"))
                {
                    ReverseSelectedOneWay();
                }
            }

            if (GUILayout.Button("删除路段", GUILayout.Height(24f)))
            {
                DeleteSelectedSegments();
            }
        }

        private void DrawMapParameters()
        {
            EditorGUILayout.LabelField("地图参数", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(
                _serializedMap.FindProperty("DefaultSpeedMetersPerSecond"),
                new GUIContent("默认速度 (m/s)"));
            EditorGUILayout.PropertyField(
                _serializedMap.FindProperty("CargoUnitLengthMeters"),
                new GUIContent("单箱长度 (m)"));
            EditorGUILayout.PropertyField(
                _serializedMap.FindProperty("DefaultEdgeDistanceMeters"),
                new GUIContent("新建路段距离 (m)"));
        }

        private void DrawSceneSection()
        {
            EditorGUILayout.LabelField("场景锚点", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "子物体名称与节点逻辑 ID 一致时，场景俯视会按它们的世界坐标摆放，连线时也会用这段距离。锚点根节点通常是回放组件下的 Nodes。",
                MessageType.None);

            EditorGUI.BeginChangeCheck();
            _sceneAnchorRoot = (Transform)EditorGUILayout.ObjectField("锚点根节点", _sceneAnchorRoot, typeof(Transform), true);
            if (EditorGUI.EndChangeCheck())
            {
                SaveSceneAnchorRoot();
                _anchorsDirty = true;
                if (_sceneView)
                {
                    _fitPending = true;
                }
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("自动查找", GUILayout.Height(22f)))
            {
                var found = ConveyorMapSceneDistanceUtility.TryAutoFindAnchorRoot(_map);
                if (found != null)
                {
                    _sceneAnchorRoot = found;
                    SaveSceneAnchorRoot();
                    _anchorsDirty = true;
                    SetStatus($"已定位锚点根节点：{found.name}");
                }
                else
                {
                    SetStatus("未能自动定位锚点根节点。");
                }
            }

            using (new EditorGUI.DisabledScope(_sceneAnchorRoot == null))
            {
                if (GUILayout.Button("批量写入距离", GUILayout.Height(22f)))
                {
                    ApplyAllEdgeDistancesFromScene();
                }
            }

            EditorGUILayout.EndHorizontal();

            using (new EditorGUI.DisabledScope(_sceneAnchorRoot == null))
            {
                if (GUILayout.Button("按锚点重排画布"))
                {
                    LayoutCanvasFromAnchors();
                }
            }
        }

        private void DrawSceneDistanceFor(string fromId, string toId, SimConveyorMapEdge edge)
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("场景测距", EditorStyles.boldLabel);
            if (_sceneAnchorRoot == null)
            {
                EditorGUILayout.HelpBox("指定锚点根节点后，可以从场景读取这条路段的距离。", MessageType.None);
                return;
            }

            EnsureAnchors();
            var fromLabel = SimEntityNaming.FormatLogicalId(_map, fromId);
            var toLabel = SimEntityNaming.FormatLogicalId(_map, toId);
            if (ConveyorMapSceneDistanceUtility.TryMeasureDistance(_map, _anchors, fromId, toId, out var sceneDistance, out _))
            {
                var delta = sceneDistance - edge.DistanceMeters;
                var deltaText = Mathf.Abs(delta) < 0.005f ? "与当前一致" : $"相差 {delta:+0.##;-0.##} m";
                EditorGUILayout.LabelField("场景距离", $"{sceneDistance:0.##} m（{deltaText}）");
            }
            else
            {
                EditorGUILayout.HelpBox($"场景中找不到 {fromLabel} 或 {toLabel}。", MessageType.Warning);
            }

            if (GUILayout.Button("从场景读取距离", GUILayout.Height(22f)))
            {
                ApplyEdgeDistanceFromScene(fromId, toId);
            }
        }

        private static void DrawProp(SerializedProperty parent, string relativePath, string label)
        {
            var prop = parent.FindPropertyRelative(relativePath);
            if (prop != null)
            {
                EditorGUILayout.PropertyField(prop, new GUIContent(label));
            }
        }

        private void DrawLegendRow(Color color, string label)
        {
            EditorGUILayout.BeginHorizontal();
            var rect = GUILayoutUtility.GetRect(14f, 14f, GUILayout.Width(18f));
            EditorGUI.DrawRect(rect, color);
            EditorGUILayout.LabelField(label);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawCanvas()
        {
            var rect = GUILayoutUtility.GetRect(
                GUIContent.none,
                GUIStyle.none,
                GUILayout.ExpandWidth(true),
                GUILayout.ExpandHeight(true));
            if (rect.width < 8f || rect.height < 8f)
            {
                return;
            }

            _canvasRect = rect;
            if (_fitPending && Event.current.type == EventType.Repaint)
            {
                FitView(_selectedNode >= 0);
                _fitPending = false;
            }

            HandleCanvasInput();
            if (Event.current.type == EventType.Repaint)
            {
                PaintCanvas();
            }
        }

        /// <summary>
        /// 平移、缩放、悬停不走整窗布局。悬停目标没变时不触发重绘。
        /// </summary>
        private bool TryConsumeCanvasPointer()
        {
            var e = Event.current;
            if (e == null)
            {
                return false;
            }

            if (e.type == EventType.MouseLeaveWindow)
            {
                if (_hoverIndex >= 0 || _hasHoverSegment || _oneWayPreviewKeys.Count > 0)
                {
                    _hoverIndex = -1;
                    _hasHoverSegment = false;
                    _hoverSegment = default;
                    UpdateOneWayPreview();
                    Repaint();
                }

                return false;
            }

            if (_canvasRect.width < 8f || _canvasRect.height < 8f)
            {
                return false;
            }

            var local = e.mousePosition - _canvasRect.position;
            var inside = _canvasRect.Contains(e.mousePosition);
            if (e.type == EventType.MouseMove)
            {
                if (!inside)
                {
                    if (_hoverIndex >= 0 || _hasHoverSegment)
                    {
                        _hoverIndex = -1;
                        _hasHoverSegment = false;
                        _hoverSegment = default;
                        Repaint();
                    }

                    return false;
                }

                var previousHover = _hoverIndex;
                var previousKey = _hasHoverSegment ? _hoverSegment.Key : long.MinValue;
                UpdateHover(local);
                var key = _hasHoverSegment ? _hoverSegment.Key : long.MinValue;
                if (previousHover != _hoverIndex || previousKey != key)
                {
                    Repaint();
                }

                e.Use();
                return true;
            }

            if (e.type == EventType.ScrollWheel && inside)
            {
                ZoomAt(local, e.delta.y);
                e.Use();
                Repaint();
                return true;
            }

            if (e.type == EventType.MouseDrag
                && _canvasHotId != 0
                && GUIUtility.hotControl == _canvasHotId)
            {
                ApplyCanvasDrag(e, local);
                e.Use();
                Repaint();
                return true;
            }

            return false;
        }

        private void ApplyCanvasDrag(Event e, Vector2 local)
        {
            _dragCurrentLocal = local;
            if ((_dragCurrentLocal - _dragStartLocal).sqrMagnitude > DragThreshold * DragThreshold)
            {
                _dragMoved = true;
            }

            if (_dragMode == DragMode.Pan)
            {
                _pan += e.delta;
            }
            else if (_dragMode == DragMode.Connect)
            {
                _activeReverse = e.shift && !_connectBidirectional;
                _hoverIndex = HitTestNode(local, GetConnectSnapRadius());
                if (_hoverIndex == _connectFrom)
                {
                    _hoverIndex = -1;
                }
            }
            else if (_dragMode == DragMode.Move && _moveIndex >= 0 && _dragMoved)
            {
                var graph = _moveOriginGraph + LocalDeltaToGraph(local - _moveOriginLocal);
                WriteNodeCenter(_moveIndex, graph);
            }
        }

        private void HandleCanvasInput()
        {
            var e = Event.current;
            var controlId = GUIUtility.GetControlID(CanvasControlHint, FocusType.Keyboard, _canvasRect);
            var local = e.mousePosition - _canvasRect.position;
            var inside = _canvasRect.Contains(e.mousePosition);

            if (e.type == EventType.KeyDown && !EditorGUIUtility.editingTextField)
            {
                if (HandleKeyDown(e))
                {
                    return;
                }
            }

            if (e.type == EventType.MouseDown && inside)
            {
                Focus();
                _canvasHotId = controlId;
                GUIUtility.hotControl = controlId;
                _dragStartLocal = local;
                _dragCurrentLocal = local;
                _dragMoved = false;
                _moveUndoRecorded = false;
                UpdateHover(local);

                var panButton = e.button == 2 || (e.button == 0 && e.alt) || (e.button == 1 && e.alt);
                if (panButton)
                {
                    _dragMode = DragMode.Pan;
                    e.Use();
                    return;
                }

                if (e.button == 1)
                {
                    _dragMode = DragMode.None;
                    e.Use();
                    return;
                }

                if (e.button == 0)
                {
                    var additive = e.shift;
                    if (_hoverIndex >= 0)
                    {
                        ClearEdgeSelection();
                        _selectedNode = _hoverIndex;
                        if (_oneWayPathTool)
                        {
                            HandleOneWayPathClick(_hoverIndex);
                            _dragMode = DragMode.None;
                        }
                        else if (_moveTool && !_sceneView)
                        {
                            _dragMode = DragMode.Move;
                            _moveIndex = _hoverIndex;
                            TryGetGraphPosition(_hoverIndex, out _moveOriginGraph);
                            _moveOriginLocal = local;
                        }
                        else
                        {
                            _dragMode = DragMode.Connect;
                            _connectFrom = _hoverIndex;
                            _activeReverse = e.shift && !_connectBidirectional;
                        }
                    }
                    else if (_showTopology && _hasHoverSegment)
                    {
                        SelectSegment(_hoverSegment.Key, additive);
                        _selectedNode = -1;
                        _connectFrom = -1;
                        _dragMode = DragMode.None;
                    }
                    else
                    {
                        if (!additive)
                        {
                            _selectedNode = -1;
                            ClearEdgeSelection();
                            if (_oneWayPathTool)
                            {
                                ClearOneWayPathPick();
                            }
                        }

                        _connectFrom = -1;
                        _dragMode = DragMode.BoxSelect;
                        _boxSelectAdditive = additive;
                    }

                    e.Use();
                    Repaint();
                }

                return;
            }

            if (_canvasHotId == 0 || GUIUtility.hotControl != _canvasHotId)
            {
                return;
            }

            if (e.type == EventType.MouseUp)
            {
                _dragCurrentLocal = local;
                if (e.button == 1 && !_dragMoved)
                {
                    UpdateHover(local);
                    ShowContextMenu();
                }
                else if (_dragMode == DragMode.Connect)
                {
                    _hoverIndex = HitTestNode(local, GetConnectSnapRadius());
                    if (_hoverIndex == _connectFrom)
                    {
                        _hoverIndex = -1;
                    }

                    FinishConnect();
                }
                else if (_dragMode == DragMode.BoxSelect)
                {
                    FinishBoxSelect();
                }
                else if (_dragMode == DragMode.Move)
                {
                    EditorUtility.SetDirty(_map);
                    _serializedMap?.Update();
                }

                _dragMode = DragMode.None;
                _connectFrom = -1;
                _moveIndex = -1;
                _canvasHotId = 0;
                GUIUtility.hotControl = 0;
                e.Use();
                Repaint();
            }
        }

        private bool HandleKeyDown(Event e)
        {
            if (e.keyCode == KeyCode.Escape)
            {
                _connectFrom = -1;
                if (_oneWayPathFrom >= 0)
                {
                    ClearOneWayPathPick();
                }
                else
                {
                    _selectedNode = -1;
                    ClearEdgeSelection();
                }

                e.Use();
                Repaint();
                return true;
            }

            if (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace)
            {
                if (_hasEdgeSelection)
                {
                    DeleteSelectedSegments();
                    e.Use();
                    return true;
                }
            }

            if (e.keyCode == KeyCode.F)
            {
                FitView(_selectedNode >= 0);
                e.Use();
                Repaint();
                return true;
            }

            if (e.keyCode == KeyCode.Q || (e.keyCode == KeyCode.R && e.shift))
            {
                RotateView(-90f);
                e.Use();
                return true;
            }

            if (e.keyCode == KeyCode.E || e.keyCode == KeyCode.R)
            {
                RotateView(90f);
                e.Use();
                return true;
            }

            if (e.keyCode == KeyCode.Alpha1 || e.keyCode == KeyCode.Keypad1)
            {
                _moveTool = false;
                _oneWayPathTool = false;
                ClearOneWayPathPick();
                e.Use();
                Repaint();
                return true;
            }

            if (!_sceneView && (e.keyCode == KeyCode.Alpha2 || e.keyCode == KeyCode.Keypad2))
            {
                _moveTool = true;
                _oneWayPathTool = false;
                ClearOneWayPathPick();
                e.Use();
                Repaint();
                return true;
            }

            if (e.keyCode == KeyCode.Alpha3 || e.keyCode == KeyCode.Keypad3)
            {
                _moveTool = false;
                _oneWayPathTool = true;
                if (_selectedNode >= 0)
                {
                    _oneWayPathFrom = _selectedNode;
                }

                e.Use();
                Repaint();
                return true;
            }

            return false;
        }

        private void UpdateHover(Vector2 local)
        {
            _hoverIndex = HitTestNode(local, GetNodeRadius() + 4f);
            _hasHoverSegment = false;
            _hoverSegment = default;
            if (_hoverIndex < 0 && _showTopology)
            {
                _hasHoverSegment = TryHitSegment(local, out _hoverSegment);
            }

            UpdateOneWayPreview();
        }

        private void FinishConnect()
        {
            var from = _connectFrom;
            var to = _hoverIndex;
            _connectFrom = -1;
            if (!_dragMoved || from < 0 || to < 0 || from == to)
            {
                return;
            }

            if (_activeReverse)
            {
                CreateLink(to, from, false);
            }
            else
            {
                CreateLink(from, to, _connectBidirectional);
            }
        }

        private void FinishBoxSelect()
        {
            if (!_dragMoved || !_showTopology)
            {
                return;
            }

            var box = GetNormalizedBox(_dragStartLocal, _dragCurrentLocal);
            if (box.width < 2f && box.height < 2f)
            {
                return;
            }

            EnsureTopology();
            if (!_boxSelectAdditive)
            {
                ClearEdgeSelection();
                _selectedNode = -1;
            }

            var hits = 0;
            for (var i = 0; i < _segments.Count; i++)
            {
                var seg = _segments[i];
                if (!SegmentIntersectsBox(seg, box))
                {
                    continue;
                }

                AddSegmentKey(seg.Key);
                hits++;
            }

            if (hits > 0)
            {
                _selectedNode = -1;
                ShowNotification(new GUIContent($"框选路段 {hits} 条"));
            }
        }

        private void PaintCanvas()
        {
            EditorGUI.DrawRect(_canvasRect, CanvasBg);
            GUI.BeginClip(_canvasRect);
            // 线性空间里 Handles 会把亮黄画成深绿。纯色块走 DrawRect；
            // 圆和箭头用白底蒙版乘 GUI.color，和 DrawRect 同一条着色路径。
            DrawGrid();
            if (_showTopology)
            {
                DrawSegments();
            }

            DrawConnectPreview();
            DrawOneWayPathPreview();
            DrawBoxSelectOverlay();
            DrawNodes();
            if (_sceneView || HasViewYaw())
            {
                DrawAxisOverlay();
            }

            if (_sceneView)
            {
                if (_hiddenAnchorCount > 0)
                {
                    GUI.Label(
                        new Rect(10f, _canvasRect.height - 22f, 420f, 18f),
                        $"{_hiddenAnchorCount} 个节点没有同名锚点，场景俯视中不显示",
                        GetAxisStyle());
                }
            }

            GUI.EndClip();
        }

        private void DrawGrid()
        {
            var step = NiceGridStep();
            if (step <= 1e-4f || _canvasRect.width < 8f || _canvasRect.height < 8f)
            {
                return;
            }

            var c0 = LocalToGraph(Vector2.zero);
            var c1 = LocalToGraph(new Vector2(_canvasRect.width, 0f));
            var c2 = LocalToGraph(new Vector2(0f, _canvasRect.height));
            var c3 = LocalToGraph(new Vector2(_canvasRect.width, _canvasRect.height));
            var minX = Mathf.Min(Mathf.Min(c0.x, c1.x), Mathf.Min(c2.x, c3.x));
            var maxX = Mathf.Max(Mathf.Max(c0.x, c1.x), Mathf.Max(c2.x, c3.x));
            var minY = Mathf.Min(Mathf.Min(c0.y, c1.y), Mathf.Min(c2.y, c3.y));
            var maxY = Mathf.Max(Mathf.Max(c0.y, c1.y), Mathf.Max(c2.y, c3.y));
            var x0 = Mathf.Floor(minX / step) * step;
            var y0 = Mathf.Floor(minY / step) * step;
            var guard = 0;
            for (var x = x0; x <= maxX + step * 0.01f && guard < 240; x += step, guard++)
            {
                DrawGuiLine(GraphToLocal(new Vector2(x, minY)), GraphToLocal(new Vector2(x, maxY)), GridColor, 1f);
            }

            guard = 0;
            for (var y = y0; y <= maxY + step * 0.01f && guard < 240; y += step, guard++)
            {
                DrawGuiLine(GraphToLocal(new Vector2(minX, y)), GraphToLocal(new Vector2(maxX, y)), GridColor, 1f);
            }
        }

        private void DrawNodes()
        {
            if (_map.Nodes == null)
            {
                return;
            }

            var radius = GetNodeRadius();
            _hiddenAnchorCount = 0;
            for (var i = 0; i < _map.Nodes.Length; i++)
            {
                if (!TryGetGraphPosition(i, out var graph))
                {
                    _hiddenAnchorCount++;
                    continue;
                }

                var p = GraphToLocal(graph);
                if (!IsLocalVisible(p, ViewCullPadding))
                {
                    continue;
                }

                var selected = i == _selectedNode;
                var hovered = i == _hoverIndex;
                var connectFrom = _dragMode == DragMode.Connect && i == _connectFrom;
                var dropTarget = _dragMode == DragMode.Connect && i == _hoverIndex && i != _connectFrom;
                var oneWayFrom = _oneWayPathTool && i == _oneWayPathFrom;
                var oneWayTo = _oneWayPathTool && _oneWayPathFrom >= 0 && i == _hoverIndex && i != _oneWayPathFrom;
                var fill = NodeColor(in _map.Nodes[i]);
                if (connectFrom || oneWayFrom)
                {
                    fill = new Color(0.18f, 0.55f, 0.28f, 1f);
                }

                if (dropTarget || oneWayTo)
                {
                    fill = new Color(0.95f, 0.75f, 0.2f, 1f);
                }

                var outline = selected || hovered || connectFrom || dropTarget || oneWayFrom || oneWayTo
                    ? Color.white
                    : new Color(0.75f, 0.86f, 0.94f, 0.9f);
                if (dropTarget)
                {
                    DrawGuiDisc(p, radius + 6f, new Color(1f, 0.85f, 0.2f, 0.95f));
                }

                DrawGuiDisc(p, radius + (selected ? 2.5f : 1.25f), outline);
                DrawGuiDisc(p, radius, fill);

                if (_map.Nodes[i].Kind == SimConveyorNodeKind.PickupPoint && !connectFrom && !dropTarget)
                {
                    var markRect = new Rect(p.x - 12f, p.y - 8f, 24f, 16f);
                    GUI.Label(markRect, PickupMarkContent(_map.Nodes[i].StackerInteractionMode), GetLabelStyle());
                }

                if (_showLabels || hovered || selected || connectFrom)
                {
                    var labelRect = new Rect(p.x - 80f, p.y + radius + 2f, 160f, 16f);
                    GUI.Label(labelRect, NodeLabel(i), GetLabelStyle());
                }
            }
        }

        private void DrawSegments()
        {
            EnsureTopology();
            for (var i = 0; i < _segments.Count; i++)
            {
                DrawSegment(_segments[i]);
            }
        }

        private void DrawSegment(Segment seg)
        {
            if (!TryGetGraphPosition(seg.Lo, out var g0) || !TryGetGraphPosition(seg.Hi, out var g1))
            {
                return;
            }

            var a = GraphToLocal(g0);
            var b = GraphToLocal(g1);
            if (!IsSegmentVisible(a, b, 12f))
            {
                return;
            }

            var selected = _selectedKeySet.Contains(seg.Key);
            var hovered = _hasHoverSegment && _hoverSegment.Key == seg.Key;
            var pathPreview = _oneWayPreviewKeys.Contains(seg.Key);
            var emphasize = selected || hovered || pathPreview;
            var chevrons = _showEdgeFlow && emphasize && (hovered || _selectedKeys.Count <= 12);
            var width = selected ? 5.5f : hovered || pathPreview ? 4.2f : 2.4f;
            var color = selected
                ? SelectedEdgeColor
                : pathPreview
                    ? OneWayPathColor
                    : string.IsNullOrEmpty(seg.ProcessTag)
                        ? ForwardColor
                        : ProcessColor;
            if (seg.LoToHi && seg.HiToLo)
            {
                DrawBidirectional(a, b, color, width, emphasize);
            }
            else if (seg.LoToHi)
            {
                DrawDirected(a, b, color, width, emphasize, chevrons);
            }
            else
            {
                DrawDirected(b, a, color, width, emphasize, chevrons);
            }

            if (selected || hovered)
            {
                var mid = (a + b) * 0.5f;
                GUI.Label(new Rect(mid.x - 110f, mid.y - 18f, 220f, 18f), Describe(seg), GetLabelStyle());
            }
        }

        private void DrawDirected(
            Vector2 from,
            Vector2 to,
            Color color,
            float width,
            bool emphasize,
            bool chevrons)
        {
            if (!TryInsetSegment(from, to, out var start, out var end))
            {
                return;
            }

            DrawGuiLine(start, end, color, width);
            DrawArrowHead(start, end, color, emphasize ? 12f : 9f);
            if (chevrons)
            {
                DrawFlowChevrons(start, end, color, 7f);
            }
        }

        private void DrawBidirectional(Vector2 a, Vector2 b, Color color, float width, bool emphasize)
        {
            if (!TryInsetSegment(a, b, out var start, out var end))
            {
                return;
            }

            DrawGuiLine(start, end, color, width);
            var arrow = emphasize ? 12f : 9f;
            DrawArrowHead(start, end, color, arrow);
            DrawArrowHead(end, start, color, arrow);
        }

        /// <summary>
        /// 线段停在节点外缘，箭头露在圆点外面。
        /// </summary>
        private bool TryInsetSegment(Vector2 from, Vector2 to, out Vector2 start, out Vector2 end)
        {
            var inset = GetNodeRadius() + 1.5f;
            return TryInsetSegment(from, to, inset, inset, out start, out end);
        }

        private static bool TryInsetSegment(
            Vector2 from,
            Vector2 to,
            float startInset,
            float endInset,
            out Vector2 start,
            out Vector2 end)
        {
            start = from;
            end = to;
            var delta = to - from;
            var len = delta.magnitude;
            if (len < 1f)
            {
                return false;
            }

            var need = startInset + endInset + 8f;
            if (len <= need)
            {
                var scale = len / need;
                startInset *= scale;
                endInset *= scale;
            }

            var dir = delta / len;
            start = from + dir * startInset;
            end = to - dir * endInset;
            return (end - start).sqrMagnitude > 1f;
        }

        private void DrawConnectPreview()
        {
            if (_dragMode != DragMode.Connect || _connectFrom < 0 || !_dragMoved)
            {
                return;
            }

            if (!TryGetGraphPosition(_connectFrom, out var fromGraph))
            {
                return;
            }

            var from = GraphToLocal(fromGraph);
            var to = _hoverIndex >= 0 && _hoverIndex != _connectFrom && TryGetGraphPosition(_hoverIndex, out var hoverGraph)
                ? GraphToLocal(hoverGraph)
                : _dragCurrentLocal;
            var color = _activeReverse
                ? ReverseColor
                : _connectBidirectional
                    ? new Color(0.55f, 0.9f, 1f, 0.95f)
                    : ForwardColor;

            var snapped = _hoverIndex >= 0 && _hoverIndex != _connectFrom;
            if (!TryInsetSegment(from, to, GetNodeRadius() + 1.5f, snapped ? GetNodeRadius() + 1.5f : 0f, out var start, out var end))
            {
                return;
            }

            DrawGuiLine(start, end, color, 3.2f);
            DrawArrowHead(start, end, color, 10f);
            if (_connectBidirectional && !_activeReverse)
            {
                DrawArrowHead(end, start, color, 10f);
            }
            var tip = _hoverIndex >= 0 && _hoverIndex != _connectFrom
                ? (_connectBidirectional && !_activeReverse
                    ? $"双向 ↔ {NodeLabel(_hoverIndex)}"
                    : _activeReverse
                        ? $"{NodeLabel(_hoverIndex)} → {NodeLabel(_connectFrom)}"
                        : $"{NodeLabel(_connectFrom)} → {NodeLabel(_hoverIndex)}")
                : "拖到目标节点";
            var mid = (from + to) * 0.5f;
            GUI.Label(new Rect(mid.x - 90f, mid.y - 18f, 180f, 18f), tip, GetLabelStyle());
        }

        private void DrawBoxSelectOverlay()
        {
            if (_dragMode != DragMode.BoxSelect || !_dragMoved)
            {
                return;
            }

            var box = GetNormalizedBox(_dragStartLocal, _dragCurrentLocal);
            EditorGUI.DrawRect(box, BoxSelectFill);
            DrawGuiLine(new Vector2(box.xMin, box.yMin), new Vector2(box.xMax, box.yMin), BoxSelectBorder, 1.5f);
            DrawGuiLine(new Vector2(box.xMax, box.yMin), new Vector2(box.xMax, box.yMax), BoxSelectBorder, 1.5f);
            DrawGuiLine(new Vector2(box.xMax, box.yMax), new Vector2(box.xMin, box.yMax), BoxSelectBorder, 1.5f);
            DrawGuiLine(new Vector2(box.xMin, box.yMax), new Vector2(box.xMin, box.yMin), BoxSelectBorder, 1.5f);
        }

        private void DrawAxisOverlay()
        {
            string text;
            if (_sceneView)
            {
                var right = FormatPlanarAxis(InverseRotatePlanar(Vector2.right));
                var up = FormatPlanarAxis(InverseRotatePlanar(Vector2.up));
                text = $"俯视 · 旋转 {_viewYawDegrees:0}° · 屏右={right}  屏上={up}";
            }
            else
            {
                text = $"画布 · 旋转 {_viewYawDegrees:0}°";
            }

            GUI.Label(new Rect(10f, 8f, 420f, 18f), text, GetAxisStyle());
        }

        private void ShowContextMenu()
        {
            var menu = new GenericMenu();
            if (_hoverIndex >= 0)
            {
                var index = _hoverIndex;
                menu.AddItem(new GUIContent($"选中 {NodeLabel(index)}"), false, () =>
                {
                    ClearEdgeSelection();
                    _selectedNode = index;
                    Repaint();
                });
                menu.AddItem(new GUIContent("聚焦场景锚点"), false, () => FrameAnchor(index));
                menu.AddItem(new GUIContent("设为路径单向起点"), false, () =>
                {
                    _oneWayPathTool = true;
                    _moveTool = false;
                    ClearEdgeSelection();
                    _selectedNode = index;
                    _oneWayPathFrom = index;
                    SetStatus($"已选起点 {NodeLabel(index)}，再点终点即可改单向。");
                    Repaint();
                });
                if (_oneWayPathFrom >= 0 && _oneWayPathFrom != index)
                {
                    menu.AddItem(
                        new GUIContent($"最短路改为单向/{NodeLabel(_oneWayPathFrom)} → {NodeLabel(index)}"),
                        false,
                        () => ApplyOneWayShortestPath(_oneWayPathFrom, index));
                }

                menu.AddItem(new GUIContent("删除节点"), false, () =>
                {
                    _selectedNode = index;
                    DeleteSelectedNode();
                });
            }
            else if (_hasHoverSegment && _hoverSegment.IsValid)
            {
                var key = _hoverSegment.Key;
                var text = Describe(_hoverSegment);
                menu.AddItem(new GUIContent($"选中路段 {text}"), false, () => SelectSegment(key, false));
                menu.AddItem(new GUIContent("删除路段"), false, () =>
                {
                    SelectSegment(key, false);
                    DeleteSelectedSegments();
                });
                if (_hoverSegment.IsOneWay)
                {
                    menu.AddItem(new GUIContent("反转方向"), false, () =>
                    {
                        SelectSegment(key, false);
                        ReverseSelectedOneWay();
                    });
                }
            }
            else
            {
                menu.AddItem(new GUIContent("适应窗口"), false, () =>
                {
                    FitView(false);
                    Repaint();
                });
                menu.AddItem(new GUIContent("聚焦选中"), false, () =>
                {
                    FitView(true);
                    Repaint();
                });
            }

            AddViewRotationItems(menu);
            menu.ShowAsContext();
        }

        private void AddViewRotationItems(GenericMenu menu)
        {
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("视图旋转/左转 90°"), false, () => RotateView(-90f));
            menu.AddItem(new GUIContent("视图旋转/右转 90°"), false, () => RotateView(90f));
            if (HasViewYaw())
            {
                menu.AddItem(new GUIContent("视图旋转/复位"), false, () => SetViewYaw(0f));
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("视图旋转/复位"));
            }
        }

        private void AddNode(SimConveyorNodeKind kind)
        {
            Undo.RecordObject(_map, "添加节点");
            var list = _map.Nodes != null
                ? new List<SimConveyorMapNode>(_map.Nodes)
                : new List<SimConveyorMapNode>();
            var logicalId = NewLogicalId(kind, list);
            var node = new SimConveyorMapNode
            {
                NodeId = SimEntityNaming.NewNodeGuid(),
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

            var graph = ViewCenterGraph();
            graph += new Vector2((list.Count % 5) * 28f, (list.Count % 5) * 18f);
            node.EditorPosition = graph - LegacyHalf;
            list.Add(node);
            _map.Nodes = list.ToArray();
            EditorUtility.SetDirty(_map);
            _serializedMap = new SerializedObject(_map);
            _deferApply = true;
            ClearEdgeSelection();
            _selectedNode = list.Count - 1;
            var message = $"已添加{KindLabel(kind)}：{logicalId}";
            if (_sceneView)
            {
                message += "。场景俯视只显示同名锚点，可关闭「场景俯视」查看画布位置。";
            }

            SetStatus(message);
            ShowNotification(new GUIContent($"已添加 {logicalId}"));
            Repaint();
        }

        private void CreateLink(int fromIndex, int toIndex, bool both)
        {
            if (!TryGetNodeId(fromIndex, out var fromId) || !TryGetNodeId(toIndex, out var toId))
            {
                SetStatus("节点缺少内部 ID，无法创建路段。");
                return;
            }

            var needForward = !ConveyorMapEditorEdgeUtility.HasDirectedEdge(_map, fromId, toId);
            var needBack = both && !ConveyorMapEditorEdgeUtility.HasDirectedEdge(_map, toId, fromId);
            SelectSegment(Pack(Math.Min(fromIndex, toIndex), Math.Max(fromIndex, toIndex)), false);
            _selectedNode = -1;
            if (!needForward && !needBack)
            {
                SetStatus("该方向的路段已存在。");
                return;
            }

            Undo.RecordObject(_map, "连线");
            var distance = ResolveNewDistance(fromId, toId);
            var edges = _map.Edges != null
                ? new List<SimConveyorMapEdge>(_map.Edges)
                : new List<SimConveyorMapEdge>();
            if (needForward)
            {
                edges.Add(NewEdge(fromId, toId, distance));
            }

            if (needBack)
            {
                edges.Add(NewEdge(toId, fromId, distance));
            }

            _map.Edges = edges.ToArray();
            EditorUtility.SetDirty(_map);
            _serializedMap?.Update();
            _deferApply = true;
            var fromLabel = NodeLabel(fromIndex);
            var toLabel = NodeLabel(toIndex);
            string text;
            if (needForward && needBack)
            {
                text = $"已连接 {fromLabel} ↔ {toLabel}，{distance:0.##} m";
            }
            else if (needForward)
            {
                text = $"已连接 {fromLabel} → {toLabel}，{distance:0.##} m";
            }
            else
            {
                text = $"已连接 {toLabel} → {fromLabel}，{distance:0.##} m";
            }
            SetStatus(text);
            ShowNotification(new GUIContent(text));
        }

        private static SimConveyorMapEdge NewEdge(string fromId, string toId, float distance) =>
            new SimConveyorMapEdge
            {
                FromNodeId = fromId,
                ToNodeId = toId,
                DistanceMeters = distance,
            };

        private float ResolveNewDistance(string fromId, string toId)
        {
            EnsureAnchors();
            if (_anchors.Count > 0
                && ConveyorMapSceneDistanceUtility.TryMeasureDistance(_map, _anchors, fromId, toId, out var distance, out _))
            {
                return distance;
            }

            return _map.DefaultEdgeDistanceMeters > 0f ? _map.DefaultEdgeDistanceMeters : 4f;
        }

        private void DeleteSelectedNode()
        {
            if (_map.Nodes == null || _selectedNode < 0 || _selectedNode >= _map.Nodes.Length)
            {
                return;
            }

            var label = NodeLabel(_selectedNode);
            Undo.RecordObject(_map, "删除节点");
            var id = _map.Nodes[_selectedNode].NodeId?.Trim();
            var nodes = new List<SimConveyorMapNode>(_map.Nodes);
            nodes.RemoveAt(_selectedNode);
            _map.Nodes = nodes.ToArray();
            if (_map.Edges != null && !string.IsNullOrEmpty(id))
            {
                var edges = new List<SimConveyorMapEdge>();
                for (var i = 0; i < _map.Edges.Length; i++)
                {
                    var edge = _map.Edges[i];
                    if (edge.FromNodeId?.Trim() == id || edge.ToNodeId?.Trim() == id)
                    {
                        continue;
                    }

                    edges.Add(edge);
                }

                _map.Edges = edges.ToArray();
            }

            EditorUtility.SetDirty(_map);
            _serializedMap = new SerializedObject(_map);
            _deferApply = true;
            _selectedNode = -1;
            ClearEdgeSelection();
            SetStatus($"已删除节点 {label}");
            Repaint();
        }

        private void DeleteSelectedSegments()
        {
            if (_selectedKeys.Count == 0 || _map.Edges == null)
            {
                return;
            }

            Undo.RecordObject(_map, _selectedKeys.Count > 1 ? "批量删除路段" : "删除路段");
            var remove = new HashSet<long>(_selectedKeys);
            var kept = new List<SimConveyorMapEdge>(_map.Edges.Length);
            var removedPairs = new HashSet<long>();
            for (var i = 0; i < _map.Edges.Length; i++)
            {
                var edge = _map.Edges[i];
                if (TryResolveEndpoints(edge, out var from, out var to))
                {
                    var lo = Math.Min(from, to);
                    var hi = Math.Max(from, to);
                    var key = Pack(lo, hi);
                    if (remove.Contains(key))
                    {
                        removedPairs.Add(key);
                        continue;
                    }
                }

                kept.Add(edge);
            }

            _map.Edges = kept.ToArray();
            EditorUtility.SetDirty(_map);
            _serializedMap?.Update();
            _deferApply = true;
            ClearEdgeSelection();
            var text = removedPairs.Count > 0
                ? $"已删除路段 {removedPairs.Count} 条"
                : "没有删除任何路段。";
            SetStatus(text);
            ShowNotification(new GUIContent(text));
            Repaint();
        }

        private void ReverseSelectedOneWay()
        {
            if (_map.Edges == null || _selectedKeys.Count == 0)
            {
                return;
            }

            EnsureTopology();
            var targets = new HashSet<long>();
            for (var i = 0; i < _selectedKeys.Count; i++)
            {
                if (_segmentByKey.TryGetValue(_selectedKeys[i], out var seg) && seg.IsOneWay)
                {
                    targets.Add(seg.Key);
                }
            }

            if (targets.Count == 0)
            {
                SetStatus("选中的路段都是双向，没有可反转的单向路段。");
                return;
            }

            Undo.RecordObject(_map, targets.Count > 1 ? "批量反转路段" : "反转路段");
            var edges = _map.Edges;
            var reversed = 0;
            var consumed = new HashSet<long>();
            for (var i = 0; i < edges.Length; i++)
            {
                if (!TryResolveEndpoints(edges[i], out var from, out var to))
                {
                    continue;
                }

                var key = Pack(Math.Min(from, to), Math.Max(from, to));
                if (!targets.Contains(key) || !consumed.Add(key))
                {
                    continue;
                }

                var edge = edges[i];
                var swap = edge.FromNodeId;
                edge.FromNodeId = edge.ToNodeId;
                edge.ToNodeId = swap;
                edges[i] = edge;
                reversed++;
            }

            _map.Edges = edges;
            EditorUtility.SetDirty(_map);
            _serializedMap?.Update();
            _deferApply = true;
            var text = $"已反转 {reversed} 条单向路段";
            SetStatus(text);
            ShowNotification(new GUIContent(text));
            Repaint();
        }

        private void HandleOneWayPathClick(int index)
        {
            if (index < 0)
            {
                return;
            }

            if (_oneWayPathFrom < 0 || _oneWayPathFrom == index)
            {
                _oneWayPathFrom = index;
                UpdateOneWayPreview();
                SetStatus($"已选起点 {NodeLabel(index)}，再点终点即可改单向。");
                return;
            }

            ApplyOneWayShortestPath(_oneWayPathFrom, index);
        }

        private void ApplyOneWayShortestPath(int fromIndex, int toIndex)
        {
            if (_map == null || fromIndex < 0 || toIndex < 0 || fromIndex == toIndex)
            {
                return;
            }

            if (!ConveyorMapEditorEdgeUtility.TryFindUndirectedShortestPath(_map, fromIndex, toIndex, out var path)
                || path == null
                || path.Count < 2)
            {
                SetStatus($"找不到 {NodeLabel(fromIndex)} 到 {NodeLabel(toIndex)} 的通路。");
                ShowNotification(new GUIContent("两点之间没有通路"));
                return;
            }

            Undo.RecordObject(_map, "最短路径改为单向");
            var changed = ConveyorMapEditorEdgeUtility.ApplyOneWayAlongPath(_map, path);
            EditorUtility.SetDirty(_map);
            _serializedMap = new SerializedObject(_map);
            _deferApply = true;
            _topologyReady = false;
            EnsureTopology();
            SelectPathSegments(path);
            _selectedNode = -1;
            _oneWayPathFrom = fromIndex;
            UpdateOneWayPreview();
            var hops = path.Count - 1;
            var text = changed > 0
                ? $"已将 {NodeLabel(fromIndex)} → {NodeLabel(toIndex)} 最短路改为单向（{hops} 段，改写 {changed} 段）"
                : $"最短路 {NodeLabel(fromIndex)} → {NodeLabel(toIndex)} 已经是单向（{hops} 段）";
            SetStatus(text);
            ShowNotification(new GUIContent(text));
            Repaint();
        }

        private void SelectPathSegments(IReadOnlyList<int> path)
        {
            ClearEdgeSelection();
            if (path == null)
            {
                return;
            }

            for (var i = 0; i < path.Count - 1; i++)
            {
                var a = path[i];
                var b = path[i + 1];
                AddSegmentKey(Pack(Math.Min(a, b), Math.Max(a, b)));
            }
        }

        private void UpdateOneWayPreview()
        {
            var to = _oneWayPathTool && _oneWayPathFrom >= 0 && _hoverIndex >= 0 && _hoverIndex != _oneWayPathFrom
                ? _hoverIndex
                : -1;
            if (to == _oneWayPreviewTo
                && _oneWayPreviewTo >= 0
                && _oneWayPreviewPath.Count > 0
                && _oneWayPreviewPath[0] == _oneWayPathFrom)
            {
                return;
            }

            _oneWayPreviewTo = to;
            _oneWayPreviewPath.Clear();
            _oneWayPreviewKeys.Clear();
            _oneWayPreviewReachable = false;
            if (to < 0)
            {
                return;
            }

            if (!ConveyorMapEditorEdgeUtility.TryFindUndirectedShortestPath(_map, _oneWayPathFrom, to, out var path)
                || path == null
                || path.Count < 2)
            {
                return;
            }

            _oneWayPreviewReachable = true;
            _oneWayPreviewPath.AddRange(path);
            for (var i = 0; i < path.Count - 1; i++)
            {
                _oneWayPreviewKeys.Add(Pack(Math.Min(path[i], path[i + 1]), Math.Max(path[i], path[i + 1])));
            }
        }

        private void DrawOneWayPathPreview()
        {
            if (!_oneWayPathTool || _oneWayPathFrom < 0 || _hoverIndex < 0 || _hoverIndex == _oneWayPathFrom)
            {
                return;
            }

            if (!_oneWayPreviewReachable || _oneWayPreviewPath.Count < 2)
            {
                if (TryGetGraphPosition(_oneWayPathFrom, out var fromGraph)
                    && TryGetGraphPosition(_hoverIndex, out var toGraph))
                {
                    var mid = (GraphToLocal(fromGraph) + GraphToLocal(toGraph)) * 0.5f;
                    GUI.Label(
                        new Rect(mid.x - 110f, mid.y - 18f, 220f, 18f),
                        "两点之间没有通路",
                        GetLabelStyle());
                }

                return;
            }

            for (var i = 0; i < _oneWayPreviewPath.Count - 1; i++)
            {
                if (!TryGetGraphPosition(_oneWayPreviewPath[i], out var g0)
                    || !TryGetGraphPosition(_oneWayPreviewPath[i + 1], out var g1))
                {
                    continue;
                }

                var a = GraphToLocal(g0);
                var b = GraphToLocal(g1);
                if (!TryInsetSegment(a, b, out var start, out var end))
                {
                    continue;
                }

                DrawArrowHead(start, end, OneWayPathColor, 11f);
            }

            var fromLabel = NodeLabel(_oneWayPathFrom);
            var toLabel = NodeLabel(_hoverIndex);
            var hops = _oneWayPreviewPath.Count - 1;
            if (TryGetGraphPosition(_hoverIndex, out var hoverGraph))
            {
                var p = GraphToLocal(hoverGraph);
                GUI.Label(
                    new Rect(p.x - 120f, p.y - GetNodeRadius() - 20f, 240f, 18f),
                    $"{fromLabel} → {toLabel} · {hops} 段",
                    GetLabelStyle());
            }
        }

        private void ClearOneWayPathPick()
        {
            _oneWayPathFrom = -1;
            _oneWayPreviewTo = -1;
            _oneWayPreviewReachable = false;
            _oneWayPreviewPath.Clear();
            _oneWayPreviewKeys.Clear();
        }

        private void RunAutoLayout()
        {
            if (_map.Nodes == null || _map.Nodes.Length == 0)
            {
                return;
            }

            Undo.RecordObject(_map, "自动排版");
            var layers = ConveyorMapLayerLayoutUtility.ComputeLayers(_map);
            const float layerGapX = 220f;
            const float nodeGapY = 90f;
            foreach (var kv in layers)
            {
                for (var row = 0; row < kv.Value.Count; row++)
                {
                    var nodeIndex = kv.Value[row];
                    var id = _map.Nodes[nodeIndex].NodeId;
                    _map.SetNodePosition(id, new Vector2(60f + kv.Key * layerGapX, 60f + row * nodeGapY));
                }
            }

            EditorUtility.SetDirty(_map);
            _serializedMap?.Update();
            _deferApply = true;
            _fitPending = true;
            SetStatus("已按拓扑层级排版。");
            Repaint();
        }

        private void LayoutCanvasFromAnchors()
        {
            EnsureAnchors();
            if (_anchors.Count == 0 || _map.Nodes == null)
            {
                SetStatus("没有可用的场景锚点。");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "按锚点重排画布",
                    "用场景锚点的俯视位置覆盖节点画布坐标。场景俯视本身不变。可用 Ctrl+Z 撤销。",
                    "重排",
                    "取消"))
            {
                return;
            }

            var placed = new List<Vector2>(_map.Nodes.Length);
            var indices = new List<int>(_map.Nodes.Length);
            for (var i = 0; i < _map.Nodes.Length; i++)
            {
                var label = SimEntityNaming.FormatLogicalId(_map, i);
                if (!_anchors.TryGetValue(label, out var anchor) || anchor == null)
                {
                    continue;
                }

                var p = anchor.position;
                indices.Add(i);
                placed.Add(new Vector2(p.x, p.z));
            }

            if (placed.Count == 0)
            {
                SetStatus("锚点名称与节点逻辑 ID 对不上。");
                return;
            }

            var median = MedianNearestGap(placed);
            var scale = median > 0.05f ? 140f / median : 40f;
            Undo.RecordObject(_map, "按锚点重排画布");
            var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var graph = new Vector2[placed.Count];
            for (var i = 0; i < placed.Count; i++)
            {
                graph[i] = new Vector2(placed[i].x * scale, -placed[i].y * scale);
                min = Vector2.Min(min, graph[i]);
            }

            var shift = new Vector2(80f, 80f) - min;
            for (var i = 0; i < indices.Count; i++)
            {
                var id = _map.Nodes[indices[i]].NodeId;
                _map.SetNodePosition(id, graph[i] + shift - LegacyHalf);
            }

            EditorUtility.SetDirty(_map);
            _serializedMap?.Update();
            _deferApply = true;
            SetStatus($"已按锚点重排 {indices.Count} 个节点的画布坐标。");
            if (!_sceneView)
            {
                _fitPending = true;
            }

            Repaint();
        }

        private void ApplyEdgeDistanceFromScene(string fromId, string toId)
        {
            if (_sceneAnchorRoot == null)
            {
                SetStatus("未指定场景锚点根节点。");
                return;
            }

            Undo.RecordObject(_map, "从场景读取路段距离");
            if (!ConveyorMapSceneDistanceUtility.TryApplyEdgeDistanceFromScene(
                    _map,
                    _sceneAnchorRoot,
                    fromId,
                    toId,
                    out _,
                    out var message))
            {
                SetStatus(message);
                return;
            }

            EditorUtility.SetDirty(_map);
            _serializedMap?.Update();
            _deferApply = true;
            SetStatus(message);
        }

        private void ApplyAllEdgeDistancesFromScene()
        {
            if (_sceneAnchorRoot == null)
            {
                SetStatus("未指定场景锚点根节点。");
                return;
            }

            Undo.RecordObject(_map, "批量从场景反推路段距离");
            var updated = ConveyorMapSceneDistanceUtility.ApplyAllEdgeDistancesFromScene(
                _map,
                _sceneAnchorRoot,
                out var warnings);
            EditorUtility.SetDirty(_map);
            _serializedMap?.Update();
            _deferApply = true;
            if (updated <= 0)
            {
                SetStatus(warnings.Count > 0 ? warnings[0] : "没有路段被更新。");
                return;
            }

            var summary = $"已从场景更新 {updated} 条路段距离。";
            if (warnings.Count > 0)
            {
                summary += $" {warnings.Count} 条因缺少锚点被跳过。";
            }

            SetStatus(summary);
        }

        private void ValidateMap()
        {
            var fleet = TryResolveFleetDescriptor(_map);
            if (ConveyorMapTopology.TryBuild(_map, fleet, out _, out var error))
            {
                SetStatus("拓扑校验通过。");
                EditorUtility.DisplayDialog("输送地图", "拓扑校验通过。", "确定");
            }
            else
            {
                SetStatus(error);
                EditorUtility.DisplayDialog("输送地图", error, "确定");
            }
        }

        private static IStackerFleetDescriptor TryResolveFleetDescriptor(WarehouseConveyorMap map)
        {
            if (map == null)
            {
                return null;
            }

            var guids = AssetDatabase.FindAssets("t:DefaultWarehouseSimulationBindingsAsset");
            for (var i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var asset = AssetDatabase.LoadAssetAtPath<DefaultWarehouseSimulationBindingsAsset>(path);
                if (asset?.ConveyorMap == map)
                {
                    return asset;
                }
            }

            return null;
        }

        private void FrameAnchor(int index)
        {
            EnsureAnchors();
            var label = NodeLabel(index);
            if (_anchors.TryGetValue(label, out var anchor) && anchor != null)
            {
                Selection.activeGameObject = anchor.gameObject;
                if (SceneView.lastActiveSceneView != null)
                {
                    SceneView.lastActiveSceneView.FrameSelected();
                }

                return;
            }

            SetStatus($"场景中没有名为「{label}」的锚点。");
        }

        private void WriteNodeCenter(int index, Vector2 center)
        {
            if (!TryGetNodeId(index, out var id))
            {
                return;
            }

            if (!_moveUndoRecorded)
            {
                Undo.RecordObject(_map, "移动节点");
                _moveUndoRecorded = true;
            }

            _map.SetNodePosition(id, center - LegacyHalf);
        }

        private void EnsureTopology()
        {
            if (_map == null)
            {
                _topologyReady = false;
                _segments.Clear();
                _segmentByKey.Clear();
                _gatedSegmentKeys.Clear();
                return;
            }

            var stamp = ComputeTopologyStamp();
            if (_topologyReady && stamp == _topologyStamp)
            {
                return;
            }

            _topologyReady = true;
            _topologyStamp = stamp;
            _graphReady = false;
            RebuildNodeCache();
            RebuildSegments();
        }

        private int ComputeTopologyStamp()
        {
            unchecked
            {
                var hash = 17;
                var nodes = _map.Nodes;
                var edges = _map.Edges;
                hash = hash * 31 + (nodes?.Length ?? 0);
                hash = hash * 31 + (edges?.Length ?? 0);
                if (nodes != null)
                {
                    for (var i = 0; i < nodes.Length; i++)
                    {
                        hash = hash * 31 + StableHash(nodes[i].NodeId);
                        hash = hash * 31 + StableHash(nodes[i].LogicalId);
                    }
                }

                if (edges != null)
                {
                    for (var i = 0; i < edges.Length; i++)
                    {
                        hash = hash * 31 + StableHash(edges[i].FromNodeId);
                        hash = hash * 31 + StableHash(edges[i].ToNodeId);
                        hash = hash * 31 + (edges[i].GateWaitSeconds > 1e-4f ? 1 : 0);
                        hash = hash * 31 + StableHash(edges[i].ProcessTag);
                    }
                }

                return hash;
            }
        }

        private static int StableHash(string value) =>
            string.IsNullOrEmpty(value) ? 0 : value.GetHashCode();

        private void RebuildNodeCache()
        {
            _nodeIndexByNodeId.Clear();
            _nodeIndexByLogicalId.Clear();
            var nodes = _map.Nodes;
            var count = nodes?.Length ?? 0;
            if (_nodeLabels.Length != count)
            {
                _nodeLabels = new string[count];
            }

            for (var i = 0; i < count; i++)
            {
                var nodeId = TrimCopy(nodes[i].NodeId);
                var logicalId = TrimCopy(nodes[i].LogicalId);
                if (nodeId.Length > 0 && !_nodeIndexByNodeId.ContainsKey(nodeId))
                {
                    _nodeIndexByNodeId.Add(nodeId, i);
                }

                if (logicalId.Length > 0 && !_nodeIndexByLogicalId.ContainsKey(logicalId))
                {
                    _nodeIndexByLogicalId.Add(logicalId, i);
                }

                _nodeLabels[i] = logicalId.Length > 0 ? logicalId : $"n{i}";
            }
        }

        private void RebuildSegments()
        {
            _segments.Clear();
            _segmentByKey.Clear();
            _gatedSegmentKeys.Clear();
            var edges = _map?.Edges;
            if (edges == null)
            {
                return;
            }

            for (var i = 0; i < edges.Length; i++)
            {
                var edge = edges[i];
                if (!TryResolveCached(edge, out var from, out var to) || from == to)
                {
                    continue;
                }

                var lo = Math.Min(from, to);
                var hi = Math.Max(from, to);
                var key = Pack(lo, hi);
                if (!_segmentByKey.TryGetValue(key, out var seg))
                {
                    seg = new Segment { Lo = lo, Hi = hi };
                }

                if (from == lo)
                {
                    seg.LoToHi = true;
                }
                else
                {
                    seg.HiToLo = true;
                }

                var processTag = edge.ProcessTag?.Trim();
                if (!string.IsNullOrEmpty(processTag))
                {
                    if (string.IsNullOrEmpty(seg.ProcessTag))
                    {
                        seg.ProcessTag = processTag;
                    }
                    else if (seg.ProcessTag.IndexOf(processTag, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        seg.ProcessTag = seg.ProcessTag + "/" + processTag;
                    }
                }

                _segmentByKey[key] = seg;
                if (edge.GateWaitSeconds > 1e-4f)
                {
                    _gatedSegmentKeys.Add(key);
                }
            }

            foreach (var seg in _segmentByKey.Values)
            {
                _segments.Add(seg);
            }
        }

        private bool TryResolveEndpoints(SimConveyorMapEdge edge, out int from, out int to)
        {
            EnsureTopology();
            return TryResolveCached(edge, out from, out to);
        }

        private bool TryResolveCached(SimConveyorMapEdge edge, out int from, out int to)
        {
            from = ResolveNodeIndex(edge.FromNodeId);
            to = ResolveNodeIndex(edge.ToNodeId);
            return from >= 0 && to >= 0;
        }

        private int ResolveNodeIndex(string endpointId)
        {
            var key = TrimCopy(endpointId);
            if (key.Length == 0)
            {
                return -1;
            }

            if (_nodeIndexByNodeId.TryGetValue(key, out var index))
            {
                return index;
            }

            if (_nodeIndexByLogicalId.TryGetValue(key, out index))
            {
                return index;
            }

            return -1;
        }

        private static string TrimCopy(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var start = 0;
            var end = value.Length;
            while (start < end && char.IsWhiteSpace(value[start]))
            {
                start++;
            }

            while (end > start && char.IsWhiteSpace(value[end - 1]))
            {
                end--;
            }

            if (start == 0 && end == value.Length)
            {
                return value;
            }

            return value.Substring(start, end - start);
        }

        private void SelectSegment(long key, bool additive)
        {
            if (!additive)
            {
                ClearEdgeSelection();
            }
            else if (_selectedKeySet.Contains(key))
            {
                RemoveSegmentKey(key);
                Repaint();
                return;
            }

            AddSegmentKey(key);
            _selectedNode = -1;
            Repaint();
        }

        private void AddSegmentKey(long key)
        {
            if (_selectedKeySet.Add(key))
            {
                _selectedKeys.Add(key);
            }
        }

        private void RemoveSegmentKey(long key)
        {
            if (!_selectedKeySet.Remove(key))
            {
                return;
            }

            _selectedKeys.Remove(key);
        }

        private void ClearEdgeSelection()
        {
            _selectedKeys.Clear();
            _selectedKeySet.Clear();
        }

        private void PruneEdgeSelection()
        {
            for (var i = _selectedKeys.Count - 1; i >= 0; i--)
            {
                if (_segmentByKey.ContainsKey(_selectedKeys[i]))
                {
                    continue;
                }

                _selectedKeySet.Remove(_selectedKeys[i]);
                _selectedKeys.RemoveAt(i);
            }
        }

        private bool TryHitSegment(Vector2 local, out Segment hit)
        {
            EnsureTopology();
            EnsureGraphPoints();
            hit = default;
            var best = EdgeHitWidth;
            var found = false;
            for (var i = 0; i < _segments.Count; i++)
            {
                var seg = _segments[i];
                if ((uint)seg.Lo >= (uint)_graphValid.Length
                    || (uint)seg.Hi >= (uint)_graphValid.Length
                    || !_graphValid[seg.Lo]
                    || !_graphValid[seg.Hi])
                {
                    continue;
                }

                var a = GraphToLocal(_graphPoints[seg.Lo]);
                var b = GraphToLocal(_graphPoints[seg.Hi]);
                if (local.x < (a.x < b.x ? a.x : b.x) - best
                    || local.x > (a.x > b.x ? a.x : b.x) + best
                    || local.y < (a.y < b.y ? a.y : b.y) - best
                    || local.y > (a.y > b.y ? a.y : b.y) + best)
                {
                    continue;
                }

                var dist = DistancePointToSegment(local, a, b);
                if (dist >= best)
                {
                    continue;
                }

                best = dist;
                hit = seg;
                found = true;
            }

            return found;
        }

        private bool SegmentIntersectsBox(Segment seg, Rect box)
        {
            if (!TryGetGraphPosition(seg.Lo, out var g0) || !TryGetGraphPosition(seg.Hi, out var g1))
            {
                return false;
            }

            return SegmentIntersectsRect(GraphToLocal(g0), GraphToLocal(g1), box);
        }

        private int HitTestNode(Vector2 local, float maxDist)
        {
            EnsureGraphPoints();
            var best = -1;
            var bestSq = maxDist * maxDist;
            for (var i = 0; i < _graphValid.Length; i++)
            {
                if (!_graphValid[i])
                {
                    continue;
                }

                var delta = GraphToLocal(_graphPoints[i]) - local;
                var distSq = delta.sqrMagnitude;
                if (distSq > bestSq)
                {
                    continue;
                }

                bestSq = distSq;
                best = i;
            }

            return best;
        }

        private bool TryGetGraphPosition(int index, out Vector2 graph)
        {
            EnsureGraphPoints();
            if ((uint)index >= (uint)_graphValid.Length || !_graphValid[index])
            {
                graph = default;
                return false;
            }

            graph = _graphPoints[index];
            return true;
        }

        private void EnsureGraphPoints()
        {
            EnsureTopology();
            var refreshAnchors = _sceneView && _anchorsDirty;
            if (refreshAnchors)
            {
                EnsureAnchors();
            }

            var count = _map?.Nodes?.Length ?? 0;
            var stamp = ComputeGraphStamp();
            if (!refreshAnchors && _graphReady && stamp == _graphStamp && _graphPoints.Length == count)
            {
                return;
            }

            _graphReady = true;
            _graphStamp = stamp;
            if (_graphPoints.Length != count)
            {
                _graphPoints = new Vector2[count];
                _graphValid = new bool[count];
            }

            var nodes = _map?.Nodes;
            for (var i = 0; i < count; i++)
            {
                if (_sceneView)
                {
                    if ((uint)i >= (uint)_nodeLabels.Length)
                    {
                        _graphValid[i] = false;
                        continue;
                    }

                    var label = _nodeLabels[i];
                    if (_anchors.TryGetValue(label, out var anchor) && anchor != null)
                    {
                        var position = anchor.position;
                        _graphPoints[i] = new Vector2(position.x, position.z);
                        _graphValid[i] = true;
                    }
                    else
                    {
                        _graphValid[i] = false;
                    }
                }
                else
                {
                    _graphPoints[i] = nodes[i].EditorPosition + LegacyHalf;
                    _graphValid[i] = true;
                }
            }
        }

        private int ComputeGraphStamp()
        {
            unchecked
            {
                var hash = _sceneView ? 7919 : 104729;
                hash = hash * 31 + _topologyStamp;
                hash = hash * 31 + (_sceneAnchorRoot != null ? _sceneAnchorRoot.GetInstanceID() : 0);
                var nodes = _map?.Nodes;
                var count = nodes?.Length ?? 0;
                hash = hash * 31 + count;
                if (_sceneView)
                {
                    return hash;
                }

                for (var i = 0; i < count; i++)
                {
                    var position = nodes[i].EditorPosition;
                    hash = hash * 31 + position.x.GetHashCode();
                    hash = hash * 31 + position.y.GetHashCode();
                }

                return hash;
            }
        }

        private bool TryGetNodeId(int index, out string id)
        {
            id = null;
            if (_map?.Nodes == null || index < 0 || index >= _map.Nodes.Length)
            {
                return false;
            }

            id = _map.Nodes[index].NodeId?.Trim();
            return !string.IsNullOrEmpty(id);
        }

        private string NodeLabel(int index)
        {
            EnsureTopology();
            if ((uint)index >= (uint)_nodeLabels.Length)
            {
                return "—";
            }

            return _nodeLabels[index];
        }

        private string Describe(Segment seg)
        {
            var lo = NodeLabel(seg.Lo);
            var hi = NodeLabel(seg.Hi);
            var flow = seg.LoToHi && seg.HiToLo
                ? $"{lo} ↔ {hi}"
                : seg.LoToHi ? $"{lo} → {hi}" : $"{hi} → {lo}";
            if (!string.IsNullOrEmpty(seg.ProcessTag))
            {
                flow += " · 加工 " + seg.ProcessTag;
            }

            return HasSegmentGate(seg) ? $"{flow} · 门" : flow;
        }

        private bool HasSegmentGate(Segment seg)
        {
            EnsureTopology();
            return _gatedSegmentKeys.Contains(seg.Key);
        }

        private Vector2 ViewCenterGraph()
        {
            if (_canvasRect.width < 8f || _canvasRect.height < 8f || _sceneView)
            {
                var count = _map.Nodes != null ? _map.Nodes.Length : 0;
                return new Vector2(120f + count * 24f, 120f);
            }

            return LocalToGraph(new Vector2(_canvasRect.width * 0.5f, _canvasRect.height * 0.5f));
        }

        private void EnsureAnchors()
        {
            if (!_anchorsDirty)
            {
                return;
            }

            _anchors.Clear();
            _anchorsDirty = false;
            if (_sceneAnchorRoot == null)
            {
                return;
            }

            if (ConveyorMapSceneDistanceUtility.TryBuildAnchorIndex(_sceneAnchorRoot, out var index, out _))
            {
                foreach (var pair in index)
                {
                    _anchors[pair.Key] = pair.Value;
                }
            }
        }

        private void LoadSceneAnchorRoot()
        {
            var idText = EditorPrefs.GetString(SceneAnchorRootPrefsKey, string.Empty);
            if (string.IsNullOrEmpty(idText) || !GlobalObjectId.TryParse(idText, out var globalId))
            {
                return;
            }

            _sceneAnchorRoot = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(globalId) as Transform;
        }

        private void SaveSceneAnchorRoot()
        {
            _anchorsDirty = true;
            if (_sceneAnchorRoot == null)
            {
                EditorPrefs.DeleteKey(SceneAnchorRootPrefsKey);
                return;
            }

            EditorPrefs.SetString(
                SceneAnchorRootPrefsKey,
                GlobalObjectId.GetGlobalObjectIdSlow(_sceneAnchorRoot).ToString());
        }

        private void SetStatus(string message)
        {
            _statusMessage = message ?? string.Empty;
            Repaint();
        }

        private void FitView(bool focusSelection)
        {
            if (_canvasRect.width < 8f || _canvasRect.height < 8f)
            {
                return;
            }

            var points = new List<Vector2>();
            Vector2 focus = default;
            var hasFocus = false;
            if (focusSelection && _selectedNode >= 0 && TryGetGraphPosition(_selectedNode, out focus))
            {
                hasFocus = true;
            }

            var count = _map.Nodes != null ? _map.Nodes.Length : 0;
            for (var i = 0; i < count; i++)
            {
                if (!TryGetGraphPosition(i, out var graph))
                {
                    continue;
                }

                if (hasFocus)
                {
                    var limit = _sceneView ? 28f : 360f;
                    if (Vector2.Distance(graph, focus) > limit)
                    {
                        continue;
                    }
                }

                points.Add(graph);
            }

            if (points.Count == 0)
            {
                _zoom = _sceneView ? 18f : 1.2f;
                _pan = new Vector2(_canvasRect.width * 0.5f, _canvasRect.height * 0.5f);
                return;
            }

            var viewPoints = new List<Vector2>(points.Count);
            for (var i = 0; i < points.Count; i++)
            {
                viewPoints.Add(ToViewSpace(points[i]));
            }

            GetRobustBounds(viewPoints, out var min, out var max);
            var size = max - min;
            size.x = Mathf.Max(size.x, _sceneView ? 1.5f : 80f);
            size.y = Mathf.Max(size.y, _sceneView ? 1.5f : 48f);
            var zoomX = (_canvasRect.width - FitPadding * 2f) / size.x;
            var zoomY = (_canvasRect.height - FitPadding * 2f) / size.y;
            var fitZoom = Mathf.Min(zoomX, zoomY);
            var medianGap = MedianNearestGap(points);
            var spacingZoom = medianGap > 1e-4f ? TargetNeighborPixels / medianGap : fitZoom;
            var zoom = fitZoom;
            if (medianGap * fitZoom < TargetNeighborPixels * 0.75f)
            {
                zoom = Mathf.Max(fitZoom, spacingZoom);
            }

            _zoom = Mathf.Clamp(zoom, CurrentMinZoom, CurrentMaxZoom);
            var center = hasFocus ? focus : FromViewSpace((min + max) * 0.5f);
            _pan = new Vector2(_canvasRect.width * 0.5f, _canvasRect.height * 0.5f) - GraphToOffset(center);
        }

        private void ZoomAt(Vector2 local, float scrollDelta)
        {
            var graph = LocalToGraph(local);
            var factor = scrollDelta > 0f ? 0.9f : 1.11f;
            _zoom = Mathf.Clamp(_zoom * factor, CurrentMinZoom, CurrentMaxZoom);
            _pan = local - GraphToOffset(graph);
        }

        private void RotateView(float deltaDegrees) => SetViewYaw(_viewYawDegrees + deltaDegrees);

        private void SetViewYaw(float degrees)
        {
            var localCenter = new Vector2(_canvasRect.width * 0.5f, _canvasRect.height * 0.5f);
            var hasCenter = _canvasRect.width > 8f && _canvasRect.height > 8f;
            var graphCenter = hasCenter ? LocalToGraph(localCenter) : Vector2.zero;
            degrees = SnapYaw90(degrees);
            if (Mathf.Abs(Mathf.DeltaAngle(_viewYawDegrees, degrees)) < 0.01f)
            {
                return;
            }

            _viewYawDegrees = degrees;
            EditorPrefs.SetFloat(PrefsViewYaw, _viewYawDegrees);
            if (hasCenter)
            {
                _pan = localCenter - GraphToOffset(graphCenter);
            }

            Repaint();
        }

        private static float SnapYaw90(float degrees)
        {
            var snapped = Mathf.Round(Mathf.Repeat(degrees, 360f) / 90f) * 90f;
            return Mathf.Repeat(snapped, 360f);
        }

        private bool HasViewYaw() => Mathf.Abs(Mathf.DeltaAngle(_viewYawDegrees, 0f)) > 0.5f;

        private float CurrentMinZoom => _sceneView ? SceneMinZoom : MinZoom;

        private float CurrentMaxZoom => _sceneView ? SceneMaxZoom : MaxZoom;

        private Vector2 GraphToLocal(Vector2 graph) => _pan + GraphToOffset(graph);

        private bool IsLocalVisible(Vector2 local, float pad)
        {
            return local.x >= -pad
                   && local.y >= -pad
                   && local.x <= _canvasRect.width + pad
                   && local.y <= _canvasRect.height + pad;
        }

        private bool IsSegmentVisible(Vector2 a, Vector2 b, float pad)
        {
            var minX = a.x < b.x ? a.x : b.x;
            var maxX = a.x > b.x ? a.x : b.x;
            var minY = a.y < b.y ? a.y : b.y;
            var maxY = a.y > b.y ? a.y : b.y;
            return maxX >= -pad
                   && minX <= _canvasRect.width + pad
                   && maxY >= -pad
                   && minY <= _canvasRect.height + pad;
        }

        private Vector2 GraphToOffset(Vector2 graph)
        {
            var view = ToViewSpace(graph);
            return new Vector2(view.x * _zoom, view.y * _zoom);
        }

        private Vector2 LocalToGraph(Vector2 local)
        {
            var z = Mathf.Max(_zoom, 1e-4f);
            return FromViewSpace((local - _pan) / z);
        }

        private Vector2 LocalDeltaToGraph(Vector2 delta)
        {
            var z = Mathf.Max(_zoom, 1e-4f);
            return FromViewSpace(delta / z);
        }

        private Vector2 ToViewSpace(Vector2 graph)
        {
            var view = RotatePlanar(graph);
            if (_sceneView)
            {
                view.y = -view.y;
            }

            return view;
        }

        private Vector2 FromViewSpace(Vector2 view)
        {
            if (_sceneView)
            {
                view.y = -view.y;
            }

            return InverseRotatePlanar(view);
        }

        private Vector2 RotatePlanar(Vector2 v)
        {
            if (Mathf.Abs(_viewYawDegrees) < 1e-4f)
            {
                return v;
            }

            var rad = _viewYawDegrees * Mathf.Deg2Rad;
            var c = Mathf.Cos(rad);
            var s = Mathf.Sin(rad);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        private Vector2 InverseRotatePlanar(Vector2 v)
        {
            if (Mathf.Abs(_viewYawDegrees) < 1e-4f)
            {
                return v;
            }

            var rad = -_viewYawDegrees * Mathf.Deg2Rad;
            var c = Mathf.Cos(rad);
            var s = Mathf.Sin(rad);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        private float GetNodeRadius() =>
            Mathf.Clamp(BaseNodeRadius + Mathf.Log(_zoom + 1f, 2f) * 1.1f, 7f, 16f);

        private float GetConnectSnapRadius() => GetNodeRadius() + ConnectSnapPadding;

        private float NiceGridStep()
        {
            var worldStep = _sceneView ? 1f : 40f;
            var pixel = worldStep * _zoom;
            while (pixel < 28f)
            {
                worldStep *= 2f;
                pixel = worldStep * _zoom;
                if (worldStep > 1e6f)
                {
                    break;
                }
            }

            while (pixel > 96f && worldStep > 1e-3f)
            {
                worldStep *= 0.5f;
                pixel = worldStep * _zoom;
            }

            return Mathf.Max(worldStep, 1e-3f);
        }

        private static Color NodeColor(in SimConveyorMapNode node)
        {
            switch (node.Kind)
            {
                case SimConveyorNodeKind.InfeedPort:
                    return InfeedColor;
                case SimConveyorNodeKind.PickupPoint:
                    if (node.StackerInteractionMode == SimStackerInteractionMode.InboundOnly)
                    {
                        return PickupInboundColor;
                    }

                    if (node.StackerInteractionMode == SimStackerInteractionMode.OutboundOnly)
                    {
                        return PickupOutboundColor;
                    }

                    return PickupBothColor;
                case SimConveyorNodeKind.OutfeedPort:
                    return OutfeedColor;
                case SimConveyorNodeKind.ProcessStation:
                    return ProcessColor;
                case SimConveyorNodeKind.VerticalTransfer:
                    return VerticalColor;
                default:
                    return JunctionColor;
            }
        }

        private static GUIContent PickupMarkContent(SimStackerInteractionMode mode)
        {
            switch (mode)
            {
                case SimStackerInteractionMode.InboundOnly:
                    return InboundMark;
                case SimStackerInteractionMode.OutboundOnly:
                    return OutboundMark;
                default:
                    return BothMark;
            }
        }

        private static string KindLabel(SimConveyorNodeKind kind)
        {
            switch (kind)
            {
                case SimConveyorNodeKind.InfeedPort:
                    return "入库口";
                case SimConveyorNodeKind.PickupPoint:
                    return "堆垛机交互点";
                case SimConveyorNodeKind.OutfeedPort:
                    return "出库口";
                case SimConveyorNodeKind.ProcessStation:
                    return "加工站点";
                case SimConveyorNodeKind.VerticalTransfer:
                    return "垂直提升机";
                default:
                    return "路口";
            }
        }

        private static string NewLogicalId(SimConveyorNodeKind kind, IReadOnlyList<SimConveyorMapNode> existing)
        {
            var used = new HashSet<string>(StringComparer.Ordinal);
            if (existing != null)
            {
                for (var i = 0; i < existing.Count; i++)
                {
                    var id = existing[i].LogicalId?.Trim();
                    if (!string.IsNullOrEmpty(id))
                    {
                        used.Add(id);
                    }
                }
            }

            for (var n = 0; n < 1000; n++)
            {
                var candidate = SimEntityNaming.NewLogicalId(kind, n);
                if (used.Add(candidate))
                {
                    return candidate;
                }
            }

            return SimEntityNaming.NewNodeGuid();
        }

        private static long Pack(int a, int b) => ((long)a << 32) ^ (uint)b;

        private static float PositiveMod(float value, float modulo)
        {
            if (modulo <= 1e-4f)
            {
                return 0f;
            }

            var m = value % modulo;
            return m < 0f ? m + modulo : m;
        }

        private static void GetRobustBounds(List<Vector2> points, out Vector2 min, out Vector2 max)
        {
            min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            if (points.Count == 0)
            {
                min = Vector2.zero;
                max = Vector2.one;
                return;
            }

            if (points.Count <= 4)
            {
                for (var i = 0; i < points.Count; i++)
                {
                    min = Vector2.Min(min, points[i]);
                    max = Vector2.Max(max, points[i]);
                }

                return;
            }

            var centroid = Vector2.zero;
            for (var i = 0; i < points.Count; i++)
            {
                centroid += points[i];
            }

            centroid /= points.Count;
            var distances = new List<float>(points.Count);
            for (var i = 0; i < points.Count; i++)
            {
                distances.Add(Vector2.Distance(points[i], centroid));
            }

            distances.Sort();
            var limit = distances[Mathf.Clamp(Mathf.FloorToInt(distances.Count * 0.9f), 0, distances.Count - 1)];
            limit = Mathf.Max(limit, 1f);
            var used = 0;
            for (var i = 0; i < points.Count; i++)
            {
                if (Vector2.Distance(points[i], centroid) > limit * 1.25f)
                {
                    continue;
                }

                min = Vector2.Min(min, points[i]);
                max = Vector2.Max(max, points[i]);
                used++;
            }

            if (used == 0)
            {
                for (var i = 0; i < points.Count; i++)
                {
                    min = Vector2.Min(min, points[i]);
                    max = Vector2.Max(max, points[i]);
                }
            }
        }

        private static float MedianNearestGap(List<Vector2> points)
        {
            if (points.Count < 2)
            {
                return 2f;
            }

            var gaps = new List<float>(points.Count);
            for (var i = 0; i < points.Count; i++)
            {
                var best = float.PositiveInfinity;
                for (var j = 0; j < points.Count; j++)
                {
                    if (i == j)
                    {
                        continue;
                    }

                    var distance = Vector2.Distance(points[i], points[j]);
                    if (distance < best)
                    {
                        best = distance;
                    }
                }

                if (best < float.PositiveInfinity)
                {
                    gaps.Add(best);
                }
            }

            if (gaps.Count == 0)
            {
                return 2f;
            }

            gaps.Sort();
            return gaps[gaps.Count / 2];
        }

        private static Rect GetNormalizedBox(Vector2 a, Vector2 b)
        {
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        private static bool SegmentIntersectsRect(Vector2 a, Vector2 b, Rect rect)
        {
            if (rect.Contains(a) || rect.Contains(b))
            {
                return true;
            }

            var r0 = new Vector2(rect.xMin, rect.yMin);
            var r1 = new Vector2(rect.xMax, rect.yMin);
            var r2 = new Vector2(rect.xMax, rect.yMax);
            var r3 = new Vector2(rect.xMin, rect.yMax);
            return SegmentsIntersect(a, b, r0, r1)
                   || SegmentsIntersect(a, b, r1, r2)
                   || SegmentsIntersect(a, b, r2, r3)
                   || SegmentsIntersect(a, b, r3, r0);
        }

        private static bool SegmentsIntersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            var d1 = Cross(b - a, c - a);
            var d2 = Cross(b - a, d - a);
            var d3 = Cross(d - c, a - c);
            var d4 = Cross(d - c, b - c);
            if (((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f))
                && ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f)))
            {
                return true;
            }

            return (Mathf.Abs(d1) < 1e-4f && PointOnSegment(c, a, b))
                   || (Mathf.Abs(d2) < 1e-4f && PointOnSegment(d, a, b))
                   || (Mathf.Abs(d3) < 1e-4f && PointOnSegment(a, c, d))
                   || (Mathf.Abs(d4) < 1e-4f && PointOnSegment(b, c, d));
        }

        private static float Cross(Vector2 u, Vector2 v) => u.x * v.y - u.y * v.x;

        private static bool PointOnSegment(Vector2 p, Vector2 a, Vector2 b) =>
            Vector2.Distance(p, a) + Vector2.Distance(p, b) <= Vector2.Distance(a, b) + 1e-3f;

        private static float DistancePointToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            var lenSq = ab.sqrMagnitude;
            if (lenSq < 1e-8f)
            {
                return Vector2.Distance(p, a);
            }

            var t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSq);
            return Vector2.Distance(p, a + ab * t);
        }

        private static string FormatPlanarAxis(Vector2 worldPlanar)
        {
            if (worldPlanar.sqrMagnitude < 1e-8f)
            {
                return "?";
            }

            worldPlanar.Normalize();
            if (Mathf.Abs(worldPlanar.x) >= Mathf.Abs(worldPlanar.y))
            {
                return worldPlanar.x >= 0f ? "+X" : "-X";
            }

            return worldPlanar.y >= 0f ? "+Z" : "-Z";
        }

        private GUIStyle GetLabelStyle()
        {
            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.UpperCenter,
                    fontSize = 10,
                    fontStyle = FontStyle.Bold,
                };
                var textColor = new Color(0.92f, 0.94f, 0.96f, 0.95f);
                _labelStyle.normal.textColor = textColor;
                _labelStyle.hover.textColor = textColor;
                _labelStyle.active.textColor = textColor;
                _labelStyle.focused.textColor = textColor;
            }

            return _labelStyle;
        }

        private GUIStyle GetAxisStyle()
        {
            if (_axisStyle == null)
            {
                _axisStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.UpperLeft,
                    fontSize = 11,
                    fontStyle = FontStyle.Bold,
                };
                _axisStyle.normal.textColor = AxisOverlay;
            }

            return _axisStyle;
        }

        /// <summary>
        /// 白底蒙版乘 GUI.color，和 <see cref="EditorGUI.DrawRect"/> 同一条路径。
        /// 节点颜色不能烘进贴图，线性空间里会偏色。
        /// </summary>
        private static void DrawGuiDisc(Vector2 center, float radius, Color color)
        {
            if (radius <= 0.5f || Event.current.type != EventType.Repaint)
            {
                return;
            }

            var diameter = radius * 2f;
            DrawTinted(
                new Rect(center.x - radius, center.y - radius, diameter, diameter),
                WhiteDiscTexture(),
                color);
        }

        private static void DrawTinted(Rect rect, Texture texture, Color color)
        {
            if (texture == null || Event.current.type != EventType.Repaint)
            {
                return;
            }

            var previous = GUI.color;
            GUI.color *= color;
            GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, true);
            GUI.color = previous;
        }

        private static Texture2D WhiteDiscTexture()
        {
            if (_whiteDisc != null)
            {
                return _whiteDisc;
            }

            _whiteDisc = CreateMask(DiscTextureSize, DiscTextureSize, "ConveyorMapDisc", (x, y, width, height) =>
            {
                var center = (width - 1) * 0.5f;
                var dx = x - center;
                var dy = y - center;
                return center - Mathf.Sqrt(dx * dx + dy * dy) + 0.75f;
            });
            return _whiteDisc;
        }

        private static Texture2D WhiteArrowTexture()
        {
            if (_whiteArrow != null)
            {
                return _whiteArrow;
            }

            _whiteArrow = CreateMask(ArrowTextureWidth, ArrowTextureHeight, "ConveyorMapArrow", (x, y, width, height) =>
            {
                var centerY = (height - 1) * 0.5f;
                var half = centerY * (1f - x / Mathf.Max(1f, width - 1f));
                return half - Mathf.Abs(y - centerY) + 0.75f;
            });
            return _whiteArrow;
        }

        private static Texture2D CreateMask(
            int width,
            int height,
            string textureName,
            Func<int, int, int, int, float> coverage)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, 1, false)
            {
                name = textureName,
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                alphaIsTransparency = true,
            };
            var pixels = new Color32[width * height];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var alpha = Mathf.Clamp01(coverage(x, y, width, height));
                    pixels[y * width + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private static void DrawGuiLine(Vector2 from, Vector2 to, Color color, float width)
        {
            var delta = to - from;
            var length = delta.magnitude;
            if (length < 0.5f || width <= 0f || Event.current.type != EventType.Repaint)
            {
                return;
            }

            var angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            var matrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, from);
            EditorGUI.DrawRect(new Rect(from.x, from.y - width * 0.5f, length, width), color);
            GUI.matrix = matrix;
        }

        private static void DrawArrowHead(Vector2 from, Vector2 to, Color color, float size)
        {
            var delta = to - from;
            if (delta.sqrMagnitude < 1e-4f || Event.current.type != EventType.Repaint)
            {
                return;
            }

            var dir = delta.normalized;
            var length = size;
            var halfWidth = size * 0.45f;
            var angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            var origin = to - dir * length;
            var matrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, origin);
            DrawTinted(
                new Rect(origin.x, origin.y - halfWidth, length, halfWidth * 2f),
                WhiteArrowTexture(),
                color);
            GUI.matrix = matrix;
        }

        private static void DrawFlowChevrons(Vector2 from, Vector2 to, Color color, float size)
        {
            var delta = to - from;
            var len = delta.magnitude;
            if (len < 36f)
            {
                return;
            }

            var dir = delta / len;
            var orth = new Vector2(-dir.y, dir.x);
            var spacing = Mathf.Clamp(len * 0.28f, 28f, 52f);
            var count = Mathf.Clamp(Mathf.FloorToInt((len - 24f) / spacing), 1, 4);
            for (var i = 1; i <= count; i++)
            {
                var t = Mathf.Lerp(0.22f, 0.78f, i / (count + 1f));
                var center = from + delta * t;
                var tip = center + dir * (size * 0.55f);
                var left = center - dir * (size * 0.35f) + orth * (size * 0.45f);
                var right = center - dir * (size * 0.35f) - orth * (size * 0.45f);
                DrawGuiLine(left, tip, color, 2.2f);
                DrawGuiLine(right, tip, color, 2.2f);
            }
        }
    }
}
#endif
