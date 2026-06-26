using UnityEngine;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Playback
{
    /// <summary>回放链路诊断日志（统一前缀，便于 Console 过滤）。</summary>
    public static class SimPlaybackLog
    {
        public const string Tag = "[SimPlayback]";
        public const string RouteTag = "[SimPlayback][Route]";

        /// <summary>为 true 时输出 Info 级回放流水日志（默认关闭，减少 Console 噪音）。</summary>
        public static bool Verbose { get; set; }

        public static void Info(string message, Object context = null)
        {
            if (!Verbose)
            {
                return;
            }

            Debug.Log($"{Tag} {message}", context);
        }

        /// <summary>寻路完成后输出完整路线（始终记录，便于排查路径问题）。</summary>
        public static void Route(string message, Object context = null)
        {
            Debug.Log($"{RouteTag} {message}", context);
        }

        public static void Warn(string message, Object context = null)
        {
            Debug.LogWarning($"{Tag} {message}", context);
        }

        public static void Error(string message, Object context = null)
        {
            Debug.LogError($"{Tag} {message}", context);
        }

        public static string FormatEvent(SimPlaybackEvent evt, int index = -1)
        {
            var head = index >= 0 ? $"#{index} " : string.Empty;
            return $"{head}t={evt.SimTime:F2}s job={evt.JobId} phase={evt.Phase} stacker={evt.StackerId} slot={evt.Slot}";
        }
    }
}
