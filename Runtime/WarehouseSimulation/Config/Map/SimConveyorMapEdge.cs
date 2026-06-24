using System;
using NaughtyAttributes;
using UnityEngine;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Config
{
    /// <summary>输送网有向边：起终点、距离与可选速度覆盖。</summary>
    [Serializable]
    public struct SimConveyorMapEdge
    {
        [Tooltip("起点节点的内部 ID（GUID），非逻辑 ID")]
        [Label("起点节点 ID")]
        public string FromNodeId;
        [Tooltip("终点节点的内部 ID（GUID），非逻辑 ID")]
        [Label("终点节点 ID")]
        public string ToNodeId;

        [Tooltip("路段长度（米）；输送时间 = 距离 ÷ 速度")]
        [Label("距离（米）")]
        public float DistanceMeters;

        [Tooltip("≤0 使用地图 DefaultSpeedMetersPerSecond")]
        [Label("速度覆盖（米/秒）")]
        public float SpeedOverrideMetersPerSecond;
    }
}
