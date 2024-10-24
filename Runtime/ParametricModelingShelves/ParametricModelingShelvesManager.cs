using NonsensicalKit.Core;
using NonsensicalKit.Tools;
using System.Collections.Generic;
using UnityEngine;

namespace NonsensicalKit.Simulation.ParametricModelingShelves
{
    public class ParametricModelingShelvesManager : ShelvesBase
    {
        public GameObject[] m_layersParent;
        public LoadsConfig[] m_loadsConfigs;
        public ShelvesLoadPrefabConfig[] m_loadPrefabConfig;
        public bool m_autoInit;

        private Array3<Vector3> _loadsPos;

        private void Awake()
        {
            if (m_autoInit)
            {
                Init();
            }
        }

        private void Update()
        {
            if (m_loadsConfigs != null)
            {
                foreach (var config in m_loadsConfigs)
                {
                    config.RenderLoads();
                }
            }
        }

        public void Init(ShelvesBase builder)
        {
            CopyAndInit(builder);
        }

        protected override void Init()
        {
            base.Init();
            m_loadsConfigs = new LoadsConfig[m_loadPrefabConfig.Length];
            for (int i = 0; i < m_loadPrefabConfig.Length; i++)
            {
                m_loadsConfigs[i] = new LoadsConfig();
                m_loadsConfigs[i].Init(m_cellCount, m_loadPrefabConfig[i].Prefab);
            }
            foreach (var item in m_loadPrefabConfig)
            {
                item.InitBuffer(m_cellCount, m_simpleExclude);
            }

            for (int x = 0; x < m_cellCount.x; x++)
            {
                for (int y = 0; y < m_cellCount.y; y++)
                {
                    for (int z = 0; z < m_cellCount.z; z++)
                    {
                        Matrix4x4 m4x4 = new Matrix4x4();
                        m4x4.SetTRS(transform.position + _cellsPos[x, y, z], transform.rotation, Vector3.one);
                        for (int i = 0; i < m_loadPrefabConfig.Length; i++)
                        {
                            if (m_loadPrefabConfig[i].Check(x, y, z))
                            {
                                m_loadsConfigs[i].AddNewLoad(x, y, z, m4x4, false);
                            }
                        }
                    }
                }
            }
            foreach (var item in m_loadsConfigs)
            {
                item.UpdateParts();
            }
        }

        public void Clean()
        {
            m_loadsConfigs = null;

            if (m_layersParent != null)
            {
                foreach (var item in m_layersParent)
                {
                    item.gameObject.Destroy();
                }
                m_layersParent = null;
            }
        }

        public void SetLayers(GameObject[] layers)
        {
            if (m_layersParent != null)
            {
                foreach (var item in m_layersParent)
                {
                    item.gameObject.Destroy();
                }
            }
            foreach (var item in layers)
            {
                item.transform.SetParent(transform);
            }
            m_layersParent = layers;
        }
    }

    [System.Serializable]
    public class LoadsConfig
    {
        public GameObject Prefab;

        public List<LoadsPartInfo> Parts;
        public Array3<int> Index;
        public List<Matrix4x4> LoadTrans;

        public void Init(Vector3Int cellCount, GameObject prefab)
        {
            Index = new Array3<int>(cellCount.x, cellCount.y, cellCount.z);
            LoadTrans = new List<Matrix4x4>();
            InitMeshs(prefab);
        }

        public void RenderLoads()
        {
            for (int i = 0; i < Parts.Count; i++)
            {
                foreach (var item in Parts[i].Trans)
                {
                    Graphics.RenderMeshInstanced(Parts[i].RenderParams, Parts[i].Mesh, Parts[i].SubMeshCount, item);
                }
            }
        }

        public void AddNewLoad(int column, int layer, int row, Matrix4x4 trans, bool autoUpdate = true)
        {
            Index[column, layer, row] = LoadTrans.Count;
            LoadTrans.Add(trans);
            if (autoUpdate)
            {
                UpdateParts();
            }
        }

        public void UpdateParts()
        {
            foreach (var item in Parts)
            {
                item.UpdateTrans(LoadTrans);
            }
        }

        private void InitMeshs(GameObject prefab)
        {
            Parts = new List<LoadsPartInfo>();

            var meshs = prefab.GetComponentsInChildren<MeshFilter>();

            foreach (var item in meshs)
            {
                if (item.gameObject.TryGetComponent<MeshRenderer>(out var renderer))
                {
                    if (item.sharedMesh.subMeshCount < renderer.sharedMaterials.Length)
                    {
                        return;
                    }
                    for (int i = 0; i < renderer.sharedMaterials.Length; i++)
                    {
                        Parts.Add(new LoadsPartInfo(new RenderParams(renderer.sharedMaterials[i]), item.sharedMesh, i, prefab.transform.worldToLocalMatrix * item.transform.localToWorldMatrix));
                    }
                }
            }
        }
    }

    public class LoadsPartInfo
    {
        public RenderParams RenderParams;
        public Mesh Mesh;
        public int SubMeshCount;
        public Matrix4x4 Offset;

        public Matrix4x4[][] Trans;

        public LoadsPartInfo(RenderParams renderParams, Mesh mesh, int subMeshCount, Matrix4x4 offset)
        {
            RenderParams = renderParams;
            Mesh = mesh;
            SubMeshCount = subMeshCount;
            Offset = offset;
        }

        public void UpdateTrans(List<Matrix4x4> trans)
        {
            int less = trans.Count;
            int patch = (less - 1) / 1023 + 1;
            Trans = new Matrix4x4[patch][];
            int index = 0;
            int patchIndex = 0;
            foreach (var item in trans)
            {
                if (index == 0)
                {
                    if (less >= 1023)
                    {
                        Trans[patchIndex] = new Matrix4x4[1023];
                        less -= 1023;
                    }
                    else
                    {
                        Trans[patchIndex] = new Matrix4x4[less];
                        less = 0;
                    }
                }
                Trans[patchIndex][index] = (item * Offset);
                index++;
                if (index == 1023)
                {
                    index = 0;
                    patchIndex++;
                }
            }
        }
    }
}
