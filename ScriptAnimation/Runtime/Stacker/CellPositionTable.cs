using System;
using NonsensicalKit.Core;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 货位坐标（层/列/排/深）→世界位置表。
    /// 无 <see cref="NonsensicalKit.DigitalTwin.Warehouse.WarehouseManager"/> 时由堆垛机使用本表解析坐标。
    /// </summary>
    [AddComponentMenu("ScriptAnimation/货位坐标表 (CellPositionTable)")]
    public class CellPositionTable : MonoBehaviour
    {
        [Serializable]
        public struct Entry
        {
            [Tooltip("层 列 排 深")]
            [InspectorLabel("货位坐标")]
            public Int4 Cell;
            [InspectorLabel("世界坐标")]
            public Vector3 WorldPosition;
        }

        [InspectorLabel("货位条目")]
        [SerializeField] private Entry[] m_entries = Array.Empty<Entry>();

        public bool TryGet(Int4 cell, out Vector3 worldPos)
        {
            if (m_entries != null)
            {
                for (int i = 0; i < m_entries.Length; i++)
                {
                    if (m_entries[i].Cell.Equals(cell))
                    {
                        worldPos = m_entries[i].WorldPosition;
                        return true;
                    }
                }
            }

            worldPos = default;
            return false;
        }

        public void SetEntries(Entry[] entries)
        {
            m_entries = entries ?? Array.Empty<Entry>();
        }
    }
}
