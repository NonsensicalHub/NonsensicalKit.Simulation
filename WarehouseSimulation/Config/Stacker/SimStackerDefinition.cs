using System;
using NaughtyAttributes;
using UnityEngine;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Config
{
    /// <summary>单台堆垛机的巷道列域与运动学参数。</summary>
    [Serializable]
    public struct SimStackerDefinition
    {
        [Label("堆垛机编号")]
        public int StackerId;

        [Tooltip("留空则使用 Fleet 默认运动学")]
        [Label("运动学")]
        public StackerKinematicsConfig Kinematics;

        [Tooltip("巷道左货位列；轨道布置在该列与下一列之间的空隙，无独立轨道列号")]
        [Label("巷道左列")]
        public int AisleLeftColumn;

        [Label("列向伸叉覆盖")]
        public SimStackerColumnReach ColumnReach;

        [Tooltip("列向伸叉为 1 列（单向）时有效：堆垛机仅服务的巷道侧别")]
        [Label("单向服务侧别")]
        public SimStackerServingSide ServingSide;
    }
}
