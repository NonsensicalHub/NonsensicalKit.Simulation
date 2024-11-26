using NaughtyAttributes;
using NonsensicalKit.Tools;
using NonsensicalKit.Tools.MeshTool;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace NonsensicalKit.Simulation.ParametricModelingShelves
{
    public class ShelvesBuilder : ShelvesBase
    {
        [SerializeField] private ShelvesBuildPrefabConfig[] m_prefabConfigs;
        [SerializeField] private  ShelvesManager m_manager;

        private Dictionary<ShelvesPrefabType, List<ShelvesBuildPrefabConfig>> _configs;
        private List<GameObject>[] _layerObjs;
        [Button]
        public void Clean()
        {
            _layerObjs = null;
            foreach (var item in m_prefabConfigs)
            {
                item.Pool.Clean();
            }
            m_manager.Clean();
            gameObject.SetDirty();
        }

        [Button]
        public void Rebuild()
        {
            Build();
            Merge();
        }

        [Button]
        public void Build()
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

            m_manager.Init(this);

           _layerObjs = new List<GameObject>[m_cellCount.y];
            for (int i = 0; i < _layerObjs.Length; i++)
            {
                _layerObjs[i] = new List<GameObject>();
            }

            for (int x = 0; x < m_cellCount.x; x++)
            {
                for (int y = 0; y < m_cellCount.y; y++)
                {
                    for (int z = 0; z < m_cellCount.z; z++)
                    {
                        _layerObjs[y].AddRange(BuildPillar(_configs[ShelvesPrefabType.Pillar], x, y, z));
                        _layerObjs[y].AddRange(BuildHorizontal(_configs[ShelvesPrefabType.Horizontal], x, y, z));
                        _layerObjs[y].AddRange(BuildVertical(_configs[ShelvesPrefabType.Vertical], x, y, z));

                        _layerObjs[y].AddRange(BuildCenter(_configs[ShelvesPrefabType.Center], x, y, z));
                    }
                }
            }

            for (int x = 0; x <= m_cellCount.x; x++)
            {
                for (int z = 0; z <= m_cellCount.z; z++)
                {
                    _layerObjs[0].AddRange(BuildBottomSupport(_configs[ShelvesPrefabType.BottomSupport], x, z));
                }
            }

            for (int y = 0; y < m_cellCount.y; y++)
            {
                for (int x = 0; x < m_cellCount.x; x++)
                {
                    _layerObjs[y].AddRange(BuildPillar(_configs[ShelvesPrefabType.Pillar], x, y, m_cellCount.z));
                    _layerObjs[y].AddRange(BuildHorizontal(_configs[ShelvesPrefabType.Horizontal], x, y, m_cellCount.z));
                }
            }

            for (int y = 0; y < m_cellCount.y; y++)
            {
                for (int z = 0; z < m_cellCount.z; z++)
                {
                    _layerObjs[y].AddRange(BuildPillar(_configs[ShelvesPrefabType.Pillar], m_cellCount.x, y, z));
                    _layerObjs[y].AddRange(BuildVertical(_configs[ShelvesPrefabType.Vertical], m_cellCount.x, y, z));
                }
            }

            for (int y = 0; y < m_cellCount.y; y++)
            {
                _layerObjs[y].AddRange(BuildPillar(_configs[ShelvesPrefabType.Pillar], m_cellCount.x, y, m_cellCount.z));
            }

            foreach (var item in m_prefabConfigs)
            {
                item.Pool.Flush();
            }
        }

        [Button]
        private void Merge()
        {
            GameObject[] layers = new GameObject[m_cellCount.y];
            for (int i = 0; i < _layerObjs.Length; i++)
            {
                layers[i] = ModelHelper.MergeMesh(_layerObjs[i]);
            }
            m_manager.SetLayers(layers);
            _layerObjs = null;

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
}
