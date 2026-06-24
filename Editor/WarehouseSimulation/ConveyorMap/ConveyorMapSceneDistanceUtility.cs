#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Editor
{
    /// <summary>
    /// 根据场景中已摆放的节点锚点（GameObject 名称 = 逻辑 ID）反推地图路段距离。
    /// </summary>
    internal static class ConveyorMapSceneDistanceUtility
    {
        public static bool TryBuildAnchorIndex(
            Transform anchorRoot,
            out Dictionary<string, Transform> index,
            out string error)
        {
            index = new Dictionary<string, Transform>(StringComparer.Ordinal);
            if (anchorRoot == null)
            {
                error = "未指定场景锚点根节点。";
                return false;
            }

            var children = anchorRoot.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < children.Length; i++)
            {
                var anchor = children[i];
                if (anchor == null || anchor == anchorRoot)
                {
                    continue;
                }

                var anchorName = anchor.gameObject.name?.Trim();
                if (string.IsNullOrEmpty(anchorName))
                {
                    continue;
                }

                index[anchorName] = anchor;
            }

            if (index.Count == 0)
            {
                error = $"锚点根节点「{anchorRoot.name}」下未找到任何子物体。";
                return false;
            }

            error = null;
            return true;
        }

        public static Transform TryAutoFindAnchorRoot(WarehouseConveyorMap map)
        {
            if (map?.Nodes == null || map.Nodes.Length == 0)
            {
                return null;
            }

            var expected = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < map.Nodes.Length; i++)
            {
                expected.Add(SimEntityNaming.FormatLogicalId(map.Nodes[i], i));
            }

            Transform best = null;
            var bestScore = 0;
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
            {
                return null;
            }

            var roots = scene.GetRootGameObjects();
            for (var r = 0; r < roots.Length; r++)
            {
                var transforms = roots[r].GetComponentsInChildren<Transform>(true);
                for (var t = 0; t < transforms.Length; t++)
                {
                    var candidate = transforms[t];
                    var score = 0;
                    var childCount = candidate.childCount;
                    for (var c = 0; c < childCount; c++)
                    {
                        var childName = candidate.GetChild(c).name?.Trim();
                        if (!string.IsNullOrEmpty(childName) && expected.Contains(childName))
                        {
                            score++;
                        }
                    }

                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = candidate;
                    }
                }
            }

            return bestScore >= 2 ? best : null;
        }

        public static bool TryMeasureDistance(
            WarehouseConveyorMap map,
            IReadOnlyDictionary<string, Transform> anchors,
            string fromNodeId,
            string toNodeId,
            out float distanceMeters,
            out string error)
        {
            distanceMeters = 0f;
            error = null;

            if (map == null)
            {
                error = "地图资源为空。";
                return false;
            }

            if (anchors == null || anchors.Count == 0)
            {
                error = "场景锚点索引为空。";
                return false;
            }

            var fromLabel = SimEntityNaming.FormatLogicalId(map, fromNodeId);
            var toLabel = SimEntityNaming.FormatLogicalId(map, toNodeId);
            if (!anchors.TryGetValue(fromLabel, out var fromTransform))
            {
                error = $"场景中未找到起点锚点「{fromLabel}」。";
                return false;
            }

            if (!anchors.TryGetValue(toLabel, out var toTransform))
            {
                error = $"场景中未找到终点锚点「{toLabel}」。";
                return false;
            }

            distanceMeters = Vector3.Distance(fromTransform.position, toTransform.position);
            return true;
        }

        public static bool TryApplyEdgeDistanceFromScene(
            WarehouseConveyorMap map,
            Transform anchorRoot,
            string fromNodeId,
            string toNodeId,
            out float distanceMeters,
            out string message)
        {
            distanceMeters = 0f;
            message = null;

            if (map?.Edges == null)
            {
                message = "地图中没有路段。";
                return false;
            }

            if (!TryBuildAnchorIndex(anchorRoot, out var anchors, out var indexError))
            {
                message = indexError;
                return false;
            }

            if (!TryMeasureDistance(map, anchors, fromNodeId, toNodeId, out distanceMeters, out var measureError))
            {
                message = measureError;
                return false;
            }

            var from = fromNodeId?.Trim();
            var to = toNodeId?.Trim();
            var updated = false;
            for (var i = 0; i < map.Edges.Length; i++)
            {
                var edge = map.Edges[i];
                if (edge.FromNodeId?.Trim() != from || edge.ToNodeId?.Trim() != to)
                {
                    continue;
                }

                edge.DistanceMeters = distanceMeters;
                map.Edges[i] = edge;
                ConveyorMapEditorEdgeUtility.SyncReverseEdge(map, edge);
                updated = true;
                break;
            }

            if (!updated)
            {
                message = $"未找到路段 {SimEntityNaming.FormatLogicalId(map, from)} → {SimEntityNaming.FormatLogicalId(map, to)}。";
                return false;
            }

            var fromLabel = SimEntityNaming.FormatLogicalId(map, from);
            var toLabel = SimEntityNaming.FormatLogicalId(map, to);
            message = $"已从场景更新 {fromLabel} → {toLabel}：{distanceMeters:0.##} m";
            return true;
        }

        public static int ApplyAllEdgeDistancesFromScene(
            WarehouseConveyorMap map,
            Transform anchorRoot,
            out List<string> warnings)
        {
            warnings = new List<string>();
            if (map?.Edges == null || map.Edges.Length == 0)
            {
                warnings.Add("地图中没有路段。");
                return 0;
            }

            if (!TryBuildAnchorIndex(anchorRoot, out var anchors, out var indexError))
            {
                warnings.Add(indexError);
                return 0;
            }

            var updatedCount = 0;
            var syncedPairs = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < map.Edges.Length; i++)
            {
                var edge = map.Edges[i];
                var from = edge.FromNodeId?.Trim();
                var to = edge.ToNodeId?.Trim();
                if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
                {
                    continue;
                }

                var pairKey = ConveyorMapEditorEdgeUtility.DirectedSegmentKey(from, to);
                if (!syncedPairs.Add(pairKey))
                {
                    continue;
                }

                if (!TryMeasureDistance(map, anchors, from, to, out var distance, out var measureError))
                {
                    warnings.Add(measureError);
                    continue;
                }

                edge.DistanceMeters = distance;
                map.Edges[i] = edge;
                ConveyorMapEditorEdgeUtility.SyncReverseEdge(map, edge);
                updatedCount++;
            }

            return updatedCount;
        }
    }
}
#endif
