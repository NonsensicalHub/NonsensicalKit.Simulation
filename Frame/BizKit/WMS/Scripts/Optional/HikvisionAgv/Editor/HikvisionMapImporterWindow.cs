using System.Collections.Generic;
using System.IO;
using System.Text;
using HikvisionAgv;
using NonsensicalKit.ScriptAnimation;
using UnityEditor;
using UnityEngine;

namespace HikvisionAgv.Editor
{
    public sealed class HikvisionMapImporterWindow : EditorWindow
    {
        [SerializeField] string m_xmlPath;
        [SerializeField] PathNetwork m_network;
        [SerializeField] HikvisionMapFrame m_frame;
        [SerializeField] string m_mapShortName;
        [SerializeField] bool m_writePositions = true;
        [SerializeField] bool m_rebuildEdges = true;
        [SerializeField] bool m_disableAutoLink = true;

        [MenuItem("Tools/海康AGV/导入海康AGV地图到路网")]
        public static void Open()
        {
            var window = GetWindow<HikvisionMapImporterWindow>("海康地图导入");
            window.minSize = new Vector2(420f, 280f);
            window.Show();
        }

        void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "解析海康 MapCfg XML，写入 PathNode 上的海康模块与邻接。不改停留字段。导入后请用地图框对齐现场模型。",
                MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            m_xmlPath = EditorGUILayout.TextField("XML 路径", m_xmlPath);
            if (GUILayout.Button("浏览", GUILayout.Width(64f)))
            {
                string picked = EditorUtility.OpenFilePanel("选择海康地图 XML", string.Empty, "xml");
                if (!string.IsNullOrEmpty(picked))
                    m_xmlPath = picked;
            }

            EditorGUILayout.EndHorizontal();

            m_network = (PathNetwork)EditorGUILayout.ObjectField(
                "目标路网", m_network, typeof(PathNetwork), true);
            m_frame = (HikvisionMapFrame)EditorGUILayout.ObjectField(
                "地图坐标框", m_frame, typeof(HikvisionMapFrame), true);
            m_mapShortName = EditorGUILayout.TextField("RCS 地图简称", m_mapShortName);
            EditorGUILayout.HelpBox("RCS 的 AA/BB，不是 XML MapId。离线沿路网走时靠这个对层。", MessageType.None);

            m_writePositions = EditorGUILayout.Toggle("按映射写入坐标", m_writePositions);
            m_rebuildEdges = EditorGUILayout.Toggle("按 XML 重建海康邻接", m_rebuildEdges);
            m_disableAutoLink = EditorGUILayout.Toggle("关闭 Awake 自动连边", m_disableAutoLink);

            EditorGUI.BeginDisabledGroup(
                string.IsNullOrEmpty(m_xmlPath) || m_network == null);
            if (GUILayout.Button("导入 / 同步", GUILayout.Height(28f)))
                Import();
            EditorGUI.EndDisabledGroup();
        }

        void Import()
        {
            if (!File.Exists(m_xmlPath))
            {
                EditorUtility.DisplayDialog("海康导入", "找不到 XML 文件。", "确定");
                return;
            }

            string xml = File.ReadAllText(m_xmlPath, Encoding.UTF8);
            var cfg = HikvisionMapCfgParser.Parse(xml);
            if (cfg.Points.Count == 0)
            {
                EditorUtility.DisplayDialog("海康导入", "没有解析到 PointInfo。", "确定");
                return;
            }

            var network = m_network;
            Undo.RecordObject(network, "导入海康地图");
            if (m_disableAutoLink)
                network.AutoLinkOnAwake = false;

            var frame = m_frame;
            if (frame == null)
            {
                frame = network.GetComponent<HikvisionMapFrame>();
                if (frame == null)
                    frame = Undo.AddComponent<HikvisionMapFrame>(network.gameObject);
                m_frame = frame;
            }

            Undo.RecordObject(frame, "导入海康地图框");
            frame.Network = network;
            frame.MapId = cfg.MapId;
            if (!string.IsNullOrWhiteSpace(m_mapShortName))
                frame.MapShortName = m_mapShortName.Trim();

            var root = EnsurePointsRoot(network);
            network.CollectNodesFromChildren();

            var byPointId = new Dictionary<int, PathNode>();
            var nodes = network.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                var feature = node != null ? node.GetFeature<HikvisionPathPointFeature>() : null;
                if (feature == null)
                    continue;
                if (!string.IsNullOrEmpty(cfg.MapId) &&
                    !string.IsNullOrEmpty(feature.MapId) &&
                    feature.MapId != cfg.MapId)
                    continue;
                byPointId[feature.PointId] = node;
            }

            var hikNodes = new HashSet<PathNode>();
            for (int i = 0; i < cfg.Points.Count; i++)
            {
                var point = cfg.Points[i];
                if (!byPointId.TryGetValue(point.Id, out var node) || node == null)
                {
                    var go = new GameObject("Hik_" + point.Id);
                    Undo.RegisterCreatedObjectUndo(go, "创建海康节点");
                    go.transform.SetParent(root, false);
                    node = Undo.AddComponent<PathNode>(go);
                    byPointId[point.Id] = node;
                }

                Undo.RecordObject(node, "同步海康节点");
                Undo.RecordObject(node.transform, "同步海康节点坐标");
                node.SetFeature(new HikvisionPathPointFeature
                {
                    MapId = cfg.MapId,
                    PointId = point.Id,
                    MapX = point.Xpos,
                    MapY = point.Ypos,
                    AllDir = point.AllDir,
                    Width = point.Width,
                    Height = point.Height,
                    RoadProperty = point.RoadProperty
                });

                if (m_writePositions)
                    node.transform.position = frame.MapToWorldByFrame(point.Xpos, point.Ypos);

                EditorUtility.SetDirty(node);
                hikNodes.Add(node);
            }

            if (m_rebuildEdges)
                RebuildHikvisionEdges(cfg, byPointId, hikNodes);

            network.CollectNodesFromChildren();
            HikvisionNodeBehaviorQuery.Invalidate(network);
            EditorUtility.SetDirty(network);
            EditorUtility.SetDirty(frame);
            Debug.Log(
                $"海康地图导入完成：MapId={cfg.MapId} 点 {cfg.Points.Count}。停留字段未改。",
                network);
        }

        static Transform EnsurePointsRoot(PathNetwork network)
        {
            Transform parent = network.transform.Find("HikvisionPoints");
            if (parent != null)
                return parent;
            var go = new GameObject("HikvisionPoints");
            Undo.RegisterCreatedObjectUndo(go, "创建海康节点根");
            go.transform.SetParent(network.transform, false);
            return go.transform;
        }

        static void RebuildHikvisionEdges(
            HikvisionMapCfg cfg,
            Dictionary<int, PathNode> byPointId,
            HashSet<PathNode> hikNodes)
        {
            foreach (var node in hikNodes)
            {
                if (node == null)
                    continue;
                Undo.RecordObject(node, "重建海康邻接");
                var snapshot = new List<PathNode>(node.Neighbors);
                for (int i = 0; i < snapshot.Count; i++)
                {
                    var other = snapshot[i];
                    if (other != null && hikNodes.Contains(other))
                    {
                        node.RemoveNeighbor(other);
                        other.RemoveNeighbor(node);
                    }
                }
            }

            for (int i = 0; i < cfg.Points.Count; i++)
            {
                var point = cfg.Points[i];
                if (!byPointId.TryGetValue(point.Id, out var from) || from == null)
                    continue;
                for (int n = 0; n < point.Neighbors.Count; n++)
                {
                    int toId = point.Neighbors[n].Id;
                    if (!byPointId.TryGetValue(toId, out var to) || to == null || to == from)
                        continue;
                    from.Connect(to, bidirectional: false);
                }
            }
        }
    }
}
