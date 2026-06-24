using System.Collections.Generic;
using UnityEngine;
using NonsensicalKit.Simulation.WarehouseSimulation.Allocation;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Playback
{
    /// <summary>场景中存放回放事件与子任务时间轴，可由仿真运行器写入。</summary>
    public sealed class SimPlaybackEventSource : MonoBehaviour
    {
        [SerializeField] private List<SimPlaybackEvent> m_events = new();
        [SerializeField] private List<SimSubTask> m_subTasks = new();
        [SerializeField] private List<GridIndex> m_initialOccupiedSlots = new();
        [SerializeField] private double m_totalSimSeconds;

        private WarehouseGridConfig _gridConfig;
        private IWarehouseSimulationBindings _hardwareBindings;
        private IReadOnlyList<SimPlaybackEvent> _externalEvents;
        private IReadOnlyList<SimSubTask> _externalSubTasks;

        public IReadOnlyList<SimPlaybackEvent> Events => _externalEvents ?? m_events;
        public IReadOnlyList<SimSubTask> SubTasks => _externalSubTasks ?? m_subTasks;

        /// <summary>仿真开始时已占用的货位（供回放 t=0 播种）。</summary>
        public IReadOnlyList<GridIndex> InitialOccupiedSlots => m_initialOccupiedSlots;

        /// <summary>最近一次仿真使用的网格配置（与 Runner 一致，供回放过滤排除区）。</summary>
        public WarehouseGridConfig GridConfig => _gridConfig;

        /// <summary>最近一次仿真使用的硬件绑定（供回放推导交互点排除区）。</summary>
        public IWarehouseSimulationBindings HardwareBindings => _hardwareBindings;

        /// <summary>最近一次仿真的逻辑时钟终点（与 <see cref="SimRunResult.TotalSimSeconds"/> 一致）。</summary>
        public double TotalSimSeconds => m_totalSimSeconds;

        /// <summary>用仿真输出覆盖当前事件列表（供回放控制器读取）。</summary>
        public void SetEvents(IReadOnlyList<SimPlaybackEvent> events)
        {
            SetTimeline(events, null);
        }

        /// <summary>用仿真输出覆盖回放事件与子任务时间轴。</summary>
        public void SetTimeline(
            IReadOnlyList<SimPlaybackEvent> events,
            IReadOnlyList<SimSubTask> subTasks,
            IReadOnlyList<GridIndex> initialOccupiedSlots = null,
            WarehouseGridConfig gridConfig = null,
            IWarehouseSimulationBindings hardwareBindings = null,
            double totalSimSeconds = 0d)
        {
            _externalEvents = null;
            _externalSubTasks = null;
            ApplyTimelineMetadata(initialOccupiedSlots, gridConfig, hardwareBindings, totalSimSeconds);

            m_events.Clear();
            if (events != null)
            {
                m_events.AddRange(events);
            }

            m_subTasks.Clear();
            if (subTasks != null)
            {
                m_subTasks.AddRange(subTasks);
            }
        }

        /// <summary>
        /// 引用仿真器内存中的时间轴而不拷贝（大批量时避免主线程二次复制）。
        /// 仿真器实例需在本组件生命周期内保持有效。
        /// </summary>
        public void AttachRunTimeline(
            IReadOnlyList<SimPlaybackEvent> events,
            IReadOnlyList<SimSubTask> subTasks,
            IReadOnlyList<GridIndex> initialOccupiedSlots = null,
            WarehouseGridConfig gridConfig = null,
            IWarehouseSimulationBindings hardwareBindings = null,
            double totalSimSeconds = 0d)
        {
            _externalEvents = events;
            _externalSubTasks = subTasks;
            ApplyTimelineMetadata(initialOccupiedSlots, gridConfig, hardwareBindings, totalSimSeconds);
            m_events.Clear();
            m_subTasks.Clear();
        }

        private void ApplyTimelineMetadata(
            IReadOnlyList<GridIndex> initialOccupiedSlots,
            WarehouseGridConfig gridConfig,
            IWarehouseSimulationBindings hardwareBindings,
            double totalSimSeconds)
        {
            m_initialOccupiedSlots.Clear();
            if (initialOccupiedSlots != null)
            {
                m_initialOccupiedSlots.AddRange(initialOccupiedSlots);
            }

            _gridConfig = gridConfig;
            _hardwareBindings = hardwareBindings;
            m_totalSimSeconds = totalSimSeconds;
        }
    }
}
