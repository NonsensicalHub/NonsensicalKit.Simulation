using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation.Editor
{
    /// <summary>路网编辑操作（Inspector / 路网配置窗口共用）。</summary>
    public static class PathNetworkEditActions
    {
        public static void CollectNodes(PathNetwork network)
        {
            Undo.RecordObject(network, "Collect Path Nodes");
            network.CollectNodesFromChildren();
            EditorUtility.SetDirty(network);
            PathNetworkGraphWindow.NotifyNetworkChanged(network);
        }

        public static void AutoLinkNeighbors(PathNetwork network)
        {
            Undo.RegisterFullObjectHierarchyUndo(network.gameObject, "Auto Link Path Nodes");
            network.AutoLinkNeighbors();
            EditorUtility.SetDirty(network);
            MarkNodesDirty(network);
        }

        public static void ClearAllNeighbors(PathNetwork network)
        {
            Undo.RegisterFullObjectHierarchyUndo(network.gameObject, "Clear Path Neighbors");
            network.ClearAllNeighbors();
            EditorUtility.SetDirty(network);
            MarkNodesDirty(network);
        }

        public static void ClearInvalidNeighbors(PathNetwork network)
        {
            Undo.RegisterFullObjectHierarchyUndo(network.gameObject, "Clear Invalid Path Neighbors");
            network.ClearInvalidNeighbors();
            EditorUtility.SetDirty(network);
            MarkNodesDirty(network);
        }

        public static void DisconnectNodeNeighbors(PathNode node)
        {
            if (node == null)
                return;

            var network = node.Network;
            if (network != null)
                Undo.RegisterFullObjectHierarchyUndo(network.gameObject, "Clear Node Neighbors");
            else
                Undo.RecordObject(node, "Clear Node Neighbors");

            node.ClearNeighbors();
            EditorUtility.SetDirty(node);
            if (network != null)
            {
                EditorUtility.SetDirty(network);
                MarkNodesDirty(network);
            }

            SceneView.RepaintAll();
        }

        public static void ClearNodeInvalidNeighbors(PathNode node)
        {
            if (node == null)
                return;

            var network = node.Network;
            if (network != null)
                Undo.RegisterFullObjectHierarchyUndo(network.gameObject, "Clear Invalid Node Neighbors");
            else
                Undo.RecordObject(node, "Clear Invalid Node Neighbors");

            int removed = node.RemoveInvalidNeighbors(network);
            EditorUtility.SetDirty(node);
            if (network != null)
            {
                EditorUtility.SetDirty(network);
                MarkNodesDirty(network);
            }

            SceneView.RepaintAll();
            Debug.Log(
                $"[{(network != null ? network.name : node.name)}] 清除错误邻居：节点 {node.name}，移除 {removed}。",
                node);
        }

        public static bool CheckReachabilityHealth(PathNetwork network)
        {
            bool healthy = network.CheckReachabilityHealth();
            if (healthy)
            {
                EditorUtility.DisplayDialog(
                    "路网健康检测",
                    "通过：每个节点都能沿有向边到达其他所有节点。\n详情见 Console。",
                    "OK");
            }
            else
            {
                EditorUtility.DisplayDialog(
                    "路网健康检测",
                    "未通过：存在不可达点对（或空节点）。\n请查看 Console 中的详细报告。",
                    "OK");
            }

            return healthy;
        }

        public static void SortNodesByPosition(PathNetwork network)
        {
            Undo.RegisterFullObjectHierarchyUndo(network.gameObject, "Sort Path Nodes By Position");
            network.SortNodesByPosition();
            EditorUtility.SetDirty(network);
        }

        public static void SortNodesByName(PathNetwork network)
        {
            Undo.RegisterFullObjectHierarchyUndo(network.gameObject, "Sort Path Nodes By Name");
            network.SortNodesByName();
            EditorUtility.SetDirty(network);
        }

        public static void AutoRenameNodes(PathNetwork network, SerializedObject serializedObject)
        {
            foreach (var node in network.Nodes)
            {
                if (node != null)
                    Undo.RecordObject(node.gameObject, "Auto Rename Path Nodes");
            }

            Undo.RecordObject(network, "Auto Rename Path Nodes");
            network.AutoRenameNodes();
            EditorUtility.SetDirty(network);
            foreach (var node in network.Nodes)
            {
                if (node != null)
                    EditorUtility.SetDirty(node.gameObject);
            }

            serializedObject.Update();
        }

        public static void ApplyShortestPathOneWay(PathNetwork network, PathNode start, PathNode goal)
        {
            Undo.RegisterFullObjectHierarchyUndo(network.gameObject, "Make Shortest Path One-Way");
            var path = new List<PathNode>();
            int changed = network.MakeShortestPathOneWay(start, goal, path);
            if (changed < 0)
            {
                EditorUtility.DisplayDialog(
                    "路径编辑",
                    $"找不到从 {start.name} 到 {goal.name} 之间的路径。请先自动连边。",
                    "OK");
                return;
            }

            MarkNodesDirty(network);
            EditorUtility.SetDirty(network);
            SceneView.RepaintAll();
            Debug.Log(
                $"[{network.name}] 单向道：{start.name} → {goal.name}，路径节点 {path.Count}，边改写 {changed}。",
                network);
        }

        public static void ApplyDisconnectShortestPath(PathNetwork network, PathNode start, PathNode goal)
        {
            Undo.RegisterFullObjectHierarchyUndo(network.gameObject, "Disconnect Shortest Path");
            var path = new List<PathNode>();
            int removed = network.DisconnectShortestPath(start, goal, path);
            if (removed < 0)
            {
                EditorUtility.DisplayDialog(
                    "路径编辑",
                    $"找不到从 {start.name} 到 {goal.name} 之间的路径。请先自动连边。",
                    "OK");
                return;
            }

            MarkNodesDirty(network);
            EditorUtility.SetDirty(network);
            SceneView.RepaintAll();
            Debug.Log(
                $"[{network.name}] 断开最短连线：{start.name} → {goal.name}，路径节点 {path.Count}，边清除 {removed}。",
                network);
        }

        public static void ApplyInsertNodeBetween(PathNetwork network, PathNode start, PathNode goal)
        {
            bool connected = start.IsConnectedTo(goal) || goal.IsConnectedTo(start);
            if (!connected)
            {
                EditorUtility.DisplayDialog(
                    "路径编辑",
                    $"{start.name} 与 {goal.name} 没有直接连线。插入节点只作用于相邻的两点。",
                    "OK");
                return;
            }

            Undo.RegisterFullObjectHierarchyUndo(network.gameObject, "Insert Path Node Between");
            var inserted = network.InsertNodeBetween(start, goal);
            if (inserted == null)
            {
                EditorUtility.DisplayDialog(
                    "路径编辑",
                    $"无法在 {start.name} 与 {goal.name} 之间插入节点。",
                    "OK");
                return;
            }

            Undo.RegisterCreatedObjectUndo(inserted.gameObject, "Insert Path Node Between");
            MarkNodesDirty(network);
            EditorUtility.SetDirty(network);
            Selection.activeGameObject = inserted.gameObject;
            SceneView.RepaintAll();
            Debug.Log(
                $"[{network.name}] 插入节点：{start.name} — {inserted.name} — {goal.name}。",
                network);
        }

        public static void MarkNodesDirty(PathNetwork network)
        {
            if (network == null)
                return;

            foreach (var node in network.Nodes)
            {
                if (node != null)
                    EditorUtility.SetDirty(node);
            }

            PathNetworkGraphWindow.NotifyNetworkChanged(network);
        }
    }
}
