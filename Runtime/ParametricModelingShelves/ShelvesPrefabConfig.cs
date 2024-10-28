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

    [System.Serializable]
    public abstract class ShelvesPrefabConfig
    {
        public bool AutoSize;
        public Vector3 DefaultScale = Vector3.one;
        public bool DefaultState;

        public bool UseMinMax=true;
        public Vector3Int Min;
        public Vector3Int Max;
        public Vector3Int[] Include;
        public Vector3Int[] Exclude;

        public Array3<bool> Buffer;

        public virtual void InitBuffer(Vector3Int cellCount, Vector3Int[] simpleExclude)
        {
            Buffer = new Array3<bool>(cellCount.x, cellCount.y, cellCount.z);
            if (DefaultState)
            {
                Buffer.Reset(true);
            }
            if (UseMinMax)
            {
                var max = Vector3Int.Max(Vector3Int.zero, new Vector3Int(cellCount.x - 1, cellCount.y - 1, cellCount.z - 1));
                Max.Clamp(Vector3Int.zero, max);
                Min.Clamp(Vector3Int.zero, Max);
                for (int x = Min.x; x <= Max.x; x++)
                {
                    for (int y = Min.y; y <= Max.y; y++)
                    {
                        for (int z = Min.z; z <= Max.z; z++)
                        {
                            Buffer[x, y, z] = !DefaultState;
                        }
                    }
                }
            }

            AnalyzeSimpleExclude(cellCount, simpleExclude);

            foreach (var item in Include)
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
                            Buffer.Reset(true);
                            break;
                        }
                        else
                        {
                            for (int x = 0; x < cellCount.x; x++)
                            {
                                for (int y = 0; y < cellCount.y; y++)
                                {
                                    Buffer[x, y, item.z] = true;
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
                                    Buffer[x, item.y, z] = true;
                                }
                            }
                        }
                        else
                        {
                            for (int x = 0; x < cellCount.x; x++)
                            {
                                Buffer[x, item.y, item.z] = true;
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
                                    Buffer[item.x, y, z] = true;
                                }
                            }
                        }
                        else
                        {
                            for (int y = 0; y < cellCount.y; y++)
                            {
                                Buffer[item.x, y, item.z] = true;
                            }
                        }
                    }
                    else
                    {
                        if (item.z < 0)
                        {
                            for (int z = 0; z < cellCount.z; z++)
                            {
                                Buffer[item.x, item.y, z] = true;
                            }
                        }
                        else
                        {
                            Buffer[item.x, item.y, item.z] = true;
                        }
                    }
                }
            }

            foreach (var item in Exclude)
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
                            Buffer.Reset(false);
                            break;
                        }
                        else
                        {
                            for (int x = 0; x < cellCount.x; x++)
                            {
                                for (int y = 0; y < cellCount.y; y++)
                                {
                                    Buffer[x, y, item.z] = false;
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
                                    Buffer[x, item.y, z] = false;
                                }
                            }
                        }
                        else
                        {
                            for (int x = 0; x < cellCount.x; x++)
                            {
                                Buffer[x, item.y, item.z] = false;
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
                                    Buffer[item.x, y, z] = false;
                                }
                            }
                        }
                        else
                        {
                            for (int y = 0; y < cellCount.y; y++)
                            {
                                Buffer[item.x, y, item.z] = false;
                            }
                        }
                    }
                    else
                    {
                        if (item.z < 0)
                        {
                            for (int z = 0; z < cellCount.z; z++)
                            {
                                Buffer[item.x, item.y, z] = false;
                            }
                        }
                        else
                        {
                            Buffer[item.x, item.y, item.z] = false;
                        }
                    }
                }
            }
        }

        protected abstract void AnalyzeSimpleExclude(Vector3Int cellCount, Vector3Int[] simpleExclude);

        public bool Check(int row, int column, int layer)
        {
            return Buffer[row, column, layer];
        }
    }

    [System.Serializable]
    public class ShelvesLoadPrefabConfig : ShelvesPrefabConfig
    {
        public GameObject Prefab;
        public Bounds Bounds;

        public override void InitBuffer(Vector3Int cellCount, Vector3Int[] simpleExclude)
        {
            base.InitBuffer(cellCount, simpleExclude);
            Bounds = Prefab.transform.BoundingBox();
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
                            Buffer.Reset(false);
                            break;
                        }
                        else
                        {
                            for (int x = 0; x < cellCount.x; x++)
                            {
                                for (int y = 0; y < cellCount.y; y++)
                                {
                                    Buffer[x, y, item.z] = false;
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
                                    Buffer[x, item.y, z] = false;
                                }
                            }
                        }
                        else
                        {
                            for (int x = 0; x < cellCount.x; x++)
                            {
                                Buffer[x, item.y, item.z] = false;
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
                                    Buffer[item.x, y, z] = false;
                                }
                            }
                        }
                        else
                        {
                            for (int y = 0; y < cellCount.y; y++)
                            {
                                Buffer[item.x, y, item.z] = false;
                            }
                        }
                    }
                    else
                    {
                        if (item.z < 0)
                        {
                            for (int z = 0; z < cellCount.z; z++)
                            {
                                Buffer[item.x, item.y, z] = false;
                            }
                        }
                        else
                        {
                            Buffer[item.x, item.y, item.z] = false;
                        }
                    }
                }
            }
        }
    }

    /// m*n*h的货架，有m*n*h个货位，有(m+1)*(n+1)*h个支柱，有（m+1）*(n+1)*h*2个横柱（横竖方向）
    [System.Serializable]
    public class ShelvesBuildPrefabConfig : ShelvesPrefabConfig
    {
        public ShelvesPrefabType PrefabType;
        public SerializableGameobjectPool Pool;
        public Vector3 OriginSize = Vector3.one;

        public override void InitBuffer(Vector3Int cellCount, Vector3Int[] simpleExclude)
        {
            if (AutoSize)
            {
                var g = Pool.Prefab;
                OriginSize = TransformTool.BoundingBox(Pool.Prefab.transform).size;
            }

            cellCount.x++;
            cellCount.z++;

            base.InitBuffer(cellCount, simpleExclude);

            if (PrefabType == ShelvesPrefabType.Horizontal || PrefabType == ShelvesPrefabType.Vertical)
            {
                Buffer[cellCount.x - 1, cellCount.y - 1, cellCount.z - 1] = false;
            }
        }

        protected override void AnalyzeSimpleExclude(Vector3Int cellCount, Vector3Int[] simpleExclude)
        {
            if (simpleExclude.Length == 0)
            {
                return;
            }
            Array3<bool> simpleExcludeBuffer = new Array3<bool>(cellCount.x, cellCount.y, cellCount.z);
            simpleExcludeBuffer.Reset(true);
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
                            simpleExcludeBuffer.Reset(false);
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

            for (int column = 0; column < Buffer.Length0; column++)
            {
                for (int layer = 0; layer < Buffer.Length1; layer++)
                {
                    for (int row = 0; row < Buffer.Length2; row++)
                    {
                        bool b = true;
                        switch (PrefabType)
                        {
                            case ShelvesPrefabType.BottomSupport:
                                b = simpleExcludeBuffer.SafeGet(column, 0, row) || simpleExcludeBuffer.SafeGet(column - 1, 0, row) || simpleExcludeBuffer.SafeGet(column, 0, row - 1) || simpleExcludeBuffer.SafeGet(column - 1, 0, row - 1);
                                break;
                            case ShelvesPrefabType.Pillar:
                                b = simpleExcludeBuffer.SafeGet(column, layer, row) || simpleExcludeBuffer.SafeGet(column - 1, layer, row) || simpleExcludeBuffer.SafeGet(column, layer, row - 1) || simpleExcludeBuffer.SafeGet(column - 1, layer, row - 1);
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
                            Buffer[column, layer, row] = false;
                        }
                    }
                }
            }
        }
    }
}
