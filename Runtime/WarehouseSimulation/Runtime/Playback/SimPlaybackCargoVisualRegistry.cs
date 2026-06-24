using System.Collections.Generic;
using NaughtyAttributes;
using UnityEngine;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;

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
                EnsureDetached(existing);
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
                return false;
            }

            return true;
        }

        /// <summary>回收指定任务的料箱实例。</summary>
        public void Release(int jobId)
        {
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

        public void ReleaseAll()
        {
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
