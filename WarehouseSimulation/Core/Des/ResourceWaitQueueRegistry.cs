using System.Collections.Generic;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Core
{
    /// <summary>
    /// 按资源 ID 维护的 FIFO 任务等待队列。
    /// </summary>
    /// <remarks>
    /// 输送 zone 预约失败时，任务 JobId 入队；对应资源占用结束后仅弹出队首并触发重试，
    /// 避免多任务同时争抢导致抖动。任务成功推进或失败时应调用 <see cref="RemoveJob"/> 清理。
    /// </remarks>
    public sealed class ResourceWaitQueueRegistry
    {
        private readonly Dictionary<string, Queue<int>> _queues = new();
        private readonly Dictionary<string, HashSet<int>> _membership = new();
        private readonly Dictionary<int, HashSet<string>> _resourcesByJob = new();

        public void Clear()
        {
            _queues.Clear();
            _membership.Clear();
            _resourcesByJob.Clear();
        }

        public void Enqueue(string resourceId, int jobId)
        {
            if (string.IsNullOrEmpty(resourceId) || jobId < 0)
            {
                return;
            }

            if (!_membership.TryGetValue(resourceId, out var members))
            {
                members = new HashSet<int>();
                _membership[resourceId] = members;
            }

            if (!members.Add(jobId))
            {
                return;
            }

            if (!_queues.TryGetValue(resourceId, out var queue))
            {
                queue = new Queue<int>();
                _queues[resourceId] = queue;
            }

            queue.Enqueue(jobId);

            if (!_resourcesByJob.TryGetValue(jobId, out var resources))
            {
                resources = new HashSet<string>();
                _resourcesByJob[jobId] = resources;
            }

            resources.Add(resourceId);
        }

        public bool TryDequeueNext(string resourceId, out int jobId)
        {
            jobId = -1;
            if (string.IsNullOrEmpty(resourceId)
                || !_queues.TryGetValue(resourceId, out var queue)
                || queue.Count == 0)
            {
                return false;
            }

            jobId = queue.Dequeue();
            if (_membership.TryGetValue(resourceId, out var members))
            {
                members.Remove(jobId);
                if (members.Count == 0)
                {
                    _membership.Remove(resourceId);
                }
            }

            if (queue.Count == 0)
            {
                _queues.Remove(resourceId);
            }

            return jobId >= 0;
        }

        public void RemoveJob(int jobId)
        {
            if (!_resourcesByJob.TryGetValue(jobId, out var resources))
            {
                return;
            }

            foreach (var resourceId in resources)
            {
                if (!_queues.TryGetValue(resourceId, out var queue))
                {
                    continue;
                }

                if (_membership.TryGetValue(resourceId, out var members))
                {
                    members.Remove(jobId);
                    if (members.Count == 0)
                    {
                        _membership.Remove(resourceId);
                    }
                }

                var filtered = new Queue<int>(queue.Count);
                while (queue.Count > 0)
                {
                    var id = queue.Dequeue();
                    if (id != jobId)
                    {
                        filtered.Enqueue(id);
                    }
                }

                if (filtered.Count > 0)
                {
                    _queues[resourceId] = filtered;
                }
                else
                {
                    _queues.Remove(resourceId);
                }
            }

            _resourcesByJob.Remove(jobId);
        }

        public bool IsQueued(string resourceId, int jobId)
        {
            return !string.IsNullOrEmpty(resourceId)
                && _membership.TryGetValue(resourceId, out var members)
                && members.Contains(jobId);
        }
    }
}
