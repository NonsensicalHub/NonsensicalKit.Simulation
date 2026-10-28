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
        [Tooltip("占用路口逻辑 ID（如 LR1104）。货物前往本加工站时，首次经过该路口独占占用，再次经过时释放，避免死胡同进站对向争用。留空则不启用。")]
        [Label("占用路口（逻辑 ID）")]
        public string ProcessOccupiedJunctionLogicalId;
        [Tooltip("本加工站允许作为起点的入库口逻辑 ID 列表。带加工标签的入库任务会收集匹配站的并集，只从并集内选口；留空表示该站不限制入库口。旧地图默认空列表，行为与改前一致。")]
        [Label("支持的入库口")]
        public string[] SupportedInfeedLogicalIds;

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
        [Tooltip("出入库皆可：同一点既能入库交接也能出库交接；只能入库 / 只能出库：保持单向。")]
        [Label("交互模式")]
        public SimStackerInteractionMode StackerInteractionMode;
        [Min(0)]
        [Label("堆垛机编号")]
        public int StackerId;
        [Tooltip("堆垛机旁参考货位列，用于货位距离和底层排除。允许巷道左右浅位，以及 ColumnReach=FourColumns 时的左右深位（AisleLeft-1 / AisleLeft+2）。交互点可以不落在某一列中心，货叉伸出由伸叉深度（米）决定。")]
        [Label("参考列")]
        public int PickupColumn;
        [Tooltip("相对巷道中心的货叉伸出距离（米），原样灌入货叉关节（正负即关节正方向）。0 表示仍按参考列的货位坐标伸叉。")]
        [Label("伸叉深度（米）")]
        public float ForkDepthMeters;
        [Label("交互排")]
        public int PickupRow;
        [Tooltip("堆垛机在交互点取放货的层号（与货位 Level 同索引）。石家庄双库输送线高度对应第 1 层，不是第 0 层（第 0 层在地面以下）。")]
        [Min(0)]
        [Label("交互层")]
        public int PickupLevel;

        [Header("编辑器")]
        [Tooltip("节点在俯视编辑器画布中的排版坐标，仅用于可视化，不参与仿真")]
        [Label("画布位置")]
        public Vector2 EditorPosition;
    }
}
