using System;
using NaughtyAttributes;
using UnityEngine;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Core
{
    /// <summary>堆垛机 fleet 拓扑。</summary>
    [Serializable]
    public sealed class StackerFleetConfig : IStackerFleetDescriptor
    {
        [Label("堆垛机数量")]
        public int StackerCount = 3;

        [Tooltip("2 列=双向伸叉；1 列=单向伸叉（需在各堆垛机定义中指定服务侧别）")]
        [Label("默认列向伸叉覆盖")]
        public SimStackerColumnReach DefaultStackerColumnReach = SimStackerColumnReach.TwoColumns;

        [Label("默认运动学")]
        public StackerKinematicsConfig DefaultKinematics = new();

        [Tooltip("留空则从输送地图堆垛机交互点推导巷道左列")]
        [Label("堆垛机列域定义")]
        public SimStackerDefinition[] StackerDefinitions;

        int IStackerFleetDescriptor.StackerCount => StackerCount;
        SimStackerColumnReach IStackerFleetDescriptor.DefaultStackerColumnReach => DefaultStackerColumnReach;
        SimStackerDefinition[] IStackerFleetDescriptor.StackerDefinitions => StackerDefinitions;
        IStackerKinematics IStackerFleetDescriptor.DefaultStackerKinematics => DefaultKinematics;

        public static StackerFleetConfig CreateDefault() => new();
    }
}
