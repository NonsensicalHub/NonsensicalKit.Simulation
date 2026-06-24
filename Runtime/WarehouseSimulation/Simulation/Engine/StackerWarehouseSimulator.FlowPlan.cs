using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <content>
    /// 流程计划：按配置向入库/出库等候队列释放货物。
    /// </content>
    public sealed partial class StackerWarehouseSimulator
    {
        private SimFlowPlanScheduler _flowPlanScheduler;
        private int _inboundWaitingCount;
        private int _outboundWaitingCount;

        private void InitFlowPlan()
        {
            var plan = SimFlowPlanResolver.Resolve(_scenario);
            _flowPlanScheduler = new SimFlowPlanScheduler(plan, _scenario.FlowRandomSeed);
            _targetCount = _flowPlanScheduler.TotalQuantity;
            _inboundWaitingCount = 0;
            _outboundWaitingCount = 0;
        }

        /// <summary>全部入库货物已在等候区；为每个入库口排定首次放货。</summary>
        private void ScheduleInitialInfeedFeeds()
        {
            var infeedCount = _conveyorTopology.InfeedNodeIndices.Count;
            for (var port = 0; port < infeedCount; port++)
            {
                ScheduleInfeedPortFeed(port, 0);
            }
        }

        private void ScheduleFlowPlan()
        {
            _flowPlanScheduler?.ScheduleInitial(_queue);
        }

        private void OnFlowCargoRelease(int entryIndex)
        {
            if (_flowPlanScheduler == null)
            {
                return;
            }

            var direction = _flowPlanScheduler.GetDirection(entryIndex);
            if (direction == SimFlowDirection.Outbound)
            {
                _outboundWaitingCount++;
                WarehouseSimLog.Info(
                    $"出库请求 +1 t={_clock.Now:F2}s；出库等候 {_outboundWaitingCount} 箱");
                ScheduleInitialOutfeedDispatches();
            }
            else
            {
                _inboundWaitingCount++;
                WarehouseSimLog.Info(
                    $"入库请求 +1 t={_clock.Now:F2}s；入库等候 {_inboundWaitingCount} 箱");
                ScheduleInitialInfeedFeeds();
            }

        }

        private void OnFlowPlanInstantRelease(int entryIndex)
        {
            if (_flowPlanScheduler == null)
            {
                return;
            }

            var quantity = _flowPlanScheduler.GetEntryQuantity(entryIndex);
            if (quantity <= 0)
            {
                return;
            }

            var direction = _flowPlanScheduler.GetDirection(entryIndex);
            if (direction == SimFlowDirection.Outbound)
            {
                _outboundWaitingCount += quantity;
                WarehouseSimLog.Info(
                    $"出库请求 +{quantity} t={_clock.Now:F2}s；出库等候 {_outboundWaitingCount} 箱");
                ScheduleInitialOutfeedDispatches();
                return;
            }

            _inboundWaitingCount += quantity;
            WarehouseSimLog.Info(
                $"入库请求 +{quantity} t={_clock.Now:F2}s；入库等候 {_inboundWaitingCount} 箱");
            ScheduleInitialInfeedFeeds();
        }

        private void OnFlowPlanBatchRelease(int entryIndex)
        {
            _flowPlanScheduler?.OnBatchRelease(_queue, entryIndex, _clock.Now);
        }
    }
}
