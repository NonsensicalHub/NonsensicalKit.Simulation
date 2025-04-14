using System;
using NonsensicalKit.Core;
using NonsensicalKit.Tools;
using NonsensicalKit.Tools.ObjectPool;
using UnityEngine;
using UnityEngine.Serialization;

namespace NonsensicalKit.Simulation.ParametricModelingShelves
{
    public enum ShelvesPrefabType
    {
        BottomSupport,
        Pillar,
        Horizontal,
        Vertical,
        Center,
    }

    [Serializable]
    public abstract class ShelvesPrefabConfig
    {
        [FormerlySerializedAs("DefaultScale")] public Vector3 m_DefaultScale = Vector3.one;
        [FormerlySerializedAs("DefaultState")] public bool m_DefaultState;

        [FormerlySerializedAs("UseMinMax")] public bool m_UseMinMax = true;
        [FormerlySerializedAs("Min")] public Vector3Int m_Min;
        [FormerlySerializedAs("Max")] public Vector3Int m_Max;
        [FormerlySerializedAs("Include")] public Vector3Int[] m_Include;
        [FormerlySerializedAs("Exclude")] public Vector3Int[] m_Exclude;

        [FormerlySerializedAs("Buffer")] public Array3<bool> m_Buffer;

        public virtual void InitBuffer(Vector3Int cellCount, Vector3Int[] simpleExclude)
        {
            m_Buffer = new Array3<bool>(cellCount.x, cellCount.y, cellCount.z);
            if (m_DefaultState)
            {
                m_Buffer.Fill(true);
            }

            if (m_UseMinMax)
            {
                var max = Vector3Int.Max(Vector3Int.zero, new Vector3Int(cellCount.x - 1, cellCount.y - 1, cellCount.z - 1));
                m_Max.Clamp(Vector3Int.zero, max);
                m_Min.Clamp(Vector3Int.zero, m_Max);
                for (int x = m_Min.x; x <= m_Max.x; x++)
                {
                    for (int y = m_Min.y; y <= m_Max.y; y++)
                    {
                        for (int z = m_Min.z; z <= m_Max.z; z++)
                        {
                            m_Buffer[x, y, z] = !m_DefaultState;
                        }
                    }
                }
            }

            AnalyzeSimpleExclude(cellCount, simpleExclude);

            foreach (var item in m_Include)
            {
                if (item.x >= cellCount.x
                    || item.y >= cellCount.y
                    || item.z >= cellCount.z)
                {
                    continue;
                }

                if (item.x < 0)
                {
                    if (item.y < 0)
                    {
                        if (item.z < 0)
                        {
                            m_Buffer.Fill(true);
                            break;
                        }
                        else
                        {
                            for (int x = 0; x < cellCount.x; x++)
                            {
                                for (int y = 0; y < cellCount.y; y++)
                                {
                                    m_Buffer[x, y, item.z] = true;
                                }
                            }
                        }
                    }
                    else
                    {
                        if (item.z < 0)
                        {
                            for (int x = 0; x < cellCount.x; x++)
                            {
                                for (int z = 0; z < cellCount.z; z++)
                                {
                                    m_Buffer[x, item.y, z] = true;
                                }
                            }
                        }
                        else
                        {
                            for (int x = 0; x < cellCount.x; x++)
                            {
                                m_Buffer[x, item.y, item.z] = true;
                            }
                        }
                    }
                }
                else
                {
                    if (item.y < 0)
                    {
                        if (item.z < 0)
                        {
                            for (int y = 0; y < cellCount.y; y++)
                            {
                                for (int z = 0; z < cellCount.z; z++)
                                {
                                    m_Buffer[item.x, y, z] = true;
                                }
                            }
                        }
                        else
                        {
                            for (int y = 0; y < cellCount.y; y++)
                            {
                                m_Buffer[item.x, y, item.z] = true;
                            }
                        }
                    }
                    else
                    {
                        if (item.z < 0)
                        {
                            for (int z = 0; z < cellCount.z; z++)
                            {
                                m_Buffer[item.x, item.y, z] = true;
                            }
                        }
                        else
                        {
                            m_Buffer[item.x, item.y, item.z] = true;
                        }
                    }
                }
            }

            foreach (var item in m_Exclude)
            {
                if (item.x >= cellCount.x
                    || item.y >= cellCount.y
                    || item.z >= cellCount.z)
                {
                    continue;
                }

                if (item.x < 0)
                {
                    if (item.y < 0)
                    {
                        if (item.z < 0)
                        {
                            m_Buffer.Fill(false);
                            break;
                        }
                        else
                        {
                            for (int x = 0; x < cellCount.x; x++)
                            {
                                for (int y = 0; y < cellCount.y; y++)
                                {
                                    m_Buffer[x, y, item.z] = false;
                                }
                            }
                        }
                    }
                    else
                    {
                        if (item.z < 0)
                        {
                            for (int x = 0; x < cellCount.x; x++)
                            {
                                for (int z = 0; z < cellCount.z; z++)
                                {
                                    m_Buffer[x, item.y, z] = false;
                                }
                            }
                        }
                        else
                        {
                            for (int x = 0; x < cellCount.x; x++)
                            {
                                m_Buffer[x, item.y, item.z] = false;
                            }
                        }
                    }
                }
                else
                {
                    if (item.y < 0)
                    {
                        if (item.z < 0)
                        {
                            for (int y = 0; y < cellCount.y; y++)
                            {
                                for (int z = 0; z < cellCount.z; z++)
                                {
                                    m_Buffer[item.x, y, z] = false;
                                }
                            }
                        }
                        else
                        {
                            for (int y = 0; y < cellCount.y; y++)
                            {
                                m_Buffer[item.x, y, item.z] = false;
                            }
                        }
                    }
                    else
                    {
                        if (item.z < 0)
                        {
                            for (int z = 0; z < cellCount.z; z++)
                            {
                                m_Buffer[item.x, item.y, z] = false;
                            }
                        }
                        else
                        {
                            m_Buffer[item.x, item.y, item.z] = false;
                        }
                    }
                }
            }
        }

        protected abstract void AnalyzeSimpleExclude(Vector3Int cellCount, Vector3Int[] simpleExclude);

        public bool Check(int row, int column, int layer)
        {
            return m_Buffer[row, column, layer];
        }
    }

    [Serializable]
    public class ShelvesLoadPrefabConfig : ShelvesPrefabConfig
    {
        [FormerlySerializedAs("Prefab")] public GameObject m_Prefab;
        [FormerlySerializedAs("Bounds")] public Bounds m_Bounds;

        public override void InitBuffer(Vector3Int cellCount, Vector3Int[] simpleExclude)
        {
            base.InitBuffer(cellCount, simpleExclude);
            m_Bounds = m_Prefab.transform.BoundingBox();
        }

        protected override void AnalyzeSimpleExclude(Vector3Int cellCount, Vector3Int[] simpleExclude)
        {
            foreach (var item in simpleExclude)
            {
                if (item.x >= cellCount.x
                    || item.y >= cellCount.y
                    || item.z >= cellCount.z)
                {
                    continue;
                }

                if (item.x < 0)
                {
                    if (item.y < 0)
                    {
                        if (item.z < 0)
                        {
                            m_Buffer.Fill(false);
                            break;
                        }
                        else
                        {
                            for (int x = 0; x < cellCount.x; x++)
                            {
                                for (int y = 0; y < cellCount.y; y++)
                                {
                                    m_Buffer[x, y, item.z] = false;
                                }
                            }
                        }
                    }
                    else
                    {
                        if (item.z < 0)
                        {
                            for (int x = 0; x < cellCount.x; x++)
                            {
                                for (int z = 0; z < cellCount.z; z++)
                                {
                                    m_Buffer[x, item.y, z] = false;
                                }
                            }
                        }
                        else
                        {
                            for (int x = 0; x < cellCount.x; x++)
                            {
                                m_Buffer[x, item.y, item.z] = false;
                            }
                        }
                    }
                }
                else
                {
                    if (item.y < 0)
                    {
                        if (item.z < 0)
                        {
                            for (int y = 0; y < cellCount.y; y++)
                            {
                                for (int z = 0; z < cellCount.z; z++)
                                {
                                    m_Buffer[item.x, y, z] = false;
                                }
                            }
                        }
                        else
                        {
                            for (int y = 0; y < cellCount.y; y++)
                            {
                                m_Buffer[item.x, y, item.z] = false;
                            }
                        }
                    }
                    else
                    {
                        if (item.z < 0)
                        {
                            for (int z = 0; z < cellCount.z; z++)
                            {
                                m_Buffer[item.x, item.y, z] = false;
                            }
                        }
                        else
                        {
                            m_Buffer[item.x, item.y, item.z] = false;
                        }
                    }
                }
            }
        }
    }

    /// m*n*h的货架，有m*n*h个货位，有(m+1)*(n+1)*h个支柱，有（m+1）*(n+1)*h*2个横柱（横竖方向）
    [Serializable]
    public class ShelvesBuildPrefabConfig : ShelvesPrefabConfig
    {
        [FormerlySerializedAs("AutoSize")] public bool m_AutoSize;
        [FormerlySerializedAs("PrefabType")] public ShelvesPrefabType m_PrefabType;
        [FormerlySerializedAs("Pool")] public SerializableGameObjectPool m_Pool;
        [FormerlySerializedAs("OriginSize")] public Vector3 m_OriginSize = Vector3.one;

        public override void InitBuffer(Vector3Int cellCount, Vector3Int[] simpleExclude)
        {
            if (m_AutoSize)
            {
                m_OriginSize = m_Pool.Prefab.transform.BoundingBox().size;
            }

            cellCount.x++;
            cellCount.z++;

            base.InitBuffer(cellCount, simpleExclude);

            if (m_PrefabType == ShelvesPrefabType.Horizontal || m_PrefabType == ShelvesPrefabType.Vertical)
            {
                m_Buffer[cellCount.x - 1, cellCount.y - 1, cellCount.z - 1] = false;
            }
        }

        protected override void AnalyzeSimpleExclude(Vector3Int cellCount, Vector3Int[] simpleExclude)
        {
            if (simpleExclude.Length == 0)
            {
                return;
            }

            Array3<bool> simpleExcludeBuffer = new Array3<bool>(cellCount.x, cellCount.y, cellCount.z);
            simpleExcludeBuffer.Fill(true);
            foreach (var item in simpleExclude)
            {
                if (item.x >= cellCount.x
                    || item.y >= cellCount.y
                    || item.z >= cellCount.z)
                {
                    continue;
                }

                if (item.x < 0)
                {
                    if (item.y < 0)
                    {
                        if (item.z < 0)
                        {
                            simpleExcludeBuffer.Fill(false);
                            break;
                        }
                        else
                        {
                            for (int x = 0; x < cellCount.x; x++)
                            {
                                for (int y = 0; y < cellCount.y; y++)
                                {
                                    simpleExcludeBuffer[x, y, item.z] = false;
                                }
                            }
                        }
                    }
                    else
                    {
                        if (item.z < 0)
                        {
                            for (int x = 0; x < cellCount.x; x++)
                            {
                                for (int z = 0; z < cellCount.z; z++)
                                {
                                    simpleExcludeBuffer[x, item.y, z] = false;
                                }
                            }
                        }
                        else
                        {
                            for (int x = 0; x < cellCount.x; x++)
                            {
                                simpleExcludeBuffer[x, item.y, item.z] = false;
                            }
                        }
                    }
                }
                else
                {
                    if (item.y < 0)
                    {
                        if (item.z < 0)
                        {
                            for (int y = 0; y < cellCount.y; y++)
                            {
                                for (int z = 0; z < cellCount.z; z++)
                                {
                                    simpleExcludeBuffer[item.x, y, z] = false;
                                }
                            }
                        }
                        else
                        {
                            for (int y = 0; y < cellCount.y; y++)
                            {
                                simpleExcludeBuffer[item.x, y, item.z] = false;
                            }
                        }
                    }
                    else
                    {
                        if (item.z < 0)
                        {
                            for (int z = 0; z < cellCount.z; z++)
                            {
                                simpleExcludeBuffer[item.x, item.y, z] = false;
                            }
                        }
                        else
                        {
                            simpleExcludeBuffer[item.x, item.y, item.z] = false;
                        }
                    }
                }
            }

            for (int column = 0; column < m_Buffer.m_Length0; column++)
            {
                for (int layer = 0; layer < m_Buffer.m_Length1; layer++)
                {
                    for (int row = 0; row < m_Buffer.m_Length2; row++)
                    {
                        bool b = true;
                        switch (m_PrefabType)
                        {
                            case ShelvesPrefabType.BottomSupport:
                                b = simpleExcludeBuffer.SafeGet(column, 0, row) || simpleExcludeBuffer.SafeGet(column - 1, 0, row) ||
                                    simpleExcludeBuffer.SafeGet(column, 0, row - 1) || simpleExcludeBuffer.SafeGet(column - 1, 0, row - 1);
                                break;
                            case ShelvesPrefabType.Pillar:
                                b = simpleExcludeBuffer.SafeGet(column, layer, row) || simpleExcludeBuffer.SafeGet(column - 1, layer, row) ||
                                    simpleExcludeBuffer.SafeGet(column, layer, row - 1) || simpleExcludeBuffer.SafeGet(column - 1, layer, row - 1);
                                break;
                            case ShelvesPrefabType.Horizontal:
                                b = simpleExcludeBuffer.SafeGet(column, layer, row) || simpleExcludeBuffer.SafeGet(column, layer, row - 1);
                                break;
                            case ShelvesPrefabType.Vertical:
                                b = simpleExcludeBuffer.SafeGet(column, layer, row) || simpleExcludeBuffer.SafeGet(column - 1, layer, row);
                                break;
                            case ShelvesPrefabType.Center:
                                b = simpleExcludeBuffer.SafeGet(column, layer, row);
                                break;
                        }

                        if (!b)
                        {
                            m_Buffer[column, layer, row] = false;
                        }
                    }
                }
            }
        }
    }
}
