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

            return dirty;
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
