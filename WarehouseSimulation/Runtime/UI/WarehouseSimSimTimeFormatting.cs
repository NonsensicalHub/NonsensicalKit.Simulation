using System;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Runtime
{
    internal static class WarehouseSimSimTimeFormatting
    {
        public static string Format(double simTimeSeconds)
        {
            var totalSeconds = Math.Max(0, simTimeSeconds);
            var hours = (int)(totalSeconds / 3600);
            var minutes = (int)(totalSeconds % 3600 / 60);
            var seconds = (int)(totalSeconds % 60);
            return $"{hours:D2}:{minutes:D2}:{seconds:D2}";
        }
    }
}
