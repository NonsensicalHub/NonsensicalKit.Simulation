using System;
using System.Collections;
using System.Diagnostics;
using System.Threading.Tasks;
using UnityEngine;
using NonsensicalKit.Simulation.WarehouseSimulation.Allocation;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;
using NonsensicalKit.Simulation.WarehouseSimulation.Simulation;
using Debug = UnityEngine.Debug;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Runtime
{
    /// <summary>在后台线程执行仓库仿真，完成后在主线程回调。</summary>
    internal static class SimRunBackgroundRun
    {
        public sealed class Outcome
        {
            public StackerWarehouseSimulator Simulator { get; set; }
            public SimRunResult Result { get; set; }
            public Exception Error { get; set; }
            public double ElapsedSeconds { get; set; }
        }

        public static bool TryApplyOutcome(
            Outcome outcome,
            out StackerWarehouseSimulator simulator,
            out SimRunResult result)
        {
            if (outcome?.Error != null)
            {
                simulator = null;
                result = null;
                return false;
            }

            simulator = outcome.Simulator;
            result = outcome.Result;
            return true;
        }

        public static IEnumerator Run(
            MonoBehaviour host,
            WarehouseSimScenario scenario,
            WarehouseGridConfig grid,
            SimRunOptions? runOptions,
            bool logKeySteps,
            bool logVerbose,
            Action<Outcome> onComplete)
        {
            if (scenario == null)
            {
                onComplete?.Invoke(new Outcome { Error = new InvalidOperationException("Scenario 未配置。") });
                yield break;
            }

            if (grid == null)
            {
                onComplete?.Invoke(new Outcome { Error = new InvalidOperationException("网格配置未配置。") });
                yield break;
            }

            scenario.EnsureResolvedOnMainThread();

            var previousKeySteps = WarehouseSimLog.KeySteps;
            var previousVerbose = WarehouseSimLog.Verbose;
            WarehouseSimLog.KeySteps = logKeySteps;
            WarehouseSimLog.Verbose = logVerbose;

            var sw = Stopwatch.StartNew();
            var lastProgressLog = sw.Elapsed;
            Debug.Log("[WarehouseSimulation] 仿真已在后台线程启动…", host);

            var task = Task.Run(() =>
            {
                var allocator = SlotAllocatorFactory.Create(scenario, grid);
                return WarehouseSimulationService.RunOnBackgroundThread(scenario, allocator, runOptions);
            });

            while (!task.IsCompleted)
            {
                if (sw.Elapsed - lastProgressLog >= TimeSpan.FromSeconds(3))
                {
                    Debug.Log(
                        $"[WarehouseSimulation] 后台仿真进行中… 已等待 {sw.Elapsed.TotalSeconds:F0}s",
                        host);
                    lastProgressLog = sw.Elapsed;
                }

                yield return null;
            }

            try
            {
                sw.Stop();
                Outcome outcome;
                if (task.IsFaulted)
                {
                    outcome = new Outcome
                    {
                        Error = task.Exception?.GetBaseException() ?? task.Exception,
                        ElapsedSeconds = sw.Elapsed.TotalSeconds,
                    };
                    Debug.LogError(
                        $"[WarehouseSimulation] 后台仿真异常（{sw.Elapsed.TotalSeconds:F2}s）：{outcome.Error.Message}",
                        host);
                }
                else
                {
                    var (simulator, result) = task.Result;
                    outcome = new Outcome
                    {
                        Simulator = simulator,
                        Result = result,
                        ElapsedSeconds = sw.Elapsed.TotalSeconds,
                    };
                    var timing = FormatCompletionTiming(sw.Elapsed.TotalSeconds, result?.WallClockProfile);
                    Debug.Log(
                        $"[WarehouseSimulation] 后台仿真完成，{timing}",
                        host);
                }

                onComplete?.Invoke(outcome);
            }
            finally
            {
                WarehouseSimLog.KeySteps = previousKeySteps;
                WarehouseSimLog.Verbose = previousVerbose;
            }
        }

        private static string FormatCompletionTiming(double taskElapsedSeconds, SimWallClockProfileSnapshot profile)
        {
            if (profile == null || !profile.Enabled || profile.TotalWallMilliseconds <= 0)
            {
                return $"任务墙钟耗时 {taskElapsedSeconds:F2}s";
            }

            var simSeconds = profile.TotalWallMilliseconds / 1000.0;
            var overheadSeconds = taskElapsedSeconds - simSeconds;
            if (overheadSeconds >= 0.5)
            {
                return
                    $"任务墙钟耗时 {taskElapsedSeconds:F2}s（仿真引擎 {simSeconds:F2}s，" +
                    $"线程排队/GC {overheadSeconds:F2}s）";
            }

            return $"任务墙钟耗时 {taskElapsedSeconds:F2}s（仿真引擎 {simSeconds:F2}s）";
        }
    }
}
