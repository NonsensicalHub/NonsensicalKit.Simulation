using System;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Core
{
    /// <summary>
    /// 路口处沿同一条直线驶入的方向。
    /// 直角汇合不算同一条线；接近 180° 对开的两条边算同一条线。
    /// </summary>
    public static class ConveyorJunctionAxis
    {
        public static bool TryBuildResourceId(
            WarehouseConveyorMap map,
            int fromNodeIndex,
            int toNodeIndex,
            out string resourceId)
        {
            resourceId = null;
            if (!TryGetBucket(map, fromNodeIndex, toNodeIndex, out var bucket))
            {
                return false;
            }

            resourceId = $"jaxis|{SimEntityNaming.FormatNode(map, toNodeIndex)}|{bucket}";
            return true;
        }

        public static bool TryGetBucket(
            WarehouseConveyorMap map,
            int fromNodeIndex,
            int toNodeIndex,
            out int bucket)
        {
            bucket = 0;
            if (map?.Nodes == null
                || fromNodeIndex < 0
                || toNodeIndex < 0
                || fromNodeIndex >= map.Nodes.Length
                || toNodeIndex >= map.Nodes.Length)
            {
                return false;
            }

            var from = map.Nodes[fromNodeIndex].EditorPosition;
            var to = map.Nodes[toNodeIndex].EditorPosition;
            bucket = Bucket(to.x - from.x, to.y - from.y);
            return true;
        }

        public static int Bucket(float dx, float dy)
        {
            if (dx * dx + dy * dy < 1e-8f)
            {
                return 0;
            }

            var angle = Math.Atan2(dy, dx);
            if (angle < 0d)
            {
                angle += Math.PI;
            }

            if (angle >= Math.PI - 1e-6d)
            {
                angle = 0d;
            }

            const int buckets = 4;
            var bucket = (int)Math.Round(angle / (Math.PI / buckets));
            if (bucket < 0 || bucket >= buckets)
            {
                return 0;
            }

            return bucket;
        }
    }
}
