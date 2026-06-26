using System;
using System.Threading;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Config
{
    /// <summary>未绑定策略资源时使用的内置默认策略（须在 Unity 主线程预先创建）。</summary>
    public static class SimStrategyDefaults
    {
        private static WarehouseSimStrategyProfile s_instance;
        private static int? s_mainThreadId;

        public static WarehouseSimStrategyProfile Instance
        {
            get
            {
                if (s_instance == null)
                {
                    if (s_mainThreadId.HasValue && Thread.CurrentThread.ManagedThreadId != s_mainThreadId.Value)
                    {
                        throw new InvalidOperationException(
                            "默认策略未在主线程初始化。请为 Scenario 绑定 Strategy，或在启动后台仿真前调用 EnsureCreatedOnMainThread。");
                    }

                    s_instance = CreateRuntimeDefault();
                }

                return s_instance;
            }
        }

        /// <summary>在 Unity 主线程（协程/MonoBehaviour）预先创建运行时默认策略，供后台仿真线程只读使用。</summary>
        public static void EnsureCreatedOnMainThread()
        {
            var threadId = Thread.CurrentThread.ManagedThreadId;
            if (s_mainThreadId == null)
            {
                s_mainThreadId = threadId;
            }
            else if (s_mainThreadId != threadId)
            {
                throw new InvalidOperationException("SimStrategyDefaults 必须在 Unity 主线程调用。");
            }

            if (s_instance == null)
            {
                s_instance = CreateRuntimeDefault();
            }
        }

        private static WarehouseSimStrategyProfile CreateRuntimeDefault()
        {
            var profile = UnityEngine.ScriptableObject.CreateInstance<WarehouseSimStrategyProfile>();
            profile.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
            return profile;
        }
    }
}
