using System;
using NaughtyAttributes;
using UnityEngine;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Config
{
    /// <summary>输送网节点：入库口、路口或堆垛机交互点及其仿真参数。</summary>
    [Serializable]
    public struct SimConveyorMapNode
    {
        [Tooltip("用户可读标识：地图显示、日志与场景锚点。可修改，修改后不影响连线。")]
        [Label("逻辑 ID")]
        public string LogicalId;

        [Tooltip("创建节点时自动分配的 GUID，路段 From/To 引用此字段。创建后不可修改。")]
        [Label("节点 ID")]
        public string NodeId;

        [Label("节点类型")]
        public SimConveyorNodeKind Kind;

        [Header("入库口")]
        [Tooltip("≤0 使用 Profile.InfeedServiceSeconds；计入该口「入库放货」子任务的服务耗时")]
        [Label("入库放货服务时间（秒）")]
        public float InfeedServiceSeconds;

        [Header("出库口")]
        [Tooltip("≤0 使用 Profile.OutfeedServiceSeconds；计入该口「出库发运」子任务的服务耗时")]
        [Label("出库发运服务时间（秒）")]
        public float OutfeedServiceSeconds;

        [Header("加工站点")]
        [Label("加工模式")]
        public SimConveyorProcessMode ProcessMode;
        [Tooltip("货物需匹配的加工标签（如 wrap 表示缠膜）。仅当任务带有相同标签时才在此停留加工。")]
        [Label("加工标签")]
        public string ProcessTag;
        [Tooltip("≤0 使用 Profile.ProcessStationServiceSeconds；停留加工模式的单次服务耗时")]
        [Label("加工服务时间（秒）")]
        public float ProcessServiceSeconds;

        [Header("垂直提升机")]
        [Tooltip("同一物理提升机各层节点共用此 ID，用于设备互斥预约；留空则使用本节点逻辑 ID")]
        [Label("提升机组 ID")]
        public string TransferGroupId;
        [Tooltip("≤0 使用 Profile.VerticalTransferSeconds；货物跨层提升的耗时")]
        [Label("提升时间（秒）")]
        public float TransferSeconds;
        [Tooltip("跨层动画目标节点逻辑 ID；留空则使用路径上下一节点的场景锚点")]
        [Label("目标层节点")]
        public string TransferTargetLogicalId;
        [Label("移动动画模式")]
        public SimConveyorVerticalTransferMotion TransferMotion;

        [Header("容量")]
        [Tooltip("出库口：出库口前输送路段排队上限；堆垛机交互点：同时进入输送的最大任务数。≤0 则使用 Profile 全局值（MaxOutfeedQueuePerPort / MaxPickupReservationsPerPoint）。")]
        [Label("最大排队数（≤0 用全局）")]
        public int MaxReservations;

        [Header("堆垛机交互点")]
        [Label("交互模式")]
        public SimStackerInteractionMode StackerInteractionMode;
        [Min(0)]
        [Label("堆垛机编号")]
        public int StackerId;
        [Tooltip("堆垛机旁货位列号（输送终点 / 伸叉取货列）。双向堆垛机可左右各配 1 个交互点；单向堆垛机通常仅 1 个，且列号须落在该堆垛机伸叉列域内")]
        [Label("交互列")]
        public int PickupColumn;
        [Label("交互排")]
        public int PickupRow;

        [Header("编辑器")]
        [Tooltip("节点在 GraphView 中的排版坐标，仅用于可视化，不参与仿真")]
        [Label("画布位置")]
        public Vector2 EditorPosition;
    }
}
