using NonsensicalKit.Core;
using NonsensicalKit.Tools.EditorTool;
using System.Collections.Generic;
using UnityEngine;

namespace NonsensicalKit.Simulation.ParametricModelingShelves
{
    public class ParametricModelingShelvesManager : ShelvesBase
    {
        [SerializeField] private GameObject[] m_layersParent;
        [SerializeField] private LoadsConfig[] m_loadsConfigs;
        [SerializeField] private ShelvesLoadPrefabConfig[] m_loadPrefabConfig;
        [SerializeField] private bool m_autoInit;

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
                Graphics.RenderMeshInstanced(Parts[i].RenderParams, Parts[i].Mesh, Parts[i].SubMeshCount, Parts[i].Trans);
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

        public List<Matrix4x4> Trans;

        public LoadsPartInfo(RenderParams renderParams, Mesh mesh, int subMeshCount, Matrix4x4 offset)
        {
            RenderParams = renderParams;
            Mesh = mesh;
            SubMeshCount = subMeshCount;
            Offset = offset;
        }

        public void UpdateTrans(List<Matrix4x4> trans)
        {
            Trans = new List<Matrix4x4>();

            foreach (var item in trans)
            {
                Trans.Add(item * Offset);
            }
        }
    }
}
