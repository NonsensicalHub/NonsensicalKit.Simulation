using System.Collections.Generic;
using System.Linq;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;
using NonsensicalKit.Simulation.WarehouseSimulation.Playback.Tasks;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Playback
{
    /// <summary>从子任务时间轴构建货位宏观可视快照。</summary>
    public static class WarehouseSlotPlaybackSnapshotBuilder
    {
        public static WarehouseSlotPlaybackSnapshot Build(
            double simTime,
            IReadOnlyList<SimSubTask> subTasks,
            bool highlightOnStackerMove,
            IReadOnlyCollection<GridIndex> initialOccupied = null)
        {
            var index = new WarehouseSlotPlaybackSnapshotIndex();
            index.Build(subTasks, highlightOnStackerMove, initialOccupied);
            return index.BuildAt(simTime);
        }

        public static WarehouseSlotPlaybackSnapshot BuildFromEvent(
            in SimPlaybackEvent evt,
            bool highlightOnStackerMove,
            IReadOnlyCollection<GridIndex> initialOccupied = null,
            IReadOnlyCollection<int> outboundJobIds = null)
        {
            var occupied = SeedOccupied(initialOccupied);
            GridIndex? highlight = null;
            switch (evt.Phase)
            {
                case SimPlaybackPhase.Arrived:
                    highlight = evt.Slot;
                    break;

                case SimPlaybackPhase.StackerMove:
                    if (highlightOnStackerMove)
                    {
                        highlight = evt.Slot;
                    }

                    break;

                case SimPlaybackPhase.StackerPick:
                    // StackerPick 事件在取货完成时刻记录，与堆垛机叉上料箱同步。
                    if (outboundJobIds != null && outboundJobIds.Contains(evt.JobId))
                    {
                        occupied.Remove(evt.Slot);
                    }

                    break;

                case SimPlaybackPhase.StackerPlace:
                case SimPlaybackPhase.Completed:
                    if (outboundJobIds != null && outboundJobIds.Contains(evt.JobId))
                    {
                        occupied.Remove(evt.Slot);
                    }
                    else
                    {
                        occupied.Add(evt.Slot);
                    }

                    break;
            }

            return new WarehouseSlotPlaybackSnapshot
            {
                OccupiedSlots = occupied,
                HighlightSlot = highlight,
            };
        }

        private static HashSet<GridIndex> SeedOccupied(IReadOnlyCollection<GridIndex> initialOccupied)
        {
            var occupied = new HashSet<GridIndex>();
            if (initialOccupied == null)
            {
                return occupied;
            }

            foreach (var slot in initialOccupied)
            {
                occupied.Add(slot);
            }

            return occupied;
        }

        private static void ApplyPlacementToOccupancy(
            HashSet<GridIndex> occupied,
            in SimSubTask task,
            HashSet<int> outboundJobIds)
        {
            if (outboundJobIds.Contains(task.JobId))
            {
                occupied.Remove(task.Slot);
                return;
            }

            occupied.Add(task.Slot);
        }
    }
}
