#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using GlobalObjectId = UnityEditor.GlobalObjectId;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Editor
{
    public sealed class ConveyorMapEditorWindow : EditorWindow
    {
        private const float InspectorWidth = 300f;
        private const string SceneAnchorRootPrefsKey = "WarehouseSim.ConveyorMapEditor.SceneAnchorRoot";

        private WarehouseConveyorMap _map;
        private Transform _sceneAnchorRoot;
        private SerializedObject _serializedMap;
        private ConveyorMapGraphView _graphView;
        private IMGUIContainer _toolbarContainer;
        private IMGUIContainer _sidePanelContainer;
        private VisualElement _emptyState;

        private int _selectedNodeIndex = -1;
        private string _selectedSegmentKey;
        private string _statusMessage = string.Empty;

        [MenuItem("Window/Warehouse Simulation/输送地图编辑器")]
        public static void OpenFromMenu()
        {
            var map = Selection.activeObject as WarehouseConveyorMap;
            Open(map);
        }

        public static void Open(WarehouseConveyorMap map)
        {
            var window = GetWindow<ConveyorMapEditorWindow>("输送地图");
            window.minSize = new Vector2(720f, 420f);
            window.SetMap(map);
            window.Show();
        }

        private const string StyleSheetPath = "Assets/CompleteLabs/WarehouseSimulation/Editor/ConveyorMap/ConveyorMapEditor.uss";

        private void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.flexDirection = FlexDirection.Column;

            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);
            if (styleSheet != null)
            {
                root.styleSheets.Add(styleSheet);
            }

            _toolbarContainer = new IMGUIContainer(DrawToolbar);
            _toolbarContainer.style.height = 22;
            root.Add(_toolbarContainer);

            var body = new VisualElement { style = { flexGrow = 1, flexDirection = FlexDirection.Row } };
            root.Add(body);

            _graphView = new ConveyorMapGraphView(this);
            if (styleSheet != null)
            {
                _graphView.styleSheets.Add(styleSheet);
            }

            body.Add(_graphView);

            _sidePanelContainer = new IMGUIContainer(DrawSidePanel);
            _sidePanelContainer.style.width = InspectorWidth;
            _sidePanelContainer.style.flexShrink = 0;
            body.Add(_sidePanelContainer);

            _emptyState = new VisualElement
            {
                style =
                {
                    position = Position.Absolute,
                    left = 0,
                    right = InspectorWidth,
                    top = 22,
                    bottom = 0,
                    alignItems = Align.Center,
                    justifyContent = Justify.Center,
                    backgroundColor = new Color(0.15f, 0.15f, 0.15f, 0.92f),
                },
            };
            var hint = new Label("请选择或创建一个输送地图资源。");
            hint.style.fontSize = 13;
            hint.style.color = new Color(0.85f, 0.85f, 0.85f);
            _emptyState.Add(hint);
            root.Add(_emptyState);

            UpdateEmptyState();
            if (_map != null)
            {
                _graphView.BindMap(_map);
            }
        }

        private void OnEnable()
        {
            Undo.undoRedoPerformed += OnUndoRedo;
            LoadSceneAnchorRoot();
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
        }

        private void OnUndoRedo()
        {
            if (_map != null && _graphView != null)
            {
                _serializedMap = new SerializedObject(_map);
                _graphView.BindMap(_map);
                RepaintSidePanel();
            }
        }

        private void OnSelectionChange()
        {
            if (Selection.activeObject is WarehouseConveyorMap map)
            {
                SetMap(map);
            }
        }

        private void SetMap(WarehouseConveyorMap map)
        {
            _map = map;
            // 不在加载时强制补齐反向边，以保留用户配置的单向路段、
            _serializedMap = _map != null ? new SerializedObject(_map) : null;
            _selectedNodeIndex = -1;
            _selectedSegmentKey = null;
            _statusMessage = string.Empty;

            if (_graphView != null)
            {
                _graphView.BindMap(_map);
            }

            UpdateEmptyState();
            RepaintSidePanel();
        }

        private void UpdateEmptyState()
        {
            if (_emptyState == null)
            {
                return;
            }

            _emptyState.style.display = _map == null ? DisplayStyle.Flex : DisplayStyle.None;
            if (_graphView != null)
            {
                _graphView.style.display = _map == null ? DisplayStyle.None : DisplayStyle.Flex;
            }
        }

        public void OnGraphNodeSelected(int nodeIndex)
        {
            _selectedNodeIndex = nodeIndex;
            _selectedSegmentKey = null;
            RepaintSidePanel();
        }

        public void OnGraphSegmentSelected(string segmentKey)
        {
            _selectedSegmentKey = segmentKey;
            _selectedNodeIndex = -1;
            HighlightSelectedSegment();
            RepaintSidePanel();
        }

        public void OnGraphSelectionCleared()
        {
            _selectedNodeIndex = -1;
            _selectedSegmentKey = null;
            HighlightSelectedSegment();
            RepaintSidePanel();
        }

        public void SetStatusMessage(string message)
        {
            _statusMessage = message ?? string.Empty;
            RepaintSidePanel();
        }

        private void HighlightSelectedSegment()
        {
            if (_graphView == null)
            {
                return;
            }

            foreach (var edge in _graphView.edges)
            {
                if (edge is ConveyorMapSegmentEdge segmentEdge)
                {
                    segmentEdge.SetSelectedVisual(segmentEdge.SegmentKey == _selectedSegmentKey);
                }
            }
        }

        private void RepaintSidePanel()
        {
            _sidePanelContainer?.MarkDirtyRepaint();
            _toolbarContainer?.MarkDirtyRepaint();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            var newMap = EditorGUILayout.ObjectField(_map, typeof(WarehouseConveyorMap), false, GUILayout.Width(220))
                as WarehouseConveyorMap;
            if (newMap != _map)
            {
                SetMap(newMap);
            }

            GUI.enabled = _map != null;

            if (GUILayout.Button("入库口", EditorStyles.toolbarButton, GUILayout.Width(52)))
            {
                _graphView?.AddNodeOfKind(SimConveyorNodeKind.InfeedPort);
            }

            if (GUILayout.Button("路口", EditorStyles.toolbarButton, GUILayout.Width(44)))
            {
                _graphView?.AddNodeOfKind(SimConveyorNodeKind.Junction);
            }

            if (GUILayout.Button("堆垛机交互点", EditorStyles.toolbarButton, GUILayout.Width(88)))
            {
                _graphView?.AddNodeOfKind(SimConveyorNodeKind.PickupPoint);
            }

            if (GUILayout.Button("出库口", EditorStyles.toolbarButton, GUILayout.Width(52)))
            {
                _graphView?.AddNodeOfKind(SimConveyorNodeKind.OutfeedPort);
            }

            GUILayout.Space(6);

            if (GUILayout.Button("自动排版", EditorStyles.toolbarButton, GUILayout.Width(64)))
            {
                _graphView?.AutoLayout();
            }

            if (GUILayout.Button("框选全部", EditorStyles.toolbarButton, GUILayout.Width(64)))
            {
                _graphView?.FrameAllNodes();
            }

            GUILayout.FlexibleSpace();

            if (_serializedMap != null)
            {
                EditorGUILayout.PropertyField(_serializedMap.FindProperty("DefaultSpeedMetersPerSecond"), GUILayout.Width(160));
                EditorGUILayout.PropertyField(_serializedMap.FindProperty("CargoUnitLengthMeters"), GUILayout.Width(140));
                EditorGUILayout.PropertyField(_serializedMap.FindProperty("DefaultEdgeDistanceMeters"), GUILayout.Width(180));
            }

            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSidePanel()
        {
            if (_map == null)
            {
                EditorGUILayout.HelpBox("请选择或创建一个 WarehouseConveyorMap 资源。", MessageType.Info);
                _map = EditorGUILayout.ObjectField("地图资源", _map, typeof(WarehouseConveyorMap), false) as WarehouseConveyorMap;
                if (_map != null)
                {
                    SetMap(_map);
                }

                return;
            }

            if (_serializedMap == null || _serializedMap.targetObject != _map)
            {
                _serializedMap = new SerializedObject(_map);
            }

            _serializedMap.Update();

            DrawSceneDistanceSection();
            EditorGUILayout.Space(6);

            if (!string.IsNullOrEmpty(_selectedSegmentKey))
            {
                DrawSegmentInspector();
            }
            else
            {
                EditorGUILayout.LabelField("节点属性", EditorStyles.boldLabel);

                if (_selectedNodeIndex < 0 || _map.Nodes == null || _selectedNodeIndex >= _map.Nodes.Length)
                {
                    EditorGUILayout.HelpBox(
                        "右键从节点拖向另一节点（或按住 Alt 左键拖动）创建有向路段，类似动画状态机过渡。\n" +
                        "节点本身即连接点，无左右连线端口；通行方向在选中路段的侧栏配置。\n" +
                        "节点类型（入库口 / 路口 / 交互点等）仅影响仿真参数，不限制连线方向。\n" +
                        "堆垛机交互点可按「交互模式」限制仅入库、仅出库或出入库皆可。", MessageType.Info);
                }
                else
                {
                    var nodesProp = _serializedMap.FindProperty("Nodes");
                    var element = nodesProp.GetArrayElementAtIndex(_selectedNodeIndex);
                    DrawNodeInspector(element);

                    EditorGUILayout.Space(6);
                    if (GUILayout.Button("删除节点", GUILayout.Height(24)))
                    {
                        _graphView?.DeleteNode(_selectedNodeIndex);
                    }
                }
            }

            EditorGUILayout.Space(8);
            if (GUILayout.Button("校验拓扑", GUILayout.Height(28)))
            {
                ValidateMap();
            }

            if (!string.IsNullOrEmpty(_statusMessage))
            {
                EditorGUILayout.Space(6);
                EditorGUILayout.HelpBox(_statusMessage, MessageType.None);
            }

            if (_serializedMap.ApplyModifiedProperties())
            {
                EditorUtility.SetDirty(_map);
                if (_selectedNodeIndex >= 0
                    && _map.Nodes != null
                    && _selectedNodeIndex < _map.Nodes.Length)
                {
                    _graphView?.FindNodeView(_selectedNodeIndex)
                        ?.RefreshDisplay(in _map.Nodes[_selectedNodeIndex]);
                }
                else
                {
                    _graphView?.RebuildGraph();
                }

                HighlightSelectedSegment();
            }
        }

        private static void DrawNodeInspector(SerializedProperty element)
        {
            var kind = (SimConveyorNodeKind)element.FindPropertyRelative("Kind").intValue;

            EditorGUILayout.HelpBox(
                "逻辑 ID 用于地图显示与场景锚点，可自由修改。连线记录的是创建时分配的内部节点 ID，修改逻辑 ID 不会断开连线。",
                MessageType.None);

            EditorGUILayout.LabelField("逻辑 ID", EditorStyles.boldLabel);
            var logicalProp = element.FindPropertyRelative("LogicalId");
            if (logicalProp != null)
            {
                EditorGUILayout.PropertyField(logicalProp, GUIContent.none);
            }
            else
            {
                EditorGUILayout.HelpBox("逻辑 ID 字段未就绪，请等待 Unity 完成脚本编译后重开地图编辑器。", MessageType.Warning);
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("节点类型", KindLabel(kind));

            var nodeIdProp = element.FindPropertyRelative("NodeId");
            if (nodeIdProp != null)
            {
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.PropertyField(nodeIdProp, new GUIContent("节点 ID（创建后不可改）"));
                EditorGUI.EndDisabledGroup();
            }

            EditorGUILayout.Space(6);

            switch (kind)
            {
                case SimConveyorNodeKind.InfeedPort:
                    EditorGUILayout.LabelField("入库口参数", EditorStyles.boldLabel);
                    EditorGUILayout.HelpBox(
                        "入库口位于输送线远端，与货架网格无坐标对应；货物经输送边到达堆垛机交互点，货位/堆垛机由仿真按负载动态分配。", MessageType.None);
                    DrawRelativeProperty(element, "InfeedServiceSeconds");
                    break;

                case SimConveyorNodeKind.PickupPoint:
                    EditorGUILayout.LabelField("堆垛机交互点参数", EditorStyles.boldLabel);
                    EditorGUILayout.HelpBox(
                        "位于堆垛机旁货位列（非巷道空隙）；入库时为输送终点，出库时为输送起点。\n" +
                        "单向堆垛机：每台通常 1 个交互点，交互列须在其伸叉列域内。\n" +
                        "双向堆垛机：每台最多 2 个交互点（左右各一，交互列不可重复）。\n" +
                        "画布颜色：蓝（出入库皆可）、蓝绿（仅入库）、蓝紫（仅出库）。", MessageType.None);
                    DrawRelativeProperty(element, "StackerInteractionMode");
                    DrawRelativeProperty(element, "StackerId");
                    DrawRelativeProperty(element, "PickupColumn");
                    DrawRelativeProperty(element, "PickupRow");
                    break;

                case SimConveyorNodeKind.OutfeedPort:
                    EditorGUILayout.LabelField("出库口参数", EditorStyles.boldLabel);
                    EditorGUILayout.HelpBox(
                        "出库流程输送终点；货物在出库口完成发运服务后离开系统。", MessageType.None);
                    DrawRelativeProperty(element, "OutfeedServiceSeconds");
                    break;
            }
        }

        private static void DrawRelativeProperty(SerializedProperty parent, string relativePath)
        {
            var prop = parent.FindPropertyRelative(relativePath);
            if (prop != null)
            {
                EditorGUILayout.PropertyField(prop);
            }
        }

        private void DrawSegmentInspector()
        {
            if (!ConveyorMapEditorEdgeUtility.TryFindEdgeIndex(_map, _selectedSegmentKey, out var edgeIndex))
            {
                EditorGUILayout.HelpBox("未找到选中的路段。", MessageType.Warning);
                return;
            }

            EditorGUILayout.LabelField("路段属性", EditorStyles.boldLabel);
            var edgesProp = _serializedMap.FindProperty("Edges");
            var edgeProp = edgesProp.GetArrayElementAtIndex(edgeIndex);
            var edge = _map.Edges[edgeIndex];

            var fromId = edge.FromNodeId?.Trim();
            var toId = edge.ToNodeId?.Trim();
            var bidirectional = ConveyorMapEditorEdgeUtility.IsBidirectionalSegment(_map, fromId, toId);

            var fromLabel = SimEntityNaming.FormatLogicalId(_map, fromId);
            var toLabel = SimEntityNaming.FormatLogicalId(_map, toId);
            EditorGUILayout.LabelField("端点", $"{fromLabel} → {toLabel}");
            if (bidirectional)
            {
                EditorGUILayout.LabelField("反向路段", $"{toLabel} → {fromLabel}（已配置）");
            }

            var flowOptions = new[] { "双向", $"单向（{fromLabel} → {toLabel}）", $"单向（{toLabel} → {fromLabel}）" };
            var flowMode = ConveyorMapEditorEdgeUtility.GetFlowModePopupIndex(_map, fromId, toId);
            EditorGUI.BeginChangeCheck();
            var newFlowMode = EditorGUILayout.Popup("通行方向", flowMode, flowOptions);
            if (EditorGUI.EndChangeCheck() && newFlowMode != flowMode)
            {
                ConveyorMapEditorEdgeUtility.ApplySegmentFlowMode(_map, fromId, toId, newFlowMode);
                var selectedFrom = newFlowMode == 2 ? toId : fromId;
                var selectedTo = newFlowMode == 2 ? fromId : toId;
                _selectedSegmentKey = ConveyorMapEditorEdgeUtility.DirectedSegmentKey(selectedFrom, selectedTo);
                EditorUtility.SetDirty(_map);
                _serializedMap.Update();
                _graphView?.RebuildGraph();
                HighlightSelectedSegment();
            }

            DrawSceneDistanceForSegment(fromId, toId, edge);

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(edgeProp.FindPropertyRelative("DistanceMeters"));
            EditorGUILayout.PropertyField(edgeProp.FindPropertyRelative("SpeedOverrideMetersPerSecond"));

            if (EditorGUI.EndChangeCheck())
            {
                _serializedMap.ApplyModifiedProperties();
                ConveyorMapEditorEdgeUtility.SyncReverseEdge(_map, _map.Edges[edgeIndex]);
                EditorUtility.SetDirty(_map);
                _graphView?.RebuildGraph();
                HighlightSelectedSegment();
            }

            var cap = _map.GetEdgeCapacity(edge);
            var sec = _map.GetEdgeTransitSeconds(edge);
            var speed = ConveyorMapMath.ResolveSpeedMetersPerSecond(_map, edge);
            EditorGUILayout.HelpBox(
                $"输送时长 = {edge.DistanceMeters:0.##}m ÷ {speed:0.##}m/s = {sec:0.##}s\n" +
                $"拥堵容量 = floor({edge.DistanceMeters:0.##} ÷ {_map.CargoUnitLengthMeters:0.##}) = {cap} 件",
                MessageType.None);

            if (GUILayout.Button("删除路段", GUILayout.Height(24)))
            {
                RemoveSegment(_map, _selectedSegmentKey);
                _selectedSegmentKey = null;
                Save();
                _graphView?.RebuildGraph();
            }
        }

        private void DrawSceneDistanceSection()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("场景距离反推", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "将场景中锚点子物体的名称与节点逻辑 ID 对齐后，可测量两锚点世界距离（米）并写回路段。\n" +
                "锚点根节点通常为回放组件下的「Nodes」。",
                MessageType.None);

            EditorGUI.BeginChangeCheck();
            _sceneAnchorRoot = EditorGUILayout.ObjectField(
                "锚点根节点",
                _sceneAnchorRoot,
                typeof(Transform),
                true) as Transform;
            if (EditorGUI.EndChangeCheck())
            {
                SaveSceneAnchorRoot();
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("自动查找", GUILayout.Height(22)))
            {
                var found = ConveyorMapSceneDistanceUtility.TryAutoFindAnchorRoot(_map);
                if (found != null)
                {
                    _sceneAnchorRoot = found;
                    SaveSceneAnchorRoot();
                    SetStatusMessage($"已自动定位锚点根节点：{found.name}");
                }
                else
                {
                    SetStatusMessage("未能自动定位锚点根节点，请手动指定。");
                }
            }

            GUI.enabled = _sceneAnchorRoot != null;
            if (GUILayout.Button("批量反推全部路段", GUILayout.Height(22)))
            {
                ApplyAllEdgeDistancesFromScene();
            }

            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSceneDistanceForSegment(string fromId, string toId, SimConveyorMapEdge edge)
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("场景测距", EditorStyles.boldLabel);

            if (_sceneAnchorRoot == null)
            {
                EditorGUILayout.HelpBox("请先在侧栏顶部指定场景锚点根节点。", MessageType.Info);
                return;
            }

            if (!ConveyorMapSceneDistanceUtility.TryBuildAnchorIndex(
                    _sceneAnchorRoot,
                    out var anchors,
                    out var indexError))
            {
                EditorGUILayout.HelpBox(indexError, MessageType.Warning);
                return;
            }

            var fromLabel = SimEntityNaming.FormatLogicalId(_map, fromId);
            var toLabel = SimEntityNaming.FormatLogicalId(_map, toId);
            if (ConveyorMapSceneDistanceUtility.TryMeasureDistance(
                    _map,
                    anchors,
                    fromId,
                    toId,
                    out var sceneDistance,
                    out _))
            {
                var delta = sceneDistance - edge.DistanceMeters;
                var deltaText = Mathf.Abs(delta) < 0.005f
                    ? "与当前配置一致"
                    : $"与当前差 {delta:+0.##;-0.##} m";
                EditorGUILayout.LabelField("场景距离", $"{sceneDistance:0.##} m（{deltaText}）");
            }
            else
            {
                EditorGUILayout.HelpBox(
                    $"无法在场景中找到 {fromLabel} 或 {toLabel} 的锚点。",
                    MessageType.Warning);
            }

            GUI.enabled = ConveyorMapSceneDistanceUtility.TryMeasureDistance(
                _map,
                anchors,
                fromId,
                toId,
                out _,
                out _);
            if (GUILayout.Button("从场景读取距离", GUILayout.Height(24)))
            {
                ApplyEdgeDistanceFromScene(fromId, toId);
            }

            GUI.enabled = true;
        }

        private void ApplyEdgeDistanceFromScene(string fromId, string toId)
        {
            if (_sceneAnchorRoot == null)
            {
                SetStatusMessage("未指定场景锚点根节点。");
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
                SetStatusMessage(message);
                return;
            }

            EditorUtility.SetDirty(_map);
            _serializedMap?.Update();
            _graphView?.RebuildGraph();
            HighlightSelectedSegment();
            SetStatusMessage(message);
        }

        private void ApplyAllEdgeDistancesFromScene()
        {
            if (_sceneAnchorRoot == null)
            {
                SetStatusMessage("未指定场景锚点根节点。");
                return;
            }

            Undo.RecordObject(_map, "批量从场景反推路段距离");
            var updatedCount = ConveyorMapSceneDistanceUtility.ApplyAllEdgeDistancesFromScene(
                _map,
                _sceneAnchorRoot,
                out var warnings);
            EditorUtility.SetDirty(_map);
            _serializedMap?.Update();
            _graphView?.RebuildGraph();
            HighlightSelectedSegment();

            if (updatedCount <= 0)
            {
                var detail = warnings.Count > 0 ? warnings[0] : "没有路段被更新。";
                SetStatusMessage(detail);
                return;
            }

            var summary = $"已从场景更新 {updatedCount} 条路段距离。";
            if (warnings.Count > 0)
            {
                summary += $" {warnings.Count} 条跳过（缺少锚点）。";
            }

            SetStatusMessage(summary);
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
            if (_sceneAnchorRoot == null)
            {
                EditorPrefs.DeleteKey(SceneAnchorRootPrefsKey);
                return;
            }

            EditorPrefs.SetString(
                SceneAnchorRootPrefsKey,
                GlobalObjectId.GetGlobalObjectIdSlow(_sceneAnchorRoot).ToString());
        }

        private void ValidateMap()
        {
            var fleet = TryResolveFleetDescriptor(_map);
            if (ConveyorMapTopology.TryBuild(_map, fleet, out _, out var error))
            {
                SetStatusMessage("拓扑校验通过。");
                EditorUtility.DisplayDialog("输送地图", "拓扑校验通过。", "确定");
            }
            else
            {
                SetStatusMessage(error);
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

        private void Save()
        {
            EditorUtility.SetDirty(_map);
            _serializedMap = new SerializedObject(_map);
            _serializedMap.Update();
            AssetDatabase.SaveAssets();
        }

        internal static bool HasEdgeBetween(List<SimConveyorMapEdge> edges, string a, string b)
        {
            foreach (var e in edges)
            {
                if (e.FromNodeId == a && e.ToNodeId == b)
                {
                    return true;
                }
            }

            return false;
        }

        internal static void RemoveSegment(WarehouseConveyorMap map, string segmentKey)
        {
            if (map.Edges == null
                || !ConveyorMapEditorEdgeUtility.TryParseDirectedSegmentKey(segmentKey, out var from, out var to))
            {
                return;
            }

            var edges = new List<SimConveyorMapEdge>();
            foreach (var e in map.Edges)
            {
                if (e.FromNodeId?.Trim() == from && e.ToNodeId?.Trim() == to)
                {
                    continue;
                }

                edges.Add(e);
            }

            map.Edges = edges.ToArray();
            EditorUtility.SetDirty(map);
        }

        internal static void DeleteNodeAtIndex(WarehouseConveyorMap map, int index)
        {
            if (map.Nodes == null || index < 0 || index >= map.Nodes.Length)
            {
                return;
            }

            var id = map.Nodes[index].NodeId?.Trim();
            var nodes = new List<SimConveyorMapNode>(map.Nodes);
            nodes.RemoveAt(index);
            map.Nodes = nodes.ToArray();

            if (map.Edges != null && !string.IsNullOrEmpty(id))
            {
                var edges = new List<SimConveyorMapEdge>();
                foreach (var e in map.Edges)
                {
                    if (e.FromNodeId == id || e.ToNodeId == id)
                    {
                        continue;
                    }

                    edges.Add(e);
                }

                map.Edges = edges.ToArray();
            }

        }

        internal static string GenerateNodeGuid() => SimEntityNaming.NewNodeGuid();

        internal static string GenerateLogicalId(
            SimConveyorNodeKind kind,
            System.Collections.Generic.IReadOnlyList<SimConveyorMapNode> existing)
        {
            var count = 0;
            if (existing != null)
            {
                for (var i = 0; i < existing.Count; i++)
                {
                    if (existing[i].Kind == kind)
                    {
                        count++;
                    }
                }
            }

            return SimEntityNaming.NewLogicalId(kind, count);
        }

        internal static string KindLabel(SimConveyorNodeKind kind) => kind switch
        {
            SimConveyorNodeKind.InfeedPort => "入库口",
            SimConveyorNodeKind.PickupPoint => "堆垛机交互点",
            SimConveyorNodeKind.OutfeedPort => "出库口",
            _ => "路口",
        };

        internal static void RunAutoLayout(WarehouseConveyorMap map)
        {
            if (map.Nodes == null || map.Nodes.Length == 0)
            {
                return;
            }

            var idToIndex = new Dictionary<string, int>();
            for (var i = 0; i < map.Nodes.Length; i++)
            {
                var id = map.Nodes[i].NodeId?.Trim();
                if (!string.IsNullOrEmpty(id))
                {
                    idToIndex[id] = i;
                }
            }

            var layers = new Dictionary<int, List<int>>();
            var n = map.Nodes.Length;
            var inDegree = new int[n];

            if (map.Edges != null)
            {
                foreach (var e in map.Edges)
                {
                    if (!idToIndex.TryGetValue(e.FromNodeId?.Trim() ?? "", out var from)
                        || !idToIndex.TryGetValue(e.ToNodeId?.Trim() ?? "", out var to))
                    {
                        continue;
                    }

                    inDegree[to]++;
                }
            }

            var remaining = (int[])inDegree.Clone();
            var queue = new Queue<int>();
            for (var i = 0; i < n; i++)
            {
                if (remaining[i] == 0)
                {
                    queue.Enqueue(i);
                }
            }

            var depth = new int[n];
            var placed = new HashSet<int>();
            while (queue.Count > 0)
            {
                var u = queue.Dequeue();
                placed.Add(u);
                if (!layers.TryGetValue(depth[u], out var list))
                {
                    list = new List<int>();
                    layers[depth[u]] = list;
                }

                list.Add(u);

                if (map.Edges == null)
                {
                    continue;
                }

                var fromId = map.Nodes[u].NodeId?.Trim();
                foreach (var e in map.Edges)
                {
                    if (e.FromNodeId != fromId || !idToIndex.TryGetValue(e.ToNodeId?.Trim() ?? "", out var to))
                    {
                        continue;
                    }

                    remaining[to]--;
                    depth[to] = Math.Max(depth[to], depth[u] + 1);
                    if (remaining[to] == 0)
                    {
                        queue.Enqueue(to);
                    }
                }
            }

            for (var i = 0; i < n; i++)
            {
                if (placed.Contains(i))
                {
                    continue;
                }

                if (!layers.TryGetValue(0, out var orphanLayer))
                {
                    orphanLayer = new List<int>();
                    layers[0] = orphanLayer;
                }

                orphanLayer.Add(i);
            }

            const float layerGapX = 220f;
            const float nodeGapY = 90f;
            foreach (var kv in layers)
            {
                for (var row = 0; row < kv.Value.Count; row++)
                {
                    var nodeIndex = kv.Value[row];
                    var id = map.Nodes[nodeIndex].NodeId;
                    map.SetNodePosition(id, new Vector2(60f + kv.Key * layerGapX, 60f + row * nodeGapY));
                }
            }
        }
    }
}
#endif
