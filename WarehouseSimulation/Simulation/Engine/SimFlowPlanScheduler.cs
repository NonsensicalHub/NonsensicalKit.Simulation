using System;
using System.Collections.Generic;
using System.Linq;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <summary>按流程计划向事件队列排定货物释放事件。</summary>
    internal sealed class SimFlowPlanScheduler
    {
        private readonly SimFlowPlanEntry[] _entries;
        private readonly int[] _remaining;
        private readonly Random _rng;

        public int TotalQuantity { get; }

        public SimFlowPlanScheduler(IReadOnlyList<SimFlowPlanEntry> plan, int randomSeed)
        {
            _entries = plan?.Count > 0 ? plan.ToArray() : Array.Empty<SimFlowPlanEntry>();
            _remaining = new int[_entries.Length];
            _rng = new Random(randomSeed);
            var total = 0;
            for (var i = 0; i < _entries.Length; i++)
            {
                _remaining[i] = Math.Max(0, _entries[i].Quantity);
                total += _remaining[i];
            }

            TotalQuantity = total;
        }

        public SimFlowDirection GetDirection(int entryIndex) =>
            entryIndex >= 0 && entryIndex < _entries.Length
                ? _entries[entryIndex].Direction
                : SimFlowDirection.Inbound;

        public int GetEntryQuantity(int entryIndex) =>
            entryIndex >= 0 && entryIndex < _entries.Length
                ? Math.Max(0, _entries[entryIndex].Quantity)
                : 0;

        public string[] GetRequiredProcessTags(int entryIndex)
        {
            if (entryIndex < 0 || entryIndex >= _entries.Length)
            {
                return null;
            }

            return _entries[entryIndex].RequiredProcessTags;
        }

        public bool HasPendingReleases()
        {
            for (var i = 0; i < _remaining.Length; i++)
            {
                if (_remaining[i] > 0)
                {
                    return true;
                }
            }

            return false;
        }

        public void ScheduleInitial(SimEventQueue queue)
        {
            for (var i = 0; i < _entries.Length; i++)
            {
                if (_remaining[i] <= 0)
                {
                    continue;
                }

                var entry = _entries[i];
                var start = Math.Max(0, entry.StartDelaySeconds);
                if (entry.ScheduleMode == SimFlowScheduleMode.Instant)
                {
                    queue.Enqueue(new ScheduledSimEvent(start, SimEventType.FlowPlanInstantRelease, 0, i));
                    _remaining[i] = 0;
                }
                else
                {
                    queue.Enqueue(new ScheduledSimEvent(start, SimEventType.FlowPlanBatchRelease, 0, i));
                }
            }
        }

        public void OnBatchRelease(SimEventQueue queue, int entryIndex, double now)
        {
            if (entryIndex < 0 || entryIndex >= _remaining.Length || _remaining[entryIndex] <= 0)
            {
                return;
            }

            var entry = _entries[entryIndex];
            var releaseCount = NextRandomQuantity(entry, _remaining[entryIndex]);
            EnqueueUnits(queue, entryIndex, now, releaseCount);
            _remaining[entryIndex] -= releaseCount;

            if (_remaining[entryIndex] <= 0)
            {
                return;
            }

            queue.Enqueue(new ScheduledSimEvent(
                now + NextRandomInterval(entry),
                SimEventType.FlowPlanBatchRelease,
                0,
                entryIndex));
        }

        private void EnqueueUnits(SimEventQueue queue, int entryIndex, double when, int count)
        {
            for (var u = 0; u < count; u++)
            {
                queue.Enqueue(new ScheduledSimEvent(when, SimEventType.FlowCargoRelease, 0, entryIndex));
            }
        }

        private int NextRandomQuantity(SimFlowPlanEntry entry, int remaining)
        {
            var min = Math.Max(1, entry.RandomQuantityMin);
            var max = Math.Max(min, entry.RandomQuantityMax);
            var count = min + _rng.Next(max - min + 1);
            return Math.Min(count, remaining);
        }

        private double NextRandomInterval(SimFlowPlanEntry entry)
        {
            var min = Math.Max(0.01, entry.RandomIntervalMinSeconds);
            var max = Math.Max(min, entry.RandomIntervalMaxSeconds);
            return min + _rng.NextDouble() * (max - min);
        }
    }
}
