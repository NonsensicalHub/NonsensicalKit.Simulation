using System.Collections.Generic;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Playback
{
    /// <summary>将堆垛机子任务时间轴聚合为宏观堆垛机任务（入库：取货点→货位；出库：货位→取货点）。</summary>
    public static class StackerJobPlaybackResolver
    {
        private const double TimeEpsilon = 1e-6;

        public static bool TryBuildActiveContext(
            IReadOnlyList<SimSubTask> subTasks,
            SimSubTaskPlaybackIndex index,
            int stackerId,
            double simTime,
            out StackerJobPlaybackContext context)
        {
            context = default;
            if (index != null
                    ? !index.TryGetActiveForStacker(stackerId, simTime, out var active)
                    : !SimSubTaskQuery.TryGetActiveForStacker(subTasks, stackerId, simTime, out active))
            {
                return false;
            }

            if (!TryBuildJobTask(subTasks, index, stackerId, active.JobId, out var jobTask))
            {
                return false;
            }

            context = new StackerJobPlaybackContext
            {
                Task = jobTask,
                SimTime = simTime,
                Phase = MapMacroPhase(active.Kind),
                PhaseProgress = active.NormalizedProgress(simTime),
                ActiveSubTask = active,
                HasActiveSubTask = true,
            };
            return true;
        }

        public static bool TryGetLastCompletedJob(
            IReadOnlyList<SimSubTask> subTasks,
            SimSubTaskPlaybackIndex index,
            int stackerId,
            double simTime,
            out StackerJobPlaybackTask task)
        {
            task = default;
            SimSubTask last = default;
            var found = false;
            IReadOnlyList<SimSubTask> stackerTasks;
            if (index != null && index.TryGetStackerTasks(stackerId, out stackerTasks))
            {
                ScanLastCompleted(stackerTasks, simTime, ref last, ref found);
            }
            else if (subTasks != null)
            {
                for (var i = 0; i < subTasks.Count; i++)
                {
                    var st = subTasks[i];
                    if (st.StackerId != stackerId)
                    {
                        continue;
                    }

                    if (!IsStackerMotionKind(st.Kind))
                    {
                        continue;
                    }

                    if (simTime < st.EndSimTime - TimeEpsilon)
                    {
                        continue;
                    }

                    if (!found || st.EndSimTime >= last.EndSimTime)
                    {
                        last = st;
                        found = true;
                    }
                }
            }

            if (!found)
            {
                return false;
            }

            if (last.Kind != SimSubTaskKind.StackerPlace && last.Kind != SimSubTaskKind.Completed)
            {
                return false;
            }

            if (!TryBuildJobTask(subTasks, index, stackerId, last.JobId, out task))
            {
                return false;
            }

            if (TryGetLastStackerPlacePose(subTasks, index, stackerId, simTime, out var idlePose))
            {
                task.IdleStackerSlot = idlePose;
                task.HasIdleStackerSlot = true;
            }

            if (task.IsOutbound
                && TryGetJobTasks(index, subTasks, last.JobId, out var jobTasks)
                && SimSubTaskQuery.IsInOutboundConveyorVisualWindow(jobTasks, simTime))
            {
                return false;
            }

            return true;
        }

        private static void ScanLastCompleted(
            IReadOnlyList<SimSubTask> stackerTasks,
            double simTime,
            ref SimSubTask last,
            ref bool found)
        {
            for (var i = 0; i < stackerTasks.Count; i++)
            {
                var st = stackerTasks[i];
                if (simTime < st.EndSimTime - TimeEpsilon)
                {
                    continue;
                }

                if (!found || st.EndSimTime >= last.EndSimTime)
                {
                    last = st;
                    found = true;
                }
            }
        }

        public static bool TryBuildJobTask(
            IReadOnlyList<SimSubTask> subTasks,
            SimSubTaskPlaybackIndex index,
            int stackerId,
            int jobId,
            out StackerJobPlaybackTask jobTask)
        {
            jobTask = default;
            jobTask.JobId = jobId;
            jobTask.StackerId = stackerId;
            var hasBounds = false;

            if (index != null && index.TryGetJobTasks(jobId, out var jobTasks))
            {
                jobTask.IsOutbound = SimSubTaskQuery.IsOutboundJob(jobTasks);
                CollectJobBounds(jobTasks, stackerId, ref jobTask, ref hasBounds);
            }
            else if (subTasks != null)
            {
                var collected = new List<SimSubTask>();
                for (var i = 0; i < subTasks.Count; i++)
                {
                    var st = subTasks[i];
                    if (st.JobId != jobId)
                    {
                        continue;
                    }

                    collected.Add(st);
                    if (st.StackerId != stackerId || !IsStackerMotionKind(st.Kind))
                    {
                        continue;
                    }

                    MergeJobBounds(st, ref jobTask, ref hasBounds);
                }

                jobTask.IsOutbound = SimSubTaskQuery.IsOutboundJob(collected);
            }

            return hasBounds && jobTask.TaskEndSimTime > jobTask.TaskStartSimTime + TimeEpsilon;
        }

        private static void CollectJobBounds(
            IReadOnlyList<SimSubTask> jobTasks,
            int stackerId,
            ref StackerJobPlaybackTask jobTask,
            ref bool hasBounds)
        {
            for (var i = 0; i < jobTasks.Count; i++)
            {
                var st = jobTasks[i];
                if (st.StackerId != stackerId || !IsStackerMotionKind(st.Kind))
                {
                    continue;
                }

                MergeJobBounds(st, ref jobTask, ref hasBounds);
            }
        }

        private static void MergeJobBounds(
            in SimSubTask st,
            ref StackerJobPlaybackTask jobTask,
            ref bool hasBounds)
        {
            if (!hasBounds)
            {
                jobTask.PickupPointIndex = st.PickupPointIndex;
                jobTask.TargetSlot = st.Slot;
                jobTask.TaskStartSimTime = st.StartSimTime;
                jobTask.TaskEndSimTime = st.EndSimTime;
                hasBounds = true;
                return;
            }

            if (st.StartSimTime < jobTask.TaskStartSimTime)
            {
                jobTask.TaskStartSimTime = st.StartSimTime;
            }

            if (st.EndSimTime > jobTask.TaskEndSimTime)
            {
                jobTask.TaskEndSimTime = st.EndSimTime;
            }

            if (st.PickupPointIndex >= 0)
            {
                jobTask.PickupPointIndex = st.PickupPointIndex;
            }

            if (st.Slot.Column >= 0 || st.Slot.Level >= 0)
            {
                jobTask.TargetSlot = st.Slot;
            }
        }

        private static StackerJobMacroPhase MapMacroPhase(SimSubTaskKind kind) =>
            kind switch
            {
                SimSubTaskKind.StackerWait => StackerJobMacroPhase.Waiting,
                SimSubTaskKind.StackerApproach => StackerJobMacroPhase.Approaching,
                SimSubTaskKind.StackerPick => StackerJobMacroPhase.Picking,
                SimSubTaskKind.StackerMove => StackerJobMacroPhase.Moving,
                SimSubTaskKind.StackerPlace => StackerJobMacroPhase.Placing,
                _ => StackerJobMacroPhase.None,
            };

        private static bool TryGetLastStackerPlacePose(
            IReadOnlyList<SimSubTask> subTasks,
            SimSubTaskPlaybackIndex index,
            int stackerId,
            double simTime,
            out GridIndex idlePose)
        {
            idlePose = default;
            SimSubTask lastPlace = default;
            var found = false;

            if (index != null && index.TryGetStackerTasks(stackerId, out var stackerTasks))
            {
                ScanLastStackerPlace(stackerTasks, simTime, ref lastPlace, ref found);
            }
            else if (subTasks != null)
            {
                for (var i = 0; i < subTasks.Count; i++)
                {
                    var st = subTasks[i];
                    if (st.StackerId != stackerId || st.Kind != SimSubTaskKind.StackerPlace)
                    {
                        continue;
                    }

                    if (simTime < st.EndSimTime - TimeEpsilon)
                    {
                        continue;
                    }

                    if (!found || st.EndSimTime >= lastPlace.EndSimTime)
                    {
                        lastPlace = st;
                        found = true;
                    }
                }
            }

            if (!found || !lastPlace.HasStackerPose)
            {
                return false;
            }

            idlePose = lastPlace.StackerToSlot;
            return true;
        }

        private static void ScanLastStackerPlace(
            IReadOnlyList<SimSubTask> stackerTasks,
            double simTime,
            ref SimSubTask lastPlace,
            ref bool found)
        {
            for (var i = 0; i < stackerTasks.Count; i++)
            {
                var st = stackerTasks[i];
                if (st.Kind != SimSubTaskKind.StackerPlace || simTime < st.EndSimTime - TimeEpsilon)
                {
                    continue;
                }

                if (!found || st.EndSimTime >= lastPlace.EndSimTime)
                {
                    lastPlace = st;
                    found = true;
                }
            }
        }

        private static bool TryGetJobTasks(
            SimSubTaskPlaybackIndex index,
            IReadOnlyList<SimSubTask> subTasks,
            int jobId,
            out IReadOnlyList<SimSubTask> jobTasks)
        {
            if (index != null && index.TryGetJobTasks(jobId, out jobTasks))
            {
                return true;
            }

            jobTasks = null;
            return false;
        }

        private static bool IsStackerMotionKind(SimSubTaskKind kind) =>
            kind is SimSubTaskKind.StackerWait
                or SimSubTaskKind.StackerApproach
                or SimSubTaskKind.StackerPick
                or SimSubTaskKind.StackerMove
                or SimSubTaskKind.StackerPlace;
    }
}
