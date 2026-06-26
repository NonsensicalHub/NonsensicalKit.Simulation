using NaughtyAttributes;
using NonsensicalKit.DigitalTwin.Warehouse;
using UnityEngine;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Playback;
using NonsensicalKit.Simulation.WarehouseSimulation.Runtime.Runner;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Runtime.DigitalTwin
{
    /// <summary>
    /// 将仿真结束时的货位占用快照写入 <see cref="WarehouseManager"/>。
    /// </summary>
    public sealed class WarehouseSimulationApplyOccupancy : MonoBehaviour
    {
        [SerializeField, Label("数字孪生仓库")]
        private WarehouseManager m_warehouseManager;

        [SerializeField, Label("回放事件源")]
        private SimPlaybackEventSource m_eventSource;

        [SerializeField, Label("仿真 Runner（可选）")]
        private WarehouseSimRunner m_runner;

        [SerializeField, Label("移动阶段高亮目标货位")]
        private bool m_highlightOnStackerMove = true;

        private WarehouseManagerSlotVisualAdapter _adapter;

        [Button("应用最终货位占用")]
        public void ApplyFinalOccupancy()
        {
            if (!TryResolveSimTime(out var simTime))
            {
                Debug.LogError("[WarehouseSimulation] 无法确定仿真终点时刻，请先运行仿真。", this);
                return;
            }

            ApplyOccupancyAt(simTime);
        }

        [Button("应用 t=0 初始占用")]
        public void ApplyInitialOccupancy()
        {
            ApplyOccupancyAt(0d);
        }

        public void ApplyOccupancyAt(double simTime)
        {
            if (m_warehouseManager == null)
            {
                Debug.LogError("[WarehouseSimulation] 未配置 WarehouseManager。", this);
                return;
            }

            if (!m_warehouseManager.Inited)
            {
                Debug.LogWarning("[WarehouseSimulation] WarehouseManager 尚未初始化。", this);
                return;
            }

            if (m_eventSource?.SubTasks == null || m_eventSource.SubTasks.Count == 0)
            {
                Debug.LogError("[WarehouseSimulation] 回放事件源缺少子任务时间轴，请先运行仿真。", this);
                return;
            }

            var snapshot = WarehouseSlotPlaybackSnapshotBuilder.Build(
                simTime,
                m_eventSource.SubTasks,
                m_highlightOnStackerMove,
                m_eventSource.InitialOccupiedSlots);

            var adapter = EnsureAdapter();
            if (snapshot.OccupiedSlots != null)
            {
                adapter.ApplyOccupancyBatch(snapshot.OccupiedSlots, occupied: true);
            }

            if (snapshot.HighlightSlot.HasValue)
            {
                adapter.ApplyHighlight(snapshot.HighlightSlot.Value);
            }
            else
            {
                adapter.HideHighlight();
            }

            Debug.Log($"[WarehouseSimulation] 已应用 t={simTime:F2}s 货位占用（{snapshot.OccupiedSlots?.Count ?? 0} 格）。", this);
        }

        private WarehouseManagerSlotVisualAdapter EnsureAdapter() =>
            _adapter ??= new WarehouseManagerSlotVisualAdapter(m_warehouseManager, warnWhenNotReady: true);

        private bool TryResolveSimTime(out double simTime)
        {
            simTime = m_eventSource != null ? m_eventSource.TotalSimSeconds : 0d;
            if (simTime > 1e-9)
            {
                return true;
            }

            if (m_runner?.LastResult != null && m_runner.LastResult.TotalSimSeconds > 1e-9)
            {
                simTime = m_runner.LastResult.TotalSimSeconds;
                return true;
            }

            return false;
        }
    }
}
