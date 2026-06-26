using System;
using UnityEngine;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Runtime
{
    /// <summary>垂直提升机回放插值；可通过 <see cref="CustomSampler"/> 覆盖默认动画。</summary>
    public static class ConveyorVerticalTransferPlayback
    {
        /// <summary>自定义采样器：入参为提升上下文，返回世界坐标。</summary>
        public static Func<VerticalTransferPlaybackContext, Vector3> CustomSampler;

        public readonly struct VerticalTransferPlaybackContext
        {
            public readonly SimConveyorVerticalTransferMotion Motion;
            public readonly Vector3 EntryWorld;
            public readonly Vector3 TargetWorld;
            public readonly float Progress;

            public VerticalTransferPlaybackContext(
                SimConveyorVerticalTransferMotion motion,
                Vector3 entryWorld,
                Vector3 targetWorld,
                float progress)
            {
                Motion = motion;
                EntryWorld = entryWorld;
                TargetWorld = targetWorld;
                Progress = progress;
            }
        }

        public static Vector3 Sample(
            in SimConveyorMapNode node,
            Vector3 entryWorld,
            Vector3 targetWorld,
            float progress)
        {
            progress = Mathf.Clamp01(progress);
            var ctx = new VerticalTransferPlaybackContext(node.TransferMotion, entryWorld, targetWorld, progress);
            if (CustomSampler != null)
            {
                return CustomSampler(ctx);
            }

            return node.TransferMotion switch
            {
                SimConveyorVerticalTransferMotion.LinearFull =>
                    Vector3.Lerp(entryWorld, targetWorld, progress),
                _ => new Vector3(
                    entryWorld.x,
                    Mathf.Lerp(entryWorld.y, targetWorld.y, progress),
                    entryWorld.z),
            };
        }
    }
}
