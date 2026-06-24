using System;
using System.Collections.Generic;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Core
{
    /// <summary>
    /// 已调度、待入队的离散事件快照。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><see cref="Time"/> — 事件触发仿真时刻（秒）</item>
    /// <item><see cref="Type"/> — 决定 <c>Dispatch</c> 分支</item>
    /// <item><see cref="JobId"/> — 关联的 <see cref="WarehouseJob"/>；全局事件（到货批次）可为 0</item>
    /// <item><see cref="Payload"/> — 类型相关整数载荷（入库口索引、zone 下标、释放序号等）</item>
    /// </list>
    /// </remarks>
    public readonly struct ScheduledSimEvent
    {
        public readonly double Time;
        public readonly SimEventType Type;
        public readonly int JobId;
        public readonly int Payload;

        public ScheduledSimEvent(double time, SimEventType type, int jobId, int payload = 0)
        {
            Time = time;
            Type = type;
            JobId = jobId;
            Payload = payload;
        }
    }

    /// <summary>
    /// 按仿真时间排序的事件优先队列（最小堆）。
    /// <para>同刻事件按 Type → JobId → Payload 打破平局，保证确定性回放。</para>
    /// </summary>
    public sealed class SimEventQueue
    {
        private readonly List<ScheduledSimEvent> _heap = new();

        public int Count => _heap.Count;

        public void Clear() => _heap.Clear();

        /// <summary>将事件插入最小堆（按时间、类型、JobId 排序）。</summary>
        public void Enqueue(ScheduledSimEvent evt)
        {
            _heap.Add(evt);
            SiftUp(_heap.Count - 1);
        }

        /// <summary>弹出最早事件；队列为空时返回 false。</summary>
        public bool TryDequeue(out ScheduledSimEvent evt)
        {
            if (_heap.Count == 0)
            {
                evt = default;
                return false;
            }

            evt = _heap[0];
            var last = _heap[^1];
            _heap.RemoveAt(_heap.Count - 1);
            if (_heap.Count > 0)
            {
                _heap[0] = last;
                SiftDown(0);
            }

            return true;
        }

        public bool TryPeek(out ScheduledSimEvent evt)
        {
            if (_heap.Count == 0)
            {
                evt = default;
                return false;
            }

            evt = _heap[0];
            return true;
        }

        private static int Compare(ScheduledSimEvent a, ScheduledSimEvent b)
        {
            var c = a.Time.CompareTo(b.Time);
            if (c != 0)
            {
                return c;
            }

            c = a.Type.CompareTo(b.Type);
            if (c != 0)
            {
                return c;
            }

            c = a.JobId.CompareTo(b.JobId);
            if (c != 0)
            {
                return c;
            }

            return a.Payload.CompareTo(b.Payload);
        }

        private void SiftUp(int i)
        {
            while (i > 0)
            {
                var parent = (i - 1) >> 1;
                if (Compare(_heap[i], _heap[parent]) >= 0)
                {
                    break;
                }

                (_heap[i], _heap[parent]) = (_heap[parent], _heap[i]);
                i = parent;
            }
        }

        private void SiftDown(int i)
        {
            var count = _heap.Count;
            while (true)
            {
                var left = (i << 1) + 1;
                if (left >= count)
                {
                    break;
                }

                var right = left + 1;
                var smallest = left;
                if (right < count && Compare(_heap[right], _heap[left]) < 0)
                {
                    smallest = right;
                }

                if (Compare(_heap[i], _heap[smallest]) <= 0)
                {
                    break;
                }

                (_heap[i], _heap[smallest]) = (_heap[smallest], _heap[i]);
                i = smallest;
            }
        }
    }
}
