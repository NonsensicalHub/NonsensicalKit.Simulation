using System.Collections.Generic;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <summary>按流程计划向入库/出库等候队列释放货物。</summary>
    internal sealed class FlowPlanProcess : ISimProcess
    {
        private readonly SimRunContext _ctx;
        private SimFlowPlanScheduler _scheduler;
        private readonly Queue<int> _inboundEntries = new();
        private readonly Queue<int> _outboundEntries = new();

        public FlowPlanProcess(SimRunContext ctx) => _ctx = ctx;

        internal int InboundWaitingCount { get; set; }

        internal int OutboundWaitingCount { get; set; }

        internal int InboundEntryCount => _inboundEntries.Count;

        internal int OutboundEntryCount => _outboundEntries.Count;

        public bool Handles(SimEventType type) =>
            type is SimEventType.FlowCargoRelease
                or SimEventType.FlowPlanBatchRelease
                or SimEventType.FlowPlanInstantRelease;

        public void OnEvent(ScheduledSimEvent evt)
        {
            switch (evt.Type)
            {
                case SimEventType.FlowCargoRelease:
                    OnCargoRelease(evt.Payload);
                    break;
                case SimEventType.FlowPlanBatchRelease:
                    _scheduler?.OnBatchRelease(_ctx.Queue, evt.Payload, _ctx.Clock.Now);
                    break;
                case SimEventType.FlowPlanInstantRelease:
                    OnInstantRelease(evt.Payload);
                    break;
            }
        }

        internal void Reset()
        {
            _scheduler = null;
            InboundWaitingCount = 0;
            OutboundWaitingCount = 0;
            _inboundEntries.Clear();
            _outboundEntries.Clear();
        }

        internal void Init()
        {
            var plan = SimFlowPlanResolver.Resolve(_ctx.Scenario);
            _scheduler = new SimFlowPlanScheduler(plan, _ctx.Scenario.FlowRandomSeed);
            _ctx.TargetCount = _scheduler.TotalQuantity;
            _ctx.InboundTargetCount = SimFlowPlanResolver.CountInboundQuantity(plan);
            _ctx.InboundCompletedCount = 0;
            InboundWaitingCount = 0;
            OutboundWaitingCount = 0;
            _inboundEntries.Clear();
            _outboundEntries.Clear();
        }

        internal void Schedule() => _scheduler?.ScheduleInitial(_ctx.Queue);

        internal bool HasPendingReleases() => _scheduler != null && _scheduler.HasPendingReleases();

        internal void DequeueInbound()
        {
            if (_inboundEntries.Count > 0)
            {
                _inboundEntries.Dequeue();
            }
        }

        internal void ClearInbound() => _inboundEntries.Clear();

        internal void DequeueOutbound()
        {
            if (_outboundEntries.Count > 0)
            {
                _outboundEntries.Dequeue();
            }
        }

        internal void ClearOutbound() => _outboundEntries.Clear();

        internal string[] PeekInboundTags()
        {
            if (_inboundEntries.Count == 0 || _scheduler == null)
            {
                return null;
            }

            return _scheduler.GetRequiredProcessTags(_inboundEntries.Peek());
        }

        internal string[] PeekOutboundTags()
        {
            if (_outboundEntries.Count == 0 || _scheduler == null)
            {
                return null;
            }

            return _scheduler.GetRequiredProcessTags(_outboundEntries.Peek());
        }

        internal int[] PeekInboundWarehouseIds()
        {
            if (_inboundEntries.Count == 0 || _scheduler == null)
            {
                return null;
            }

            return _scheduler.GetAllowedWarehouseIds(_inboundEntries.Peek());
        }

        internal int[] PeekOutboundWarehouseIds()
        {
            if (_outboundEntries.Count == 0 || _scheduler == null)
            {
                return null;
            }

            return _scheduler.GetAllowedWarehouseIds(_outboundEntries.Peek());
        }

        private void OnCargoRelease(int entryIndex)
        {
            if (_scheduler == null)
            {
                return;
            }

            var direction = _scheduler.GetDirection(entryIndex);
            if (direction == SimFlowDirection.Outbound)
            {
                OutboundWaitingCount++;
                _outboundEntries.Enqueue(entryIndex);
                WarehouseSimLog.Info(
                    $"出库请求 +1 t={_ctx.Clock.Now:F2}s；出库等候 {OutboundWaitingCount} 箱");
                _ctx.ScheduleInitialOutfeedDispatches();
            }
            else
            {
                InboundWaitingCount++;
                _inboundEntries.Enqueue(entryIndex);
                WarehouseSimLog.Info(
                    $"入库请求 +1 t={_ctx.Clock.Now:F2}s；入库等候 {InboundWaitingCount} 箱");
                _ctx.ScheduleInitialInfeedFeeds();
            }
        }

        private void OnInstantRelease(int entryIndex)
        {
            if (_scheduler == null)
            {
                return;
            }

            var quantity = _scheduler.GetEntryQuantity(entryIndex);
            if (quantity <= 0)
            {
                return;
            }

            var direction = _scheduler.GetDirection(entryIndex);
            if (direction == SimFlowDirection.Outbound)
            {
                for (var i = 0; i < quantity; i++)
                {
                    _outboundEntries.Enqueue(entryIndex);
                }

                OutboundWaitingCount += quantity;
                WarehouseSimLog.Info(
                    $"出库请求 +{quantity} t={_ctx.Clock.Now:F2}s；出库等候 {OutboundWaitingCount} 箱");
                _ctx.ScheduleInitialOutfeedDispatches();
                return;
            }

            for (var i = 0; i < quantity; i++)
            {
                _inboundEntries.Enqueue(entryIndex);
            }

            InboundWaitingCount += quantity;
            WarehouseSimLog.Info(
                $"入库请求 +{quantity} t={_ctx.Clock.Now:F2}s；入库等候 {InboundWaitingCount} 箱");
            _ctx.ScheduleInitialInfeedFeeds();
        }
    }
}
