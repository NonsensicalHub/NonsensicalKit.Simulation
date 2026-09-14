using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation.Editor
{
    /// <summary>
    /// 路网配置窗口：节点连边编辑、排序命名、路径改写，以及正交三视图可视化。
    /// </summary>
    public class PathNetworkGraphWindow : EditorWindow
    {
        private const float ConfigPanelWidth = 280f;
        private const float NodeRadius = 9f;
        private const float EdgeHitWidth = 8f;
        private const float DragThreshold = 5f;
        private const float MinZoom = 0.04f;
        private const float MaxZoom = 300f;
        private const float FitPadding = 48f;

        private static readonly int CanvasControlHint = "PathNetworkGraphCanvas".GetHashCode();
        private static readonly Color CanvasBg = new Color(0.16f, 0.16f, 0.17f, 1f);
        private static readonly Color GridColor = new Color(1f, 1f, 1f, 0.04f);
        private static readonly Color EdgeColor = new Color(0.28f, 0.78f, 0.92f, 0.9f);
        private static readonly Color OneWayColor = new Color(1f, 0.72f, 0.28f, 0.95f);
        private static readonly Color SelectedEdgeColor = new Color(1f, 0.88f, 0.25f, 1f);
        private static readonly Color HoverEdgeColor = new Color(0.75f, 0.95f, 1f, 1f);
        private static readonly Color NodeFill = new Color(0.18f, 0.42f, 0.52f, 1f);
        private static readonly Color NodeOutline = new Color(0.45f, 0.92f, 1f, 1f);
        private static readonly Color IsolatedFill = new Color(0.28f, 0.28f, 0.3f, 1f);
        private static readonly Color ConnectFromFill = new Color(0.18f, 0.55f, 0.28f, 1f);
        private static readonly Color SelectedNodeFill = new Color(0.55f, 0.42f, 0.16f, 1f);
        private static readonly Color BoxFill = new Color(0.35f, 0.7f, 1f, 0.12f);
        private static readonly Color BoxOutline = new Color(0.45f, 0.8f, 1f, 0.85f);
        private static readonly Color AxisLabelColor = new Color(0.78f, 0.82f, 0.86f, 0.9f);

        /// <summary>正交投影平面：俯视 XZ、正视 XY、侧视 ZY。</summary>
        private enum GraphViewPlane
        {
            TopXZ = 0,
            FrontXY = 1,
            SideZY = 2
        }

        private GUIStyle _nodeLabelStyle;
        private GUIStyle _axisLabelStyle;

        private PathNetwork _network;
        private SerializedObject _networkSO;
        private Vector2 _configScroll;
        private Vector2 _pan;
        private float _zoom = 8f;
        private bool _fitPending = true;
        private GraphViewPlane _viewPlane = GraphViewPlane.TopXZ;
        private bool _bidirectional = true;
        private bool _clickToConnect;
        private bool _showLabels = true;

        private readonly HashSet<long> _selectedEdgeKeys = new HashSet<long>();
        private readonly HashSet<int> _selectedNodeIds = new HashSet<int>();
        private readonly List<GraphEdge> _edges = new List<GraphEdge>(128);
        private readonly List<PathNode> _nodes = new List<PathNode>(128);

        private Rect _canvasRect;
        private PathNode _hoverNode;
        private PathNode _connectFrom;
        private GraphEdge _hoverEdge;
        private bool _hasHoverEdge;

        private enum DragMode
        {
            None,
            Pan,
            BoxSelect,
            Connect
        }

        private DragMode _dragMode;
        private Vector2 _dragStartLocal;
        private Vector2 _dragCurrentLocal;
        private bool _dragMoved;

        [MenuItem("Tools/ScriptAnimation/打开路网图", false, 50)]
        public static void Open()
        {
            var window = GetWindow<PathNetworkGraphWindow>("路网图");
            window.minSize = new Vector2(920f, 420f);
            window.TryBindFromSelection();
            window.Show();
        }

        public static void Open(PathNetwork network)
        {
            var window = GetWindow<PathNetworkGraphWindow>("路网图");
            window.minSize = new Vector2(920f, 420f);
            window.SetNetwork(network);
            window.Show();
            window.Focus();
        }

        [MenuItem("CONTEXT/PathNetwork/打开路网图")]
        private static void OpenFromContext(MenuCommand command)
        {
            Open(command.context as PathNetwork);
        }

        public void SetNetwork(PathNetwork network)
        {
            if (_network == network)
                return;

            _network = network;
            RefreshSerializedObject();
            _fitPending = true;
            ClearInteraction(clearConnect: true);
            RebuildCache();
            Repaint();
        }

        private void RefreshSerializedObject()
        {
            _networkSO = _network != null ? new SerializedObject(_network) : null;
        }

        private void OnEnable()
        {
            wantsMouseMove = true;
            titleContent = new GUIContent("路网图");
            Selection.selectionChanged += OnEditorSelectionChanged;
            Undo.undoRedoPerformed += OnUndoRedo;
            TryBindFromSelection();
            RefreshSerializedObject();
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= OnEditorSelectionChanged;
            Undo.undoRedoPerformed -= OnUndoRedo;
        }

        private void OnFocus()
        {
            RebuildCache();
            Repaint();
        }

        private void OnEditorSelectionChanged()
        {
            TryBindFromSelection();
            SyncNodeSelectionFromEditor();
            Repaint();
        }

        private void OnUndoRedo()
        {
            RefreshSerializedObject();
            RebuildCache();
            PruneInvalidSelection();
            Repaint();
        }

        private void OnGUI()
        {
            DrawToolbar();
            EditorGUILayout.BeginHorizontal();
            DrawConfigPanel();
            EditorGUILayout.BeginVertical();
            DrawHint();
            DrawCanvas();
            DrawStatusBar();
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawConfigPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(ConfigPanelWidth));
            _configScroll = EditorGUILayout.BeginScrollView(_configScroll);

            if (_network == null)
            {
                EditorGUILayout.HelpBox("请选择或拖入 PathNetwork 组件。", MessageType.Info);
            }
            else
            {
                DrawNodeManagementSection();
                EditorGUILayout.Space(6f);
                DrawSortAndRenameSection();
                EditorGUILayout.Space(6f);
                DrawPathEditSection();
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawNodeManagementSection()
        {
            EditorGUILayout.LabelField("节点管理", EditorStyles.boldLabel);

            if (GUILayout.Button("收集子节点"))
            {
                PathNetworkEditActions.CollectNodes(_network);
                RebuildCache();
                Repaint();
            }

            if (GUILayout.Button("自动链接邻近节点"))
            {
                PathNetworkEditActions.AutoLinkNeighbors(_network);
                RebuildCache();
                Repaint();
            }

            if (GUILayout.Button("清除所有邻接"))
            {
                PathNetworkEditActions.ClearAllNeighbors(_network);
                RebuildCache();
                Repaint();
            }

            if (GUILayout.Button("清除错误邻居"))
            {
                PathNetworkEditActions.ClearInvalidNeighbors(_network);
                RebuildCache();
                Repaint();
            }

            if (GUILayout.Button("健康性检测（全连通）", GUILayout.Height(26f)))
            {
                PathNetworkEditActions.CheckReachabilityHealth(_network);
                Repaint();
            }
        }

        private void DrawSortAndRenameSection()
        {
            EditorGUILayout.LabelField("排序与命名", EditorStyles.boldLabel);

            if (GUILayout.Button("按位置排序子节点"))
            {
                PathNetworkEditActions.SortNodesByPosition(_network);
                RebuildCache();
                Repaint();
            }

            if (GUILayout.Button("按名字排序子节点"))
            {
                PathNetworkEditActions.SortNodesByName(_network);
                RebuildCache();
                Repaint();
            }

            if (_networkSO != null)
            {
                _networkSO.Update();
                EditorGUILayout.PropertyField(
                    _networkSO.FindProperty("m_renamePrefix"),
                    new GUIContent("命名前缀", "自动命名时加在「类型序号」前面。为空则不加。"));
                _networkSO.ApplyModifiedProperties();
            }

            if (GUILayout.Button("按连接状态自动命名"))
            {
                PathNetworkEditActions.AutoRenameNodes(_network, _networkSO);
                RebuildCache();
                Repaint();
            }
        }

        private void DrawPathEditSection()
        {
            EditorGUILayout.LabelField("路径编辑", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "配置起点、终点后，可对两点之间的无向最短路径做单向改写或完全断开；若两点有直接连线，可在中点插入新节点。",
                MessageType.None);

            if (_networkSO == null)
                return;

            _networkSO.Update();
            EditorGUILayout.PropertyField(_networkSO.FindProperty("m_pathEditStart"), new GUIContent("起点"));
            EditorGUILayout.PropertyField(_networkSO.FindProperty("m_pathEditGoal"), new GUIContent("终点"));
            _networkSO.ApplyModifiedProperties();

            bool canEditPath = _network.PathEditStart != null &&
                               _network.PathEditGoal != null &&
                               _network.PathEditStart != _network.PathEditGoal;

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(!canEditPath))
            {
                if (GUILayout.Button("最短路径设为单向", GUILayout.Height(26f)))
                {
                    PathNetworkEditActions.ApplyShortestPathOneWay(
                        _network, _network.PathEditStart, _network.PathEditGoal);
                    RebuildCache();
                    Repaint();
                }

                if (GUILayout.Button("交换并设为单向", GUILayout.Height(26f)))
                {
                    PathNetworkEditActions.SwapPathEditEndpoints(_networkSO);
                    PathNetworkEditActions.ApplyShortestPathOneWay(
                        _network, _network.PathEditStart, _network.PathEditGoal);
                    RebuildCache();
                    Repaint();
                }
            }

            EditorGUILayout.EndHorizontal();

            using (new EditorGUI.DisabledScope(!canEditPath))
            {
                if (GUILayout.Button("断开最短连线", GUILayout.Height(26f)))
                {
                    PathNetworkEditActions.ApplyDisconnectShortestPath(
                        _network, _network.PathEditStart, _network.PathEditGoal);
                    RebuildCache();
                    Repaint();
                }

                if (GUILayout.Button("在两点间插入节点", GUILayout.Height(26f)))
                {
                    PathNetworkEditActions.ApplyInsertNodeBetween(
                        _network, _network.PathEditStart, _network.PathEditGoal);
                    RebuildCache();
                    Repaint();
                }
            }
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            EditorGUI.BeginChangeCheck();
            var picked = (PathNetwork)EditorGUILayout.ObjectField(
                _network, typeof(PathNetwork), true, GUILayout.MinWidth(180f));
            if (EditorGUI.EndChangeCheck())
                SetNetwork(picked);

            if (GUILayout.Button("刷新", EditorStyles.toolbarButton, GUILayout.Width(44f)))
            {
                RebuildCache();
                Repaint();
            }

            if (GUILayout.Button("适应窗口", EditorStyles.toolbarButton, GUILayout.Width(68f)))
            {
                _fitPending = true;
                Repaint();
            }

            GUILayout.Space(6f);
            DrawViewPlaneToggle(GraphViewPlane.TopXZ, "俯视 XZ", 64f);
            DrawViewPlaneToggle(GraphViewPlane.FrontXY, "正视 XY", 64f);
            DrawViewPlaneToggle(GraphViewPlane.SideZY, "侧视 ZY", 64f);

            GUILayout.Space(8f);
            _bidirectional = GUILayout.Toggle(
                _bidirectional, _bidirectional ? "连边:双向" : "连边:单向",
                EditorStyles.toolbarButton, GUILayout.Width(76f));
            _clickToConnect = GUILayout.Toggle(
                _clickToConnect, "点击连边", EditorStyles.toolbarButton, GUILayout.Width(68f));
            _showLabels = GUILayout.Toggle(
                _showLabels, "显示名称", EditorStyles.toolbarButton, GUILayout.Width(68f));

            GUILayout.FlexibleSpace();

            using (new EditorGUI.DisabledScope(_selectedNodeIds.Count != 2))
            {
                if (GUILayout.Button("连接选中节点", EditorStyles.toolbarButton, GUILayout.Width(88f)))
                    ConnectSelectedNodes();
                if (GUILayout.Button("插入中间点", EditorStyles.toolbarButton, GUILayout.Width(76f)))
                    InsertNodeBetweenSelected();
            }

            using (new EditorGUI.DisabledScope(_selectedEdgeKeys.Count == 0))
            {
                if (GUILayout.Button($"删除选中连线 ({_selectedEdgeKeys.Count})", EditorStyles.toolbarButton, GUILayout.Width(130f)))
                    DeleteSelectedEdges();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawViewPlaneToggle(GraphViewPlane plane, string label, float width)
        {
            bool on = _viewPlane == plane;
            bool next = GUILayout.Toggle(on, label, EditorStyles.toolbarButton, GUILayout.Width(width));
            if (next && !on)
                SetViewPlane(plane);
        }

        private void SetViewPlane(GraphViewPlane plane)
        {
            if (_viewPlane == plane)
                return;

            _viewPlane = plane;
            _fitPending = true;
            _hoverNode = null;
            _hasHoverEdge = false;
            _dragMode = DragMode.None;
            _dragMoved = false;
            Repaint();
        }

        private void DrawHint()
        {
            EditorGUILayout.LabelField(
                "三视图切换俯角正视/侧视 · 中键/Alt+左键平移 · 滚轮缩放 · 空处拖拽框选连线 · Delete 删除 · 从节点拖到另一节点连边 · 右键菜单改方向",
                EditorStyles.miniLabel);
        }

        private void DrawStatusBar()
        {
            string connect = _connectFrom != null ? $" · 连接起点 {_connectFrom.name}" : string.Empty;
            string hover = _hoverNode != null ? $" · 悬停 {_hoverNode.name}" : string.Empty;
            EditorGUILayout.LabelField(
                $"{GetViewPlaneLabel()} · 节点 {_nodes.Count} · 连线 {_edges.Count} · 已选边 {_selectedEdgeKeys.Count} · 缩放 {_zoom:0.0} px/m{connect}{hover}",
                EditorStyles.miniLabel);
        }

        private void DrawCanvas()
        {
            Rect rect = GUILayoutUtility.GetRect(
                GUIContent.none, GUIStyle.none,
                GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (rect.width < 8f || rect.height < 8f)
                return;

            _canvasRect = rect;
            RebuildCache();

            if (_fitPending && Event.current.type == EventType.Repaint)
            {
                FitView();
                _fitPending = false;
            }

            HandleCanvasInput();

            if (Event.current.type == EventType.Repaint)
                PaintCanvas();
        }

        private void HandleCanvasInput()
        {
            Event e = Event.current;
            int controlId = GUIUtility.GetControlID(CanvasControlHint, FocusType.Keyboard, _canvasRect);
            Vector2 local = e.mousePosition - _canvasRect.position;
            bool inside = _canvasRect.Contains(e.mousePosition);

            if (e.type == EventType.MouseMove && inside)
            {
                UpdateHover(local);
                Repaint();
                return;
            }

            if (e.type == EventType.ScrollWheel && inside)
            {
                ZoomAt(local, e.delta.y);
                e.Use();
                Repaint();
                return;
            }

            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace)
                {
                    DeleteSelectedEdges();
                    e.Use();
                    return;
                }

                if (e.keyCode == KeyCode.Escape)
                {
                    ClearInteraction(clearConnect: true);
                    e.Use();
                    Repaint();
                    return;
                }

                if (e.keyCode == KeyCode.F)
                {
                    FitView();
                    e.Use();
                    Repaint();
                    return;
                }

                if (!e.control && !e.alt && !e.shift)
                {
                    if (e.keyCode == KeyCode.Alpha1 || e.keyCode == KeyCode.Keypad1)
                    {
                        SetViewPlane(GraphViewPlane.TopXZ);
                        e.Use();
                        return;
                    }

                    if (e.keyCode == KeyCode.Alpha2 || e.keyCode == KeyCode.Keypad2)
                    {
                        SetViewPlane(GraphViewPlane.FrontXY);
                        e.Use();
                        return;
                    }

                    if (e.keyCode == KeyCode.Alpha3 || e.keyCode == KeyCode.Keypad3)
                    {
                        SetViewPlane(GraphViewPlane.SideZY);
                        e.Use();
                        return;
                    }
                }
            }

            if (e.type == EventType.MouseDown && inside)
            {
                Focus();
                GUIUtility.hotControl = controlId;
                _dragStartLocal = local;
                _dragCurrentLocal = local;
                _dragMoved = false;
                UpdateHover(local);

                bool panButton = e.button == 2 || (e.button == 0 && e.alt) || (e.button == 1 && e.alt);
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
                    if (_hoverNode != null)
                    {
                        _dragMode = DragMode.Connect;
                        if (_connectFrom == null || !_clickToConnect)
                            _connectFrom = _hoverNode;
                    }
                    else if (_hasHoverEdge)
                    {
                        _dragMode = DragMode.None;
                        ToggleOrSelectEdge(_hoverEdge.Key, e.shift || e.control);
                    }
                    else
                    {
                        _dragMode = DragMode.BoxSelect;
                        if (!e.shift && !e.control)
                            _selectedEdgeKeys.Clear();
                    }

                    e.Use();
                    Repaint();
                }

                return;
            }

            if (GUIUtility.hotControl != controlId)
                return;

            if (e.type == EventType.MouseDrag)
            {
                _dragCurrentLocal = local;
                if ((_dragCurrentLocal - _dragStartLocal).sqrMagnitude > DragThreshold * DragThreshold)
                    _dragMoved = true;

                if (_dragMode == DragMode.Pan)
                    _pan += e.delta;

                e.Use();
                Repaint();
                return;
            }

            if (e.type == EventType.MouseUp)
            {
                _dragCurrentLocal = local;
                UpdateHover(local);

                if (e.button == 1 && !_dragMoved)
                    ShowContextMenu(local);
                else if (_dragMode == DragMode.BoxSelect && _dragMoved)
                    ApplyBoxSelection(ToRect(_dragStartLocal, _dragCurrentLocal), e.control);
                else if (_dragMode == DragMode.BoxSelect && !_dragMoved)
                {
                    if (!e.shift && !e.control)
                    {
                        _selectedEdgeKeys.Clear();
                        _selectedNodeIds.Clear();
                        _connectFrom = null;
                    }
                }
                else if (_dragMode == DragMode.Connect)
                    FinishConnectGesture(e);

                _dragMode = DragMode.None;
                GUIUtility.hotControl = 0;
                e.Use();
                Repaint();
            }
        }

        private void FinishConnectGesture(Event e)
        {
            PathNode from = _connectFrom;
            PathNode to = _hoverNode;

            if (_dragMoved)
            {
                if (from != null && to != null && to != from)
                    ConnectPair(from, to, _bidirectional);
                if (!_clickToConnect)
                    _connectFrom = null;
                return;
            }

            if (from == null)
                return;

            SelectNode(from, e.shift || e.control);

            if (_clickToConnect && to != null && to != from)
            {
                ConnectPair(from, to, _bidirectional);
                _connectFrom = _bidirectional ? null : to;
                return;
            }

            if (_clickToConnect)
                _connectFrom = from;
            else
                _connectFrom = null;
        }

        private void PaintCanvas()
        {
            EditorGUI.DrawRect(_canvasRect, CanvasBg);
            GUI.BeginClip(_canvasRect);
            Handles.BeginGUI();

            DrawGrid();
            DrawEdges();
            DrawConnectPreview();
            DrawNodes();
            DrawSelectionBox();
            DrawViewAxisOverlay();

            Handles.EndGUI();
            GUI.EndClip();
            Handles.DrawSolidRectangleWithOutline(
                new Vector3[]
                {
                    new Vector3(_canvasRect.xMin, _canvasRect.yMin),
                    new Vector3(_canvasRect.xMax, _canvasRect.yMin),
                    new Vector3(_canvasRect.xMax, _canvasRect.yMax),
                    new Vector3(_canvasRect.xMin, _canvasRect.yMax)
                },
                Color.clear,
                new Color(0f, 0f, 0f, 0.45f));
        }

        private GUIStyle GetNodeLabelStyle()
        {
            if (_nodeLabelStyle == null)
            {
                _nodeLabelStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.UpperCenter,
                    fontSize = 10,
                    fontStyle = FontStyle.Bold
                };
                _nodeLabelStyle.normal.textColor = new Color(0.92f, 0.94f, 0.96f, 0.95f);
            }

            return _nodeLabelStyle;
        }

        private GUIStyle GetAxisLabelStyle()
        {
            if (_axisLabelStyle == null)
            {
                _axisLabelStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.UpperLeft,
                    fontSize = 11,
                    fontStyle = FontStyle.Bold
                };
                _axisLabelStyle.normal.textColor = AxisLabelColor;
            }

            return _axisLabelStyle;
        }

        private void DrawViewAxisOverlay()
        {
            GetViewAxes(out string hAxis, out string vAxis);
            var style = GetAxisLabelStyle();
            GUI.Label(new Rect(10f, 8f, 220f, 18f), $"{GetViewPlaneLabel()}  {hAxis}→  {vAxis}↑", style);
        }

        private void DrawGrid()
        {
            float step = NiceGridStep();
            Vector2 origin = GraphToLocal(Vector2.zero);
            Handles.color = GridColor;

            float startX = origin.x % (step * _zoom);
            for (float x = startX; x < _canvasRect.width; x += step * _zoom)
                Handles.DrawLine(new Vector3(x, 0f), new Vector3(x, _canvasRect.height));

            float startY = origin.y % (step * _zoom);
            for (float y = startY; y < _canvasRect.height; y += step * _zoom)
                Handles.DrawLine(new Vector3(0f, y), new Vector3(_canvasRect.width, y));
        }

        private void DrawEdges()
        {
            for (int i = 0; i < _edges.Count; i++)
            {
                var edge = _edges[i];
                if (edge.A == null || edge.B == null)
                    continue;

                Vector2 a = WorldToLocal(edge.A.Position);
                Vector2 b = WorldToLocal(edge.B.Position);
                bool selected = _selectedEdgeKeys.Contains(edge.Key);
                bool hovered = _hasHoverEdge && _hoverEdge.Key == edge.Key && _dragMode != DragMode.BoxSelect;

                Color color = edge.IsBidirectional ? EdgeColor : OneWayColor;
                if (hovered)
                    color = HoverEdgeColor;
                if (selected)
                    color = SelectedEdgeColor;

                float width = selected ? 5f : hovered ? 4f : 2.4f;
                Handles.color = color;
                Handles.DrawAAPolyLine(width, a, b);

                if (!edge.IsBidirectional)
                {
                    Vector2 from = edge.AToB ? a : b;
                    Vector2 to = edge.AToB ? b : a;
                    DrawArrow(from, to, color, selected ? 11f : 9f);
                }
            }
        }

        private void DrawNodes()
        {
            var labelStyle = GetNodeLabelStyle();

            for (int i = 0; i < _nodes.Count; i++)
            {
                var node = _nodes[i];
                if (node == null)
                    continue;

                Vector2 p = WorldToLocal(node.Position);
                bool selected = _selectedNodeIds.Contains(node.GetInstanceID());
                bool connectFrom = _connectFrom == node;
                bool hovered = _hoverNode == node;
                bool isolated = node.Neighbors == null || node.Neighbors.Count == 0;

                Color fill = isolated ? IsolatedFill : NodeFill;
                if (selected)
                    fill = SelectedNodeFill;
                if (connectFrom)
                    fill = ConnectFromFill;

                Handles.color = fill;
                Handles.DrawSolidDisc(p, Vector3.forward, NodeRadius);
                Handles.color = hovered || selected || connectFrom ? Color.white : NodeOutline;
                Handles.DrawWireDisc(p, Vector3.forward, NodeRadius);

                if (_showLabels || hovered || selected || connectFrom)
                {
                    var labelRect = new Rect(p.x - 70f, p.y + NodeRadius + 1f, 140f, 16f);
                    GUI.Label(labelRect, node.name, labelStyle);
                }
            }
        }

        private void DrawConnectPreview()
        {
            if (_dragMode != DragMode.Connect || _connectFrom == null || !_dragMoved)
                return;

            Vector2 from = WorldToLocal(_connectFrom.Position);
            Handles.color = _bidirectional ? EdgeColor : OneWayColor;
            Handles.DrawDottedLine(from, _dragCurrentLocal, 4f);
            if (!_bidirectional)
                DrawArrow(from, _dragCurrentLocal, Handles.color, 9f);
        }

        private void DrawSelectionBox()
        {
            if (_dragMode != DragMode.BoxSelect || !_dragMoved)
                return;

            Rect box = ToRect(_dragStartLocal, _dragCurrentLocal);
            Handles.DrawSolidRectangleWithOutline(
                new Vector3[]
                {
                    new Vector3(box.xMin, box.yMin),
                    new Vector3(box.xMax, box.yMin),
                    new Vector3(box.xMax, box.yMax),
                    new Vector3(box.xMin, box.yMax)
                },
                BoxFill,
                BoxOutline);
        }

        private void ShowContextMenu(Vector2 local)
        {
            UpdateHover(local);
            var menu = new GenericMenu();

            if (_hoverNode != null)
            {
                var node = _hoverNode;
                menu.AddItem(new GUIContent($"聚焦场景 {node.name}"), false, () => FrameNodeInScene(node));
                menu.AddItem(new GUIContent("设为连接起点"), false, () =>
                {
                    _connectFrom = node;
                    _clickToConnect = true;
                    Repaint();
                });
                menu.AddItem(new GUIContent("断开该节点全部连线"), false, () => DisconnectNode(node));
            }

            if (_hasHoverEdge)
            {
                var edge = _hoverEdge;
                if (_hoverNode != null)
                    menu.AddSeparator("");

                menu.AddItem(new GUIContent($"在中点插入节点  {edge.A.name} — {edge.B.name}"), false, () =>
                    InsertNodeBetweenPair(edge.A, edge.B));
                menu.AddItem(new GUIContent($"删除连线  {edge.A.name} ↔ {edge.B.name}"), false, () =>
                {
                    _selectedEdgeKeys.Clear();
                    _selectedEdgeKeys.Add(edge.Key);
                    DeleteSelectedEdges();
                });
                menu.AddItem(new GUIContent($"设为双向  {edge.A.name} ↔ {edge.B.name}"), false, () =>
                    SetEdgeDirection(edge.A, edge.B, bidirectional: true));
                menu.AddItem(new GUIContent($"设为单向  {edge.A.name} → {edge.B.name}"), false, () =>
                    SetEdgeDirection(edge.A, edge.B, bidirectional: false));
                menu.AddItem(new GUIContent($"设为单向  {edge.B.name} → {edge.A.name}"), false, () =>
                    SetEdgeDirection(edge.B, edge.A, bidirectional: false));
            }

            if (_hoverNode == null && !_hasHoverEdge)
            {
                menu.AddItem(new GUIContent("适应窗口"), false, () =>
                {
                    FitView();
                    Repaint();
                });
                menu.AddItem(new GUIContent("视图/俯视 XZ"), _viewPlane == GraphViewPlane.TopXZ, () =>
                    SetViewPlane(GraphViewPlane.TopXZ));
                menu.AddItem(new GUIContent("视图/正视 XY"), _viewPlane == GraphViewPlane.FrontXY, () =>
                    SetViewPlane(GraphViewPlane.FrontXY));
                menu.AddItem(new GUIContent("视图/侧视 ZY"), _viewPlane == GraphViewPlane.SideZY, () =>
                    SetViewPlane(GraphViewPlane.SideZY));
                if (_selectedEdgeKeys.Count > 0)
                    menu.AddItem(new GUIContent($"删除选中连线 ({_selectedEdgeKeys.Count})"), false, DeleteSelectedEdges);
            }

            if (menu.GetItemCount() > 0)
                menu.ShowAsContext();
        }

        private void UpdateHover(Vector2 local)
        {
            _hoverNode = HitTestNode(local);
            _hasHoverEdge = false;
            _hoverEdge = default;
            if (_hoverNode == null)
                _hasHoverEdge = TryHitTestEdge(local, out _hoverEdge);
        }

        private PathNode HitTestNode(Vector2 local)
        {
            PathNode best = null;
            float bestDist = NodeRadius + 2f;
            for (int i = 0; i < _nodes.Count; i++)
            {
                var node = _nodes[i];
                if (node == null)
                    continue;

                float dist = Vector2.Distance(local, WorldToLocal(node.Position));
                if (dist <= bestDist)
                {
                    bestDist = dist;
                    best = node;
                }
            }

            return best;
        }

        private bool TryHitTestEdge(Vector2 local, out GraphEdge hit)
        {
            hit = default;
            float best = EdgeHitWidth;
            bool found = false;
            for (int i = 0; i < _edges.Count; i++)
            {
                var edge = _edges[i];
                if (edge.A == null || edge.B == null)
                    continue;

                float dist = DistancePointToSegment(local, WorldToLocal(edge.A.Position), WorldToLocal(edge.B.Position));
                if (dist < best)
                {
                    best = dist;
                    hit = edge;
                    found = true;
                }
            }

            return found;
        }

        private void ApplyBoxSelection(Rect box, bool toggle)
        {
            for (int i = 0; i < _edges.Count; i++)
            {
                var edge = _edges[i];
                if (edge.A == null || edge.B == null)
                    continue;

                Vector2 a = WorldToLocal(edge.A.Position);
                Vector2 b = WorldToLocal(edge.B.Position);
                if (!SegmentIntersectsRect(a, b, box))
                    continue;

                if (toggle)
                {
                    if (!_selectedEdgeKeys.Add(edge.Key))
                        _selectedEdgeKeys.Remove(edge.Key);
                }
                else
                {
                    _selectedEdgeKeys.Add(edge.Key);
                }
            }
        }

        private void ToggleOrSelectEdge(long key, bool additive)
        {
            if (!additive)
            {
                _selectedEdgeKeys.Clear();
                _selectedEdgeKeys.Add(key);
                return;
            }

            if (!_selectedEdgeKeys.Add(key))
                _selectedEdgeKeys.Remove(key);
        }

        private void SelectNode(PathNode node, bool additive)
        {
            if (node == null)
                return;

            if (!additive)
                _selectedNodeIds.Clear();

            int id = node.GetInstanceID();
            if (additive && _selectedNodeIds.Contains(id))
                _selectedNodeIds.Remove(id);
            else
                _selectedNodeIds.Add(id);

            if (!additive)
                Selection.activeObject = node;
            else
            {
                var objects = new List<UnityEngine.Object>();
                for (int i = 0; i < _nodes.Count; i++)
                {
                    if (_nodes[i] != null && _selectedNodeIds.Contains(_nodes[i].GetInstanceID()))
                        objects.Add(_nodes[i].gameObject);
                }

                Selection.objects = objects.ToArray();
            }
        }

        private void ConnectSelectedNodes()
        {
            PathNode a = null;
            PathNode b = null;
            for (int i = 0; i < _nodes.Count; i++)
            {
                var node = _nodes[i];
                if (node == null || !_selectedNodeIds.Contains(node.GetInstanceID()))
                    continue;
                if (a == null)
                    a = node;
                else
                {
                    b = node;
                    break;
                }
            }

            if (a != null && b != null)
                ConnectPair(a, b, _bidirectional);
        }

        private void InsertNodeBetweenSelected()
        {
            PathNode a = null;
            PathNode b = null;
            for (int i = 0; i < _nodes.Count; i++)
            {
                var node = _nodes[i];
                if (node == null || !_selectedNodeIds.Contains(node.GetInstanceID()))
                    continue;
                if (a == null)
                    a = node;
                else
                {
                    b = node;
                    break;
                }
            }

            if (a != null && b != null)
                InsertNodeBetweenPair(a, b);
        }

        private void InsertNodeBetweenPair(PathNode a, PathNode b)
        {
            if (_network == null || a == null || b == null || a == b)
                return;

            if (!a.IsConnectedTo(b) && !b.IsConnectedTo(a))
            {
                EditorUtility.DisplayDialog(
                    "路网图",
                    $"{a.name} 与 {b.name} 没有直接连线。插入节点只作用于相邻的两点。",
                    "OK");
                return;
            }

            Undo.RegisterFullObjectHierarchyUndo(_network.gameObject, "Insert Path Node Between");
            var inserted = _network.InsertNodeBetween(a, b);
            if (inserted == null)
                return;

            Undo.RegisterCreatedObjectUndo(inserted.gameObject, "Insert Path Node Between");
            _selectedEdgeKeys.Clear();
            _selectedNodeIds.Clear();
            _selectedNodeIds.Add(inserted.GetInstanceID());
            MarkDirty();
            EditorUtility.SetDirty(inserted);
            RebuildCache();
            Selection.activeGameObject = inserted.gameObject;
            Debug.Log($"[{_network.name}] 插入节点：{a.name} — {inserted.name} — {b.name}。", _network);
        }

        private void ConnectPair(PathNode from, PathNode to, bool bidirectional)
        {
            if (_network == null || from == null || to == null || from == to)
                return;

            Undo.RegisterFullObjectHierarchyUndo(_network.gameObject, "Connect Path Nodes");
            from.Connect(to, bidirectional);
            if (!bidirectional)
                to.RemoveNeighbor(from);

            MarkDirty();
            RebuildCache();
            Debug.Log(
                $"[{_network.name}] 连边：{from.name} {(bidirectional ? "↔" : "→")} {to.name}。",
                _network);
        }

        private void SetEdgeDirection(PathNode from, PathNode to, bool bidirectional)
        {
            if (_network == null || from == null || to == null || from == to)
                return;

            Undo.RegisterFullObjectHierarchyUndo(_network.gameObject, "Set Path Edge Direction");
            from.Connect(to, bidirectional);
            if (!bidirectional)
                to.RemoveNeighbor(from);
            else
                to.Connect(from, false);

            MarkDirty();
            RebuildCache();
        }

        private void DisconnectNode(PathNode node)
        {
            if (_network == null || node == null)
                return;

            Undo.RegisterFullObjectHierarchyUndo(_network.gameObject, "Clear Node Neighbors");
            node.ClearNeighbors();
            MarkDirty();
            RebuildCache();
            _selectedEdgeKeys.Clear();
        }

        private void DeleteSelectedEdges()
        {
            if (_network == null || _selectedEdgeKeys.Count == 0)
                return;

            Undo.RegisterFullObjectHierarchyUndo(_network.gameObject, "Delete Path Edges");
            int removed = 0;
            for (int i = 0; i < _edges.Count; i++)
            {
                var edge = _edges[i];
                if (!_selectedEdgeKeys.Contains(edge.Key) || edge.A == null || edge.B == null)
                    continue;

                if (edge.A.IsConnectedTo(edge.B))
                {
                    edge.A.RemoveNeighbor(edge.B);
                    removed++;
                }

                if (edge.B.IsConnectedTo(edge.A))
                {
                    edge.B.RemoveNeighbor(edge.A);
                    removed++;
                }
            }

            _selectedEdgeKeys.Clear();
            MarkDirty();
            RebuildCache();
            Debug.Log($"[{_network.name}] 删除连线：清除邻接引用 {removed}。", _network);
        }

        private void FrameNodeInScene(PathNode node)
        {
            if (node == null)
                return;

            Selection.activeObject = node;
            if (SceneView.lastActiveSceneView != null)
                SceneView.lastActiveSceneView.FrameSelected();
        }

        private void MarkDirty()
        {
            if (_network == null)
                return;

            EditorUtility.SetDirty(_network);
            for (int i = 0; i < _nodes.Count; i++)
            {
                if (_nodes[i] != null)
                    EditorUtility.SetDirty(_nodes[i]);
            }

            SceneView.RepaintAll();
        }

        private void RebuildCache()
        {
            _nodes.Clear();
            _edges.Clear();
            if (_network == null)
                return;

            var source = _network.Nodes;
            var seenEdges = new HashSet<long>();
            for (int i = 0; i < source.Count; i++)
            {
                var node = source[i];
                if (node == null)
                    continue;
                _nodes.Add(node);

                var neighbors = node.Neighbors;
                for (int n = 0; n < neighbors.Count; n++)
                {
                    var other = neighbors[n];
                    if (other == null || other == node)
                        continue;

                    long key = MakeEdgeKey(node, other);
                    if (!seenEdges.Add(key))
                        continue;

                    PathNode a = node.GetInstanceID() < other.GetInstanceID() ? node : other;
                    PathNode b = node.GetInstanceID() < other.GetInstanceID() ? other : node;
                    _edges.Add(new GraphEdge
                    {
                        A = a,
                        B = b,
                        Key = key,
                        AToB = a.IsConnectedTo(b),
                        BToA = b.IsConnectedTo(a)
                    });
                }
            }

            PruneInvalidSelection();
        }

        private void PruneInvalidSelection()
        {
            if (_selectedEdgeKeys.Count > 0)
            {
                var valid = new HashSet<long>();
                for (int i = 0; i < _edges.Count; i++)
                    valid.Add(_edges[i].Key);

                _selectedEdgeKeys.RemoveWhere(key => !valid.Contains(key));
            }

            if (_connectFrom != null && !_nodes.Contains(_connectFrom))
                _connectFrom = null;

            if (_selectedNodeIds.Count > 0)
            {
                var validNodes = new HashSet<int>();
                for (int i = 0; i < _nodes.Count; i++)
                {
                    if (_nodes[i] != null)
                        validNodes.Add(_nodes[i].GetInstanceID());
                }

                _selectedNodeIds.RemoveWhere(id => !validNodes.Contains(id));
            }
        }

        private void TryBindFromSelection()
        {
            var go = Selection.activeGameObject;
            if (go == null)
                return;

            var network = go.GetComponent<PathNetwork>() ?? go.GetComponentInParent<PathNetwork>();
            if (network != null)
                SetNetwork(network);
        }

        private void SyncNodeSelectionFromEditor()
        {
            _selectedNodeIds.Clear();
            var objects = Selection.gameObjects;
            for (int i = 0; i < objects.Length; i++)
            {
                var node = objects[i] != null ? objects[i].GetComponent<PathNode>() : null;
                if (node != null)
                    _selectedNodeIds.Add(node.GetInstanceID());
            }
        }

        private void ClearInteraction(bool clearConnect)
        {
            _selectedEdgeKeys.Clear();
            _selectedNodeIds.Clear();
            _hoverNode = null;
            _hasHoverEdge = false;
            _dragMode = DragMode.None;
            _dragMoved = false;
            if (clearConnect)
                _connectFrom = null;
        }

        private void FitView()
        {
            if (_nodes.Count == 0 || _canvasRect.width < 8f)
            {
                _zoom = 8f;
                _pan = new Vector2(_canvasRect.width * 0.5f, _canvasRect.height * 0.5f);
                return;
            }

            float minU = float.MaxValue;
            float minV = float.MaxValue;
            float maxU = float.MinValue;
            float maxV = float.MinValue;
            int count = 0;
            for (int i = 0; i < _nodes.Count; i++)
            {
                var node = _nodes[i];
                if (node == null)
                    continue;
                Vector2 p = ProjectWorld(node.Position);
                minU = Mathf.Min(minU, p.x);
                maxU = Mathf.Max(maxU, p.x);
                minV = Mathf.Min(minV, p.y);
                maxV = Mathf.Max(maxV, p.y);
                count++;
            }

            if (count == 0)
                return;

            float width = Mathf.Max(0.5f, maxU - minU);
            float height = Mathf.Max(0.5f, maxV - minV);
            float zoomX = (_canvasRect.width - FitPadding * 2f) / width;
            float zoomY = (_canvasRect.height - FitPadding * 2f) / height;
            _zoom = Mathf.Clamp(Mathf.Min(zoomX, zoomY), MinZoom, MaxZoom);

            Vector2 center = new Vector2((minU + maxU) * 0.5f, (minV + maxV) * 0.5f);
            _pan = new Vector2(_canvasRect.width * 0.5f, _canvasRect.height * 0.5f) - GraphToOffset(center);
        }

        private void ZoomAt(Vector2 local, float scrollDelta)
        {
            float oldZoom = _zoom;
            _zoom = Mathf.Clamp(_zoom * (scrollDelta > 0f ? 0.9f : 1.11f), MinZoom, MaxZoom);
            Vector2 graph = (local - _pan) / oldZoom;
            _pan = local - graph * _zoom;
        }

        private Vector2 WorldToLocal(Vector3 world)
        {
            return GraphToLocal(ProjectWorld(world));
        }

        private Vector2 ProjectWorld(Vector3 world)
        {
            switch (_viewPlane)
            {
                case GraphViewPlane.FrontXY:
                    return new Vector2(world.x, world.y);
                case GraphViewPlane.SideZY:
                    return new Vector2(world.z, world.y);
                default:
                    return new Vector2(world.x, world.z);
            }
        }

        private string GetViewPlaneLabel()
        {
            switch (_viewPlane)
            {
                case GraphViewPlane.FrontXY:
                    return "正视 XY";
                case GraphViewPlane.SideZY:
                    return "侧视 ZY";
                default:
                    return "俯视 XZ";
            }
        }

        private void GetViewAxes(out string horizontal, out string vertical)
        {
            switch (_viewPlane)
            {
                case GraphViewPlane.FrontXY:
                    horizontal = "X";
                    vertical = "Y";
                    break;
                case GraphViewPlane.SideZY:
                    horizontal = "Z";
                    vertical = "Y";
                    break;
                default:
                    horizontal = "X";
                    vertical = "Z";
                    break;
            }
        }

        private Vector2 GraphToLocal(Vector2 graph)
        {
            return _pan + GraphToOffset(graph);
        }

        private Vector2 GraphToOffset(Vector2 graph)
        {
            return new Vector2(graph.x * _zoom, -graph.y * _zoom);
        }

        private float NiceGridStep()
        {
            float worldStep = 1f;
            float pixel = worldStep * _zoom;
            while (pixel < 28f)
            {
                worldStep *= 2f;
                pixel = worldStep * _zoom;
            }

            while (pixel > 90f && worldStep > 0.125f)
            {
                worldStep *= 0.5f;
                pixel = worldStep * _zoom;
            }

            return worldStep;
        }

        private static void DrawArrow(Vector2 from, Vector2 to, Color color, float size)
        {
            Vector2 dir = to - from;
            if (dir.sqrMagnitude < 0.0001f)
                return;

            dir.Normalize();
            Vector2 n = new Vector2(-dir.y, dir.x);
            Vector2 tip = Vector2.Lerp(from, to, 0.64f);
            Handles.color = color;
            Handles.DrawAAConvexPolygon(
                tip,
                tip - dir * size + n * size * 0.55f,
                tip - dir * size - n * size * 0.55f);
        }

        private static Rect ToRect(Vector2 a, Vector2 b)
        {
            return Rect.MinMaxRect(
                Mathf.Min(a.x, b.x),
                Mathf.Min(a.y, b.y),
                Mathf.Max(a.x, b.x),
                Mathf.Max(a.y, b.y));
        }

        private static float DistancePointToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lenSq = ab.sqrMagnitude;
            if (lenSq < 0.0001f)
                return Vector2.Distance(p, a);

            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSq);
            return Vector2.Distance(p, a + ab * t);
        }

        private static bool SegmentIntersectsRect(Vector2 a, Vector2 b, Rect rect)
        {
            if (rect.Contains(a) || rect.Contains(b))
                return true;

            Vector2 r0 = new Vector2(rect.xMin, rect.yMin);
            Vector2 r1 = new Vector2(rect.xMax, rect.yMin);
            Vector2 r2 = new Vector2(rect.xMax, rect.yMax);
            Vector2 r3 = new Vector2(rect.xMin, rect.yMax);
            return SegmentsIntersect(a, b, r0, r1)
                   || SegmentsIntersect(a, b, r1, r2)
                   || SegmentsIntersect(a, b, r2, r3)
                   || SegmentsIntersect(a, b, r3, r0);
        }

        private static bool SegmentsIntersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            return Crossing(a, b, c, d) && Crossing(c, d, a, b);
        }

        private static bool Crossing(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            Vector2 ab = b - a;
            float c1 = Cross(ab, c - a);
            float c2 = Cross(ab, d - a);
            if (Mathf.Abs(c1) < 0.0001f && Mathf.Abs(c2) < 0.0001f)
            {
                return Overlap(a.x, b.x, c.x, d.x) && Overlap(a.y, b.y, c.y, d.y);
            }

            return c1 * c2 <= 0f;
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        private static bool Overlap(float a1, float a2, float b1, float b2)
        {
            if (a1 > a2)
                (a1, a2) = (a2, a1);
            if (b1 > b2)
                (b1, b2) = (b2, b1);
            return a1 <= b2 && b1 <= a2;
        }

        private static long MakeEdgeKey(PathNode a, PathNode b)
        {
            int idA = a.GetInstanceID();
            int idB = b.GetInstanceID();
            if (idA > idB)
                (idA, idB) = (idB, idA);
            return ((long)idA << 32) ^ (uint)idB;
        }

        private struct GraphEdge
        {
            public PathNode A;
            public PathNode B;
            public long Key;
            public bool AToB;
            public bool BToA;
            public bool IsBidirectional => AToB && BToA;
        }
    }
}
