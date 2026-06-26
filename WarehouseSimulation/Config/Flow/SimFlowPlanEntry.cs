using System;
using NaughtyAttributes;
using UnityEngine;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Config
{
    /// <summary>
    /// 流程计划中的一段：指定方向、数量与释放节奏。
    /// 多段计划按数组顺序独立调度，可交错模拟真实出入库场景。
    /// </summary>
    [Serializable]
    public class SimFlowPlanEntry
    {
        [Label("流向")]
        public SimFlowDirection Direction = SimFlowDirection.Inbound;

        [Min(1)]
        [Label("数量")]
        public int Quantity = 100;

        [Min(0f)]
        [Label("起始延迟（秒）")]
        [Tooltip("本段第一条货物相对仿真 t=0 的延迟。")]
        public float StartDelaySeconds = 0f;

        [Label("释放节奏")]
        public SimFlowScheduleMode ScheduleMode = SimFlowScheduleMode.Instant;

        [Min(1)]
        [Label("随机数量下限")]
        [Tooltip("仅「分次释放」模式生效；与上限相等时为固定批量。")]
        public int RandomQuantityMin = 1;

        [Min(1)]
        [Label("随机数量上限")]
        [Tooltip("仅「分次释放」模式生效；与下限相等时为固定批量。")]
        public int RandomQuantityMax = 10;

        [Min(0.01f)]
        [Label("随机间隔下限（秒）")]
        [Tooltip("仅「分次释放」模式生效；与上限相等时为固定间隔。")]
        public float RandomIntervalMinSeconds = 5f;

        [Min(0.01f)]
        [Label("随机间隔上限（秒）")]
        [Tooltip("仅「分次释放」模式生效；与下限相等时为固定间隔。")]
        public float RandomIntervalMaxSeconds = 15f;

        [Label("加工需求标签")]
        [Tooltip("本段货物需经过的加工站点标签（如 wrap 表示需缠膜）。留空表示无特殊加工要求。")]
        public string[] RequiredProcessTags;
    }
}
