using System;
using NaughtyAttributes;
using UnityEngine;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Config
{
    /// <summary>输送网络拓扑：节点、有向边及默认速度与箱长（用于路段容量）。</summary>
    [CreateAssetMenu(fileName = "WarehouseConveyorMap", menuName = "Warehouse Simulation/Conveyor Map")]
    public class WarehouseConveyorMap : ScriptableObject
    {
        [Tooltip("输送网所有节点：入库口、路口、堆垛机交互点")]
        [Label("节点列表")]
        public SimConveyorMapNode[] Nodes;

        [Tooltip("有向路段：路口间默认双向（A→B 与 B→A 各一条），也可仅保留单向；距离与可选速度覆盖")]
        [Label("路段列表")]
        public SimConveyorMapEdge[] Edges;

        [Header("输送物理参数")]
        [Tooltip("默认输送速度（米/秒），用于 时间 = 距离 ÷ 速度")]
        [Label("默认输送速度（米/秒）")]
        public float DefaultSpeedMetersPerSecond = 0.6f;

        [Tooltip("单箱在输送线上的占用长度 / 碰撞长度（米），用于路段容量与跟车间距")]
        [Label("单箱占用长度（米）")]
        public float CargoUnitLengthMeters = 1.2f;

        [Tooltip("新建路段时的默认输送距离（米），与编辑器布局无关")]
        [Label("新建路段默认距离（米）")]
        public float DefaultEdgeDistanceMeters = 4f;

        /// <summary>路段输送时间（秒）= 距离 ÷ 有效速度。</summary>
        public float GetEdgeTransitSeconds(SimConveyorMapEdge edge) =>
            ConveyorMapMath.GetTransitSeconds(this, edge);

        /// <summary>路段可同时容纳的箱数（由路段长度与单箱占用长度推算）。</summary>
        public int GetEdgeCapacity(SimConveyorMapEdge edge) =>
            ConveyorMapMath.GetSegmentCapacity(this, edge);

#if UNITY_EDITOR
        public Vector2 GetNodePosition(string nodeId, int nodeIndex, float defaultColumnSpacing = 180f, float defaultRowSpacing = 100f)
        {
            if (Nodes != null && nodeIndex >= 0 && nodeIndex < Nodes.Length)
            {
                return Nodes[nodeIndex].EditorPosition;
            }

            if (!string.IsNullOrEmpty(nodeId) && Nodes != null)
            {
                foreach (var node in Nodes)
                {
                    if (node.NodeId == nodeId)
                    {
                        return node.EditorPosition;
                    }
                }
            }

            var col = Math.Max(nodeIndex, 0) % 4;
            var row = Math.Max(nodeIndex, 0) / 4;
            return new Vector2(40f + col * defaultColumnSpacing, 40f + row * defaultRowSpacing);
        }

        public void SetNodePosition(string nodeId, Vector2 position)
        {
            if (string.IsNullOrEmpty(nodeId) || Nodes == null)
            {
                return;
            }

            for (var i = 0; i < Nodes.Length; i++)
            {
                if (Nodes[i].NodeId != nodeId)
                {
                    continue;
                }

                var node = Nodes[i];
                node.EditorPosition = position;
                Nodes[i] = node;
                return;
            }
        }
#endif
    }
}
