using System;
using System.Collections.Generic;
using NaughtyAttributes;
using NonsensicalKit.Core;
using NonsensicalKit.Tools;
using UnityEngine;

namespace NonsensicalKit.Simulation.ParametricModelingShelves
{
    public class ShelvesManager : ShelvesBase
    {
        [SerializeField] private GameObject[] m_layersParent;
        [SerializeField] private LoadsConfig[] m_loadsConfigs;
        [SerializeField] private ShelvesLoadPrefabConfig[] m_loadPrefabConfig;
        [SerializeField] private bool m_autoInit;
        [SerializeField] private float[] m_layerIntervals;

        public float[] LayerIntervals
        {
            get => m_layerIntervals;
            set
            {
                m_layerIntervals = value;
                UpdateLayerIntervals();
            }
        }

        private Array4<GameObject> _loadTargets;
        private Array3<bool> _loadsVisible;

        private float[] _layerAddingHeight;
        private Array3<Vector3> _loadsPos;

        private Matrix4x4 _ltwMatrix;

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
                bool flag = false;
                if (transform.localToWorldMatrix != _ltwMatrix)
                {
                    _ltwMatrix = transform.localToWorldMatrix;
                    flag = true;
                }

                foreach (var config in m_loadsConfigs)
                {
                    if (flag)
                    {
                        config.SetLTW(_ltwMatrix);
                    }

                    config.RenderLoads();
                }
            }
        }

        public void Init(ShelvesBase builder)
        {
            CopyAndInit(builder);
        }

        public void SetLoadVisible(int x, int y, int z, bool show)
        {
            _loadsVisible.SafeSet(x, y, z, show);
            UpdateLoads();
        }

        public void SetLoadsVisible(Array3<bool> show)
        {
            _loadsVisible = show;
            UpdateLoads();
        }

        private void UpdateLayerIntervals(bool update = true)
        {
            if (m_layerIntervals is null || m_layerIntervals.Length != m_cellCount.y)
            {
                m_layerIntervals = new float[m_cellCount.y];
            }

            _layerAddingHeight = new float[m_layerIntervals.Length];
            float sum = 0;
            for (int i = 0; i < m_layerIntervals.Length; i++)
            {
                sum += m_layerIntervals[i];
                _layerAddingHeight[i] = sum;
            }

            if (update)
            {
                UpdateLoads();
            }
        }

        [Button("UpdateLoads")]
        private void UpdateLoads()
        {
            if (m_loadsConfigs == null)
            {
                return;
            }

            for (int x = 0; x < m_cellCount.x; x++)
            {
                for (int y = 0; y < m_cellCount.y; y++)
                {
                    var addHeight = new Vector3(0, _layerAddingHeight[y], 0);
                    for (int z = 0; z < m_cellCount.z; z++)
                    {
                        Matrix4x4 m4x4 = new Matrix4x4();
                        m4x4.SetTRS(CellsPos[x, y, z] + addHeight, transform.rotation, Vector3.one);
                        Matrix4x4 m4x4_2 = new Matrix4x4();
                        m4x4_2.SetTRS(CellsPos[x, y, z] + addHeight, transform.rotation, Vector3.zero);
                        for (int i = 0; i < m_loadPrefabConfig.Length; i++)
                        {
                            if (m_loadPrefabConfig[i].Check(x, y, z))
                            {
                                m_loadsConfigs[i].SetNewTrans(x, y, z, _loadsVisible.SafeGet(x, y, z) ? m4x4 : m4x4_2, false);
                                var loadTarget = _loadTargets[i, x, y, z];
                                loadTarget.transform.SetPositionAndRotation(transform.position + CellsPos[x, y, z] + addHeight, transform.rotation);
                                loadTarget.transform.localScale = _loadsVisible.SafeGet(x, y, z) ? Vector3.one : Vector3.zero;
                            }
                        }
                    }
                }
            }

            foreach (var item in m_loadsConfigs)
            {
                item.UpdateParts();
            }

            for (int i = 0; i < m_layersParent.Length; i++)
            {
                m_layersParent[i].transform.localPosition = new Vector3(0, _layerAddingHeight[i], 0);
            }
        }

        protected override void Init()
        {
            base.Init();
            m_loadsConfigs = new LoadsConfig[m_loadPrefabConfig.Length];
            if (_layerAddingHeight == null || _layerAddingHeight.Length < m_cellCount.y)
            {
                _layerAddingHeight = new float[m_cellCount.y];
            }

            UpdateLayerIntervals(false);

            _loadsVisible = new Array3<bool>(m_cellCount.x, m_cellCount.y, m_cellCount.z);
            _loadsVisible.Fill(true);

            for (int i = 0; i < m_loadPrefabConfig.Length; i++)
            {
                m_loadsConfigs[i] = new LoadsConfig();
                m_loadsConfigs[i].Init(m_cellCount, m_loadPrefabConfig[i].m_Prefab);
                m_loadsConfigs[i].SetLTW(transform.localToWorldMatrix);
            }

            foreach (var item in m_loadPrefabConfig)
            {
                item.InitBuffer(m_cellCount, m_simpleExclude);
            }

            if (_loadTargets.m_Array != null)
            {
                foreach (var item in _loadTargets.m_Array)
                {
                    if (item != null)
                    {
                        item.Destroy();
                    }
                }
            }

            _loadTargets = new Array4<GameObject>(m_loadPrefabConfig.Length, m_cellCount.x, m_cellCount.y, m_cellCount.z);

            for (int x = 0; x < m_cellCount.x; x++)
            {
                for (int y = 0; y < m_cellCount.y; y++)
                {
                    var addHeight = new Vector3(0, _layerAddingHeight[y], 0);
                    for (int z = 0; z < m_cellCount.z; z++)
                    {
                        Matrix4x4 m4x4 = new Matrix4x4();
                        m4x4.SetTRS(CellsPos[x, y, z] + addHeight, transform.rotation, Vector3.one);
                        Matrix4x4 m4x4_2 = new Matrix4x4();
                        m4x4_2.SetTRS(CellsPos[x, y, z] + addHeight, transform.rotation, Vector3.zero);
                        for (int i = 0; i < m_loadPrefabConfig.Length; i++)
                        {
                            if (m_loadPrefabConfig[i].Check(x, y, z))
                            {
                                m_loadsConfigs[i].AddNewLoad(x, y, z, _loadsVisible[x, y, z] ? m4x4 : m4x4_2, false);
                                var newTarget = new GameObject();
                                newTarget.transform.SetPositionAndRotation(transform.position + CellsPos[x, y, z] + addHeight, transform.rotation);
                                newTarget.transform.localScale = _loadsVisible[x, y, z] ? Vector3.one : Vector3.zero;
                                var newBox = newTarget.AddComponent<BoxCollider>();
                                newBox.center = m_loadPrefabConfig[i].m_Bounds.center;
                                newBox.size = m_loadPrefabConfig[i].m_Bounds.size;
                                var newLoadTarget = newTarget.AddComponent<LoadTarget>();
                                newLoadTarget.m_Pos = new Vector3Int(x, y, z);
                                newTarget.transform.SetParent(transform, true);
                                _loadTargets[i, x, y, z] = (newTarget);
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
            if (_loadTargets.m_Array != null)
            {
                foreach (var item in _loadTargets.m_Array)
                {
                    item.Destroy();
                }
            }

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
            if (layers.Length != _layerAddingHeight.Length)
            {
                return;
            }

            if (m_layersParent != null)
            {
                foreach (var item in m_layersParent)
                {
                    item.Destroy();
                }
            }

            GameObject[] trueLayers = new GameObject[layers.Length];

            for (int i = 0; i < layers.Length; i++)
            {
                trueLayers[i] = new GameObject();
                trueLayers[i].transform.SetParent(transform);
                trueLayers[i].transform.localPosition = new Vector3(0, 0, 0);
                layers[i].transform.SetParent(trueLayers[i].transform, true);
                trueLayers[i].transform.localPosition = new Vector3(0, _layerAddingHeight[i], 0);
            }

            m_layersParent = trueLayers;
        }
    }

    [Serializable]
    public class LoadsConfig
    {
        public GameObject Prefab;
        public List<LoadsPartInfo> Parts;
        public Array3<int> Index;
        public List<Matrix4x4> LoadTrans;
        public Matrix4x4 LTW;

        public void Init(Vector3Int cellCount, GameObject prefab)
        {
            Index = new Array3<int>(cellCount.x, cellCount.y, cellCount.z);
            LoadTrans = new List<Matrix4x4>();
            Prefab = prefab;
            InitMeshs(prefab);
        }

        public void RenderLoads()
        {
            foreach (var t in Parts)
            {
                foreach (var item in t.Trans)
                {
                    Graphics.RenderMeshInstanced(t.RenderParams, t.Mesh, t.SubMeshCount, item);
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

        [Obsolete("Use SetNewTrans")]
        public void SetNewLoadNewTrans(int column, int layer, int row, Matrix4x4 trans, bool autoUpdate = true)
        {
            SetNewTrans(column, layer, row, trans, autoUpdate);
        }

        public void SetNewTrans(int column, int layer, int row, Matrix4x4 trans, bool autoUpdate = true)
        {
            LoadTrans[Index[column, layer, row]] = trans;
            if (autoUpdate)
            {
                UpdateParts();
            }
        }

        public void SetLTW(Matrix4x4 ltw)
        {
            LTW = ltw;
            UpdateParts();
        }

        public void UpdateParts()
        {
            List<Matrix4x4> trans = new List<Matrix4x4>();
            foreach (var t in LoadTrans)
            {
                trans.Add(LTW * t);
            }

            foreach (var item in Parts)
            {
                item.UpdateTrans(trans);
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
                        Parts.Add(new LoadsPartInfo(new RenderParams(renderer.sharedMaterials[i]), item.sharedMesh, i,
                            prefab.transform.worldToLocalMatrix * item.transform.localToWorldMatrix));
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
