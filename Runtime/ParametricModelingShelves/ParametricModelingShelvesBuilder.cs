using NaughtyAttributes;
using NonsensicalKit.Core;
using NonsensicalKit.Tools;
using NonsensicalKit.Tools.EditorTool;
using NonsensicalKit.Tools.MeshTool;
using NonsensicalKit.Tools.ObjectPool;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace NonsensicalKit.Simulation.ParametricModelingShelves
{
    public class ParametricModelingShelvesBuilder : ShelvesBase
    {
        [SerializeField] private ShelvesBuildPrefabConfig[] m_prefabConfigs;
        [SerializeField] private ParametricModelingShelvesManager m_buffer;

        private Dictionary<ShelvesPrefabType, List<ShelvesBuildPrefabConfig>> _configs;

        [Button]
        public void Clean()
        {
            foreach (var item in m_prefabConfigs)
            {
                item.Pool.Clean();
            }
            m_buffer.Clean();
            gameObject.SetDirty();
        }

        [Button]
        public void Rebuild()
        {
            if (m_cellCount.x <= 0 || m_cellCount.y <= 0 || m_cellCount.z <= 0)
            {
                Debug.Log("非法尺寸");
                return;
            }

            foreach (var item in m_prefabConfigs)
            {
                item.Pool.Cache();
            }

            Init();

            m_buffer.Init(this);

            GameObject[] layers = new GameObject[m_cellCount.y];
            List<GameObject>[] layerObjs = new List<GameObject>[m_cellCount.y];
            for (int i = 0; i < layerObjs.Length; i++)
            {
                layerObjs[i] = new List<GameObject>();
            }

            for (int x = 0; x < m_cellCount.x; x++)
            {
                for (int y = 0; y < m_cellCount.y; y++)
                {
                    for (int z = 0; z < m_cellCount.z; z++)
                    {
                        layerObjs[y].AddRange(BuildPillar(_configs[ShelvesPrefabType.Pillar], x, y, z));
                        layerObjs[y].AddRange(BuildHorizontal(_configs[ShelvesPrefabType.Horizontal], x, y, z));
                        layerObjs[y].AddRange(BuildVertical(_configs[ShelvesPrefabType.Vertical], x, y, z));

                        layerObjs[y].AddRange(BuildCenter(_configs[ShelvesPrefabType.Center], x, y, z));
                    }
                }
            }

            for (int x = 0; x <= m_cellCount.x; x++)
            {
                for (int z = 0; z <= m_cellCount.z; z++)
                {
                    layerObjs[0].AddRange(BuildBottomSupport(_configs[ShelvesPrefabType.BottomSupport], x, z));
                }
            }

            for (int y = 0; y < m_cellCount.y; y++)
            {
                for (int x = 0; x < m_cellCount.x; x++)
                {
                    layerObjs[y].AddRange(BuildPillar(_configs[ShelvesPrefabType.Pillar], x, y, m_cellCount.z));
                    layerObjs[y].AddRange(BuildHorizontal(_configs[ShelvesPrefabType.Horizontal], x, y, m_cellCount.z));
                }
            }

            for (int y = 0; y < m_cellCount.y; y++)
            {
                for (int z = 0; z < m_cellCount.z; z++)
                {
                    layerObjs[y].AddRange(BuildPillar(_configs[ShelvesPrefabType.Pillar], m_cellCount.x, y, z));
                    layerObjs[y].AddRange(BuildVertical(_configs[ShelvesPrefabType.Vertical], m_cellCount.x, y, z));
                }
            }

            for (int y = 0; y < m_cellCount.y; y++)
            {
                layerObjs[y].AddRange(BuildPillar(_configs[ShelvesPrefabType.Pillar], m_cellCount.x, y, m_cellCount.z));
            }

            foreach (var item in m_prefabConfigs)
            {
                item.Pool.Flush();
            }
            for (int i = 0; i < layerObjs.Length; i++)
            {
                layers[i] = ModelHelper.MergeMesh(layerObjs[i]);
            }
            m_buffer.SetLayers(layers);

            foreach (var item in m_prefabConfigs)
            {
                item.Pool.Clear();
            }

            gameObject.SetDirty();
        }

        protected override void Init()
        {
            base.Init();
            _configs = new Dictionary<ShelvesPrefabType, List<ShelvesBuildPrefabConfig>>();
            foreach (var item in Enum.GetValues(typeof(ShelvesPrefabType)))
            {
                _configs.Add((ShelvesPrefabType)item, new List<ShelvesBuildPrefabConfig>());
            }
            foreach (var item in m_prefabConfigs)
            {
                _configs[item.PrefabType].Add(item);
                item.InitBuffer(m_cellCount, m_simpleExclude);
            }
        }

        private List<GameObject> BuildBottomSupport(List<ShelvesBuildPrefabConfig> configs, int x, int z)
        {
            List<GameObject> corners = new List<GameObject>();
            foreach (var item in configs)
            {
                if (item.Check(x, 0, z))
                {
                    var newPillar = item.Pool.New();
                    corners.Add(newPillar);
                    newPillar.transform.position = transform.position + _pillarsPos[x, 0, z] + _bottomOffset;
                    newPillar.transform.localScale = Vector3.Scale(item.DefaultScale, new Vector3(1, m_bottomHeight / item.OriginSize.y, 1));
                }
            }
            return corners;
        }

        private List<GameObject> BuildPillar(List<ShelvesBuildPrefabConfig> configs, int x, int y, int z)
        {
            List<GameObject> corners = new List<GameObject>();
            foreach (var item in configs)
            {
                if (item.Check(x, y, z))
                {
                    var newPillar = item.Pool.New();
                    corners.Add(newPillar);
                    newPillar.transform.position = transform.position + _pillarsPos[x, y, z];
                    newPillar.transform.localScale = Vector3.Scale(item.DefaultScale, new Vector3(1, _cellYSize[y] / item.OriginSize.y, 1));
                }
            }
            return corners;
        }

        private List<GameObject> BuildHorizontal(List<ShelvesBuildPrefabConfig> configs, int x, int y, int z)
        {
            List<GameObject> sides = new List<GameObject>();
            foreach (var item in configs)
            {
                if (item.Check(x, y, z))
                {
                    var newCrossbar = item.Pool.New();
                    sides.Add(newCrossbar);
                    newCrossbar.transform.position = transform.position + _horizontalCorssbarPos[x, y, z];
                    newCrossbar.transform.localScale = Vector3.Scale(item.DefaultScale, new Vector3(_cellXSize[x] / item.OriginSize.x, 1, 1));
                }
            }
            return sides;
        }

        private List<GameObject> BuildVertical(List<ShelvesBuildPrefabConfig> configs, int x, int y, int z)
        {
            List<GameObject> sides = new List<GameObject>();
            foreach (var item in configs)
            {
                if (item.Check(x, y, z))
                {
                    var newCrossbar = item.Pool.New();
                    sides.Add(newCrossbar);
                    newCrossbar.transform.position = transform.position + _verticalCorssbarPos[x, y, z];
                    newCrossbar.transform.localScale = Vector3.Scale(item.DefaultScale, new Vector3(1, 1, _cellZSize[z] / item.OriginSize.z));
                }
            }
            return sides;
        }

        private List<GameObject> BuildCenter(List<ShelvesBuildPrefabConfig> configs, int x, int y, int z)
        {
            List<GameObject> centers = new List<GameObject>();
            foreach (var item in configs)
            {
                if (item.Check(x, y, z))
                {
                    var newCenter = item.Pool.New();
                    centers.Add(newCenter);
                    newCenter.transform.position = transform.position + _cellsPos[x, y, z];
                    newCenter.transform.localScale = Vector3.Scale(item.DefaultScale, new Vector3(_cellXSize[x] / item.OriginSize.x, 1, _cellZSize[z] / item.OriginSize.z));
                }
            }
            return centers;
        }
    }

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

        public Vector3Int Min;
        public Vector3Int Max;
        public Vector3Int[] Include;
        public Vector3Int[] Exclude;

        public Array3<bool> Buffer;

        public virtual void InitBuffer(Vector3Int cellCount, Vector3Int[] simpleExclude)
        {
            Buffer = new Array3<bool>(cellCount.x, cellCount.y, cellCount.z);

            var max = Vector3Int.Max(Vector3Int.zero, new Vector3Int(cellCount.x - 1, cellCount.y - 1, cellCount.z - 1));
            Max.Clamp(Vector3Int.zero, max);
            Min.Clamp(Vector3Int.zero, Max);
            for (int x = Min.x; x <= Max.x; x++)
            {
                for (int y = Min.y; y <= Max.y; y++)
                {
                    for (int z = Min.z; z <= Max.z; z++)
                    {
                        Buffer[x, y, z] = true;
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

        protected override void AnalyzeSimpleExclude(Vector3Int cellCount, Vector3Int[] simpleExclude)
        {
            Exclude = Exclude.Concat(simpleExclude).ToArray();
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
