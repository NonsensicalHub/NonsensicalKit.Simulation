using System.IO;
using NaughtyAttributes;
using NonsensicalKit.DigitalTwin.Warehouse;
using UnityEngine;
using NonsensicalKit.Simulation.WarehouseSimulation.Allocation;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Runtime
{
    /// <summary>
    /// 生成与仿真网格尺寸一致的 WarehouseManager 数据文件。
    /// </summary>
    public sealed class WarehouseSimulationTestDataBootstrap : MonoBehaviour
    {
        [SerializeField, Label("仓库数据名（不含扩展名）")]
        private string m_warehouseName = "SimulationTest";

        [SerializeField, Label("网格配置")]
        private WarehouseGridConfig m_grid = new()
        {
            LevelCount = 8,
            ColumnCount = 12,
            RowCount = 10,
        };

        [SerializeField, Label("货位轴向坐标")]
        private WarehouseSlotAxisProfile m_axisProfile = new();

        [SerializeField, Label("硬件绑定（自动排除交互点列）")]
        private DefaultWarehouseSimulationBindingsAsset m_bindings;

        [Button("按网格尺寸初始化坐标数组")]
        public void SyncAxisProfileToGrid()
        {
            m_axisProfile ??= new WarehouseSlotAxisProfile();
            m_axisProfile.EnsureSize(m_grid.RowCount, m_grid.ColumnCount, m_grid.LevelCount);
            Debug.Log(
                $"[WarehouseSimulation] 已按网格 {m_grid.LevelCount} 层 × {m_grid.ColumnCount} 列 × {m_grid.RowCount} 排初始化坐标数组。",
                this);
        }

        [Button("生成 StreamingAssets 仓库数据")]
        public void CreateWarehouseDataFile()
        {
            if (string.IsNullOrWhiteSpace(m_warehouseName))
            {
                Debug.LogError("[WarehouseSimulation] 仓库数据名不能为空。", this);
                return;
            }

            m_axisProfile ??= new WarehouseSlotAxisProfile();
            m_axisProfile.EnsureSize(m_grid.RowCount, m_grid.ColumnCount, m_grid.LevelCount);

            if (!m_axisProfile.TryBuildWarehouseData(m_grid, depthCount: 1, m_bindings, out var data, out var error))
            {
                Debug.LogError($"[WarehouseSimulation] 生成仓库数据失败：{error}", this);
                return;
            }

            var physicalCount = m_grid.LevelCount * m_grid.ColumnCount * m_grid.RowCount;
            var excludedCount = physicalCount - data.Bins.Length;

            var directory = Path.Combine(Application.streamingAssetsPath, "Warehouse");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"{m_warehouseName}.dat");
            BinDataIO.SaveSync(data.Bins, path);

            Debug.Log(
                $"[WarehouseSimulation] 已生成仓库数据：{path}\n" +
                $"  尺寸：{m_grid.LevelCount} 层 × {m_grid.ColumnCount} 列 × {m_grid.RowCount} 排\n" +
                $"  货位：{data.Bins.Length} 个存储位" +
                (excludedCount > 0 ? $"（已排除底层交互区等 {excludedCount} 个非存储格）" : string.Empty),
                this);
        }
    }
}
