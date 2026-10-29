using System.Collections.Generic;
using NaughtyAttributes;
using UnityEngine;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;
using NonsensicalKit.Simulation.WarehouseSimulation.Playback;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Runtime
{
    /// <summary>
    /// 按任务 JobId 分配/回收料箱可视对象，支持多条输送线任务并行回放。
    /// </summary>
    public sealed class SimPlaybackCargoVisualRegistry : MonoBehaviour
    {
        [SerializeField, Label("料箱预制体")]
        private GameObject m_cargoPrefab;

        [SerializeField, Label("实例父节点（可选）")]
        private Transform m_poolRoot;

        private readonly Dictionary<int, Transform> _activeByJob = new();
        private readonly Stack<Transform> _pool = new();
        private readonly Dictionary<int, GridIndex> _parkedSlotByJob = new();
        private readonly Dictionary<GridIndex, int> _parkedJobBySlot = new();
        private readonly List<int> _parkReleaseIds = new();

        private void Awake()
        {
            if (m_poolRoot == null)
            {
                m_poolRoot = transform;
            }
        }

        private void OnDisable()
        {
            ReleaseAll();
        }

        /// <summary>获取或创建指定任务的料箱可视对象。</summary>
        public Transform Acquire(int jobId)
        {
            if (jobId < 0)
            {
                return null;
            }

            if (_activeByJob.TryGetValue(jobId, out var existing) && existing != null)
            {
                // 不在此处 EnsureDetached：堆垛机绑叉后输送线/堆垛机各自显式改父节点。
                // 每帧 Acquire+Detach 会把叉上的货扯回世界坐标，表现为取完货找不到/不随叉。
                existing.gameObject.SetActive(true);
                existing.name = $"Cargo (Job {jobId})";
                return existing;
            }

            var cargo = PopOrCreate();
            if (cargo == null)
            {
                return null;
            }

            EnsureDetached(cargo);
            cargo.gameObject.SetActive(true);
            cargo.name = $"Cargo (Job {jobId})";
            _activeByJob[jobId] = cargo;
            return cargo;
        }

        public bool TryGet(int jobId, out Transform cargo)
        {
            cargo = null;
            if (jobId < 0 || !_activeByJob.TryGetValue(jobId, out cargo))
            {
                return false;
            }

            if (cargo == null)
            {
                _activeByJob.Remove(jobId);
                UnparkJob(jobId);
                return false;
            }

            return true;
        }

        /// <summary>入库放货后料箱留在货位上，输送线回收和货架 GPU 都不要再动它。</summary>
        public bool IsJobParked(int jobId) =>
            jobId >= 0 && _parkedSlotByJob.ContainsKey(jobId);

        public bool IsSlotParked(GridIndex slot) => _parkedJobBySlot.ContainsKey(slot);

        /// <summary>取停在该货位上的场景料箱。没有或已销毁返回 false。</summary>
        public bool TryGetParkedCargo(GridIndex slot, out Transform cargo)
        {
            cargo = null;
            if (!_parkedJobBySlot.TryGetValue(slot, out var jobId))
            {
                return false;
            }

            return TryGet(jobId, out cargo);
        }

        /// <summary>记下这只料箱占着的货位。同一货位上一次的料箱会收掉。</summary>
        public void ParkJob(int jobId, GridIndex slot)
        {
            if (jobId < 0)
            {
                return;
            }

            UnparkJob(jobId);
            if (_parkedJobBySlot.TryGetValue(slot, out var other) && other != jobId)
            {
                ReleaseCore(other);
            }

            _parkedSlotByJob[jobId] = slot;
            _parkedJobBySlot[slot] = jobId;
        }

        /// <summary>料箱离开货位（重新挂上货叉）时只取消占用标记，不回收实例。</summary>
        public void UnparkJob(int jobId)
        {
            if (jobId < 0 || !_parkedSlotByJob.TryGetValue(jobId, out var slot))
            {
                return;
            }

            _parkedSlotByJob.Remove(jobId);
            if (_parkedJobBySlot.TryGetValue(slot, out var parked) && parked == jobId)
            {
                _parkedJobBySlot.Remove(slot);
            }
        }

        /// <summary>
        /// 当前快照里已经不再占用的货位，收回停在那里的场景料箱。
        /// 仍在快照里的留下，避免和货架 GPU 各画一次。
        /// </summary>
        public void ReleaseParkedExcept(HashSet<GridIndex> stillOccupied)
        {
            _parkReleaseIds.Clear();
            foreach (var pair in _parkedJobBySlot)
            {
                if (stillOccupied == null || !stillOccupied.Contains(pair.Key))
                {
                    _parkReleaseIds.Add(pair.Value);
                }
            }

            for (var i = 0; i < _parkReleaseIds.Count; i++)
            {
                ReleaseCore(_parkReleaseIds[i]);
            }
        }

        /// <summary>回收指定任务的料箱实例。已停在货位上的不回收，等货位快照清掉再收。</summary>
        public void Release(int jobId)
        {
            if (jobId >= 0 && _parkedSlotByJob.ContainsKey(jobId))
            {
                return;
            }

            ReleaseCore(jobId);
        }

        public void ReleaseAll()
        {
            _parkedSlotByJob.Clear();
            _parkedJobBySlot.Clear();
            foreach (var kv in _activeByJob)
            {
                if (kv.Value == null)
                {
                    continue;
                }

                EnsureDetached(kv.Value);
                kv.Value.gameObject.SetActive(false);
                _pool.Push(kv.Value);
            }

            _activeByJob.Clear();
        }

        /// <summary>复制当前在池中的任务 JobId 列表（用于批量刷新校验信息）。</summary>
        public void CopyActiveJobIds(List<int> buffer)
        {
            buffer?.Clear();
            if (buffer == null)
            {
                return;
            }

            foreach (var kv in _activeByJob)
            {
                buffer.Add(kv.Key);
            }
        }

        private void ReleaseCore(int jobId)
        {
            UnparkJob(jobId);
            if (jobId < 0 || !_activeByJob.TryGetValue(jobId, out var cargo))
            {
                return;
            }

            _activeByJob.Remove(jobId);
            if (cargo == null)
            {
                return;
            }

            EnsureDetached(cargo);
            cargo.gameObject.SetActive(false);
            _pool.Push(cargo);
        }

        private Transform PopOrCreate()
        {
            while (_pool.Count > 0)
            {
                var pooled = _pool.Pop();
                if (pooled != null)
                {
                    return pooled;
                }
            }

            if (m_cargoPrefab == null)
            {
                Debug.LogError("[SimPlayback] 未配置料箱预制体，无法创建料箱可视对象。", this);
                return null;
            }

            var instance = Instantiate(m_cargoPrefab, m_poolRoot);
            instance.name = $"{m_cargoPrefab.name} (Pooled)";
            return instance.transform;
        }

        private static void EnsureDetached(Transform cargo)
        {
            if (cargo != null && cargo.parent != null)
            {
                cargo.SetParent(null, true);
            }
        }
    }
}

