using UnityEngine;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <summary>仿真计算过程日志（与回放日志分离）。</summary>
    public static class WarehouseSimLog
    {
        public const string Tag = "[WarehouseSimulation]";

        [System.ThreadStatic]
        private static bool s_suppressUnityOutput;

        /// <summary>为 true 时输出重试、寻路等详细流水（默认关闭）。</summary>
        public static bool Verbose { get; set; }

        /// <summary>关键步骤：到货、放货、完成、汇总等（默认开启）。</summary>
        public static bool KeySteps { get; set; } = true;

        /// <summary>后台仿真线程内禁止调用 Unity Debug API。</summary>
        public static void EnterBackgroundThread() => s_suppressUnityOutput = true;

        public static void ExitBackgroundThread() => s_suppressUnityOutput = false;

        /// <summary>是否应输出日志（主线程且未关闭关键步骤）。</summary>
        public static bool ShouldEmit => KeySteps && !s_suppressUnityOutput;

        public static void Info(string message)
        {
            if (!ShouldEmit)
            {
                return;
            }

            Debug.Log($"{Tag} {message}");
        }

        public static void Info(System.Func<string> messageFactory)
        {
            if (!ShouldEmit)
            {
                return;
            }

            Debug.Log($"{Tag} {messageFactory()}");
        }

        public static void Detail(string message)
        {
            if (!Verbose || s_suppressUnityOutput)
            {
                return;
            }

            Debug.Log($"{Tag} {message}");
        }

        public static void Warn(string message)
        {
            if (s_suppressUnityOutput)
            {
                return;
            }

            Debug.LogWarning($"{Tag} {message}");
        }

        public static void Error(string message)
        {
            if (s_suppressUnityOutput)
            {
                return;
            }

            Debug.LogError($"{Tag} {message}");
        }
    }
}
