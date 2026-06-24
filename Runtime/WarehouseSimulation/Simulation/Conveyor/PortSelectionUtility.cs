using System;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <summary>入库口与出库口共用的端口选择算法（轮询 / 最短队列）。</summary>
    internal static class PortSelectionUtility
    {
        internal const int PhysicalOccupancyPenalty = 1000;

        internal static int SelectPort(
            InfeedPortSelectionStrategy strategy,
            int portCount,
            ref int roundRobinCursor,
            Func<int, bool> isPortAvailable,
            Func<int, int> getQueueLoad)
        {
            if (portCount <= 0 || isPortAvailable == null || getQueueLoad == null)
            {
                return -1;
            }

            if (strategy == InfeedPortSelectionStrategy.ShortestQueue)
            {
                var bestPort = -1;
                var bestLoad = int.MaxValue;
                for (var port = 0; port < portCount; port++)
                {
                    if (!isPortAvailable(port))
                    {
                        continue;
                    }

                    var load = getQueueLoad(port);
                    if (load >= bestLoad)
                    {
                        continue;
                    }

                    bestLoad = load;
                    bestPort = port;
                }

                return bestPort;
            }

            var start = roundRobinCursor >= 0 ? roundRobinCursor % portCount : 0;
            for (var offset = 0; offset < portCount; offset++)
            {
                var port = (start + offset) % portCount;
                if (!isPortAvailable(port))
                {
                    continue;
                }

                roundRobinCursor = (port + 1) % portCount;
                return port;
            }

            return -1;
        }

        internal static int FillCandidatePorts(
            InfeedPortSelectionStrategy strategy,
            int portCount,
            int roundRobinCursor,
            int[] portOrderScratch,
            Func<int, bool> isPortAvailable,
            Func<int, int> getQueueLoad)
        {
            if (portCount <= 0
                || portOrderScratch == null
                || isPortAvailable == null
                || getQueueLoad == null)
            {
                return 0;
            }

            var count = 0;
            if (strategy == InfeedPortSelectionStrategy.ShortestQueue)
            {
                for (var port = 0; port < portCount; port++)
                {
                    if (!isPortAvailable(port))
                    {
                        continue;
                    }

                    portOrderScratch[count++] = port;
                }

                SortPortsByQueueLoad(portOrderScratch, count, getQueueLoad);
                return count;
            }

            var start = roundRobinCursor >= 0 ? roundRobinCursor % portCount : 0;
            for (var offset = 0; offset < portCount; offset++)
            {
                var port = (start + offset) % portCount;
                if (!isPortAvailable(port))
                {
                    continue;
                }

                portOrderScratch[count++] = port;
            }

            return count;
        }

        internal static int GetQueueLoadWithOccupancyPenalty(
            int port,
            int[] reservationCounts,
            bool[] cargoOccupied)
        {
            var load = reservationCounts[port];
            if (cargoOccupied != null
                && port < cargoOccupied.Length
                && cargoOccupied[port])
            {
                load += PhysicalOccupancyPenalty;
            }

            return load;
        }

        private static void SortPortsByQueueLoad(
            int[] portOrderScratch,
            int count,
            Func<int, int> getQueueLoad)
        {
            for (var i = 1; i < count; i++)
            {
                var key = portOrderScratch[i];
                var keyLoad = getQueueLoad(key);
                var j = i - 1;
                while (j >= 0)
                {
                    var leftLoad = getQueueLoad(portOrderScratch[j]);
                    if (leftLoad < keyLoad
                        || (leftLoad == keyLoad && portOrderScratch[j] <= key))
                    {
                        break;
                    }

                    portOrderScratch[j + 1] = portOrderScratch[j];
                    j--;
                }

                portOrderScratch[j + 1] = key;
            }
        }
    }
}
