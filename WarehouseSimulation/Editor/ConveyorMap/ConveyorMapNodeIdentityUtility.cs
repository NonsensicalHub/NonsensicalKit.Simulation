#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Editor
{
    /// <summary>
    /// 节点 ID（GUID）在创建时分配、不可修改，路段 <see cref="SimConveyorMapEdge.FromNodeId"/> 引用它。
    /// 逻辑 ID 供显示与用户编辑；修改逻辑 ID 不影响已有连线。
    /// </summary>
    internal static class ConveyorMapNodeIdentityUtility
    {
        /// <summary>补齐缺失的 GUID 与逻辑 ID，不改动已有字段。</summary>
        public static bool EnsureNodeIdentity(WarehouseConveyorMap map)
        {
            if (map?.Nodes == null || map.Nodes.Length == 0)
            {
                return false;
            }

            var usedLogicalIds = new HashSet<string>(StringComparer.Ordinal);
            var dirty = false;

            for (var i = 0; i < map.Nodes.Length; i++)
            {
                var node = map.Nodes[i];
                var logicalId = node.LogicalId?.Trim() ?? string.Empty;
                var changed = false;

                if (!string.IsNullOrEmpty(logicalId))
                {
                    usedLogicalIds.Add(logicalId);
                }

                if (string.IsNullOrEmpty(node.NodeId?.Trim()))
                {
                    node.NodeId = SimEntityNaming.NewNodeGuid();
                    changed = true;
                }

                if (string.IsNullOrEmpty(node.LogicalId?.Trim()))
                {
                    node.LogicalId = GenerateDefaultLogicalId(node.Kind, usedLogicalIds);
                    usedLogicalIds.Add(node.LogicalId);
                    changed = true;
                }

                if (changed)
                {
                    map.Nodes[i] = node;
                    dirty = true;
                }
            }

            if (dirty)
            {
                EditorUtility.SetDirty(map);
            }

            if (EnsureEdgeNodeIds(map))
            {
                dirty = true;
            }

            return dirty;
        }

        /// <summary>
        /// 将仍引用逻辑 ID 的旧路段端点迁移为节点 GUID，避免 GraphView 无法解析连线。
        /// </summary>
        public static bool EnsureEdgeNodeIds(WarehouseConveyorMap map)
        {
            if (map?.Nodes == null || map.Nodes.Length == 0 || map.Edges == null || map.Edges.Length == 0)
            {
                return false;
            }

            var nodeIdSet = new HashSet<string>(StringComparer.Ordinal);
            var logicalToNodeId = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < map.Nodes.Length; i++)
            {
                var nodeId = map.Nodes[i].NodeId?.Trim();
                var logicalId = map.Nodes[i].LogicalId?.Trim();
                if (!string.IsNullOrEmpty(nodeId))
                {
                    nodeIdSet.Add(nodeId);
                }

                if (!string.IsNullOrEmpty(logicalId) && !string.IsNullOrEmpty(nodeId))
                {
                    logicalToNodeId[logicalId] = nodeId;
                }
            }

            var dirty = false;
            var edges = map.Edges;
            for (var i = 0; i < edges.Length; i++)
            {
                var edge = edges[i];
                var from = edge.FromNodeId?.Trim();
                var to = edge.ToNodeId?.Trim();
                var changed = false;

                if (!string.IsNullOrEmpty(from)
                    && !nodeIdSet.Contains(from)
                    && logicalToNodeId.TryGetValue(from, out var resolvedFrom))
                {
                    from = resolvedFrom;
                    changed = true;
                }

                if (!string.IsNullOrEmpty(to)
                    && !nodeIdSet.Contains(to)
                    && logicalToNodeId.TryGetValue(to, out var resolvedTo))
                {
                    to = resolvedTo;
                    changed = true;
                }

                if (!changed)
                {
                    continue;
                }

                edge.FromNodeId = from;
                edge.ToNodeId = to;
                edges[i] = edge;
                dirty = true;
            }

            if (dirty)
            {
                map.Edges = edges;
                EditorUtility.SetDirty(map);
            }

            return dirty;
        }

        /// <summary>解析路段端点：优先 NodeId，回退逻辑 ID（兼容旧数据）。</summary>
        public static bool TryResolveNodeIndex(WarehouseConveyorMap map, string endpointId, out int nodeIndex)
        {
            nodeIndex = -1;
            if (map?.Nodes == null || string.IsNullOrWhiteSpace(endpointId))
            {
                return false;
            }

            var trimmed = endpointId.Trim();
            for (var i = 0; i < map.Nodes.Length; i++)
            {
                if (map.Nodes[i].NodeId?.Trim() == trimmed)
                {
                    nodeIndex = i;
                    return true;
                }
            }

            for (var i = 0; i < map.Nodes.Length; i++)
            {
                if (map.Nodes[i].LogicalId?.Trim() == trimmed)
                {
                    nodeIndex = i;
                    return true;
                }
            }

            return false;
        }

        public static string ResolveDisplayLabel(in SimConveyorMapNode node, int nodeIndex)
        {
            var logicalId = node.LogicalId?.Trim();
            return !string.IsNullOrEmpty(logicalId)
                ? logicalId
                : nodeIndex >= 0 ? $"n{nodeIndex}" : "—";
        }

        private static string GenerateDefaultLogicalId(SimConveyorNodeKind kind, HashSet<string> used)
        {
            for (var attempt = 0; attempt < 1000; attempt++)
            {
                var candidate = SimEntityNaming.NewLogicalId(kind, attempt);
                if (used.Add(candidate))
                {
                    return candidate;
                }
            }

            return SimEntityNaming.NewLogicalId(kind, used.Count);
        }
    }
}
#endif
