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

        [Header("中间门")]
        [Tooltip("货物前端到达该路段中间门前方的停留点后，等待开门的秒数。≤0 表示这段没有门。后车在该停留点被挡住，直到门打开、前车离开。")]
        [Label("开门等待（秒）")]
        [Min(0f)]
        public float GateWaitSeconds;

        [Tooltip("沿当前行进方向，靠近 0 为起点侧，靠近 1 为终点侧。填 0 时按路段中点。双向路段会把反向位置对称过去；正向在终点时，反向落在起点侧，而不是中点。")]
        [Label("门位置（0–1）")]
        [Range(0f, 1f)]
        public float GateNormalizedPosition;

        [Header("加工专用")]
        [Tooltip("留空表示普通路段，任何货物都可以走。填写后（如 wrap），只有任务加工标签包含该值的货物才能走这条路段。")]
        [Label("加工专用标签")]
        public string ProcessTag;
    }
}
