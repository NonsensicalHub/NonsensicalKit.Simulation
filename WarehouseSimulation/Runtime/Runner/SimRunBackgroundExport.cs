using System;
using System.Collections.Generic;
using System.Collections;
using System.Diagnostics;
using System.Threading.Tasks;
using UnityEngine;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;
using NonsensicalKit.Simulation.WarehouseSimulation.Simulation;
using Debug = UnityEngine.Debug;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Runtime
{
    /// <summary>在后台线程生成并写入仿真 HTML/Markdown 报告，完成后在主线程回调。</summary>
    internal static class SimRunBackgroundExport
    {
        public sealed class Outcome
        {
            public string HtmlPath { get; set; }
            public string DebugReportPath { get; set; }
            public Exception Error { get; set; }
            public double ElapsedSeconds { get; set; }
        }

        public static IEnumerator Run(
            MonoBehaviour host,
            WarehouseSimScenario scenario,
            StackerWarehouseSimulator simulator,
            SimRunResult result,
            string directory,
            Action<Outcome> onComplete,
            IReadOnlyList<GridIndex> initialOccupiedSlots = null,
            string scenarioDisplayName = null)
        {
            if (simulator == null || result == null)
            {
                onComplete?.Invoke(new Outcome
                {
                    Error = new InvalidOperationException("导出中止：请先运行仿真。"),
                });
                yield break;
            }

            scenario?.EnsureResolvedOnMainThread();
            var displayInfo = SimRunExportDisplayInfo.Capture(scenario, scenarioDisplayName);

            // 主线程拷贝回放快照，避免后台序列化时与后续运行互相踩。
            var eventsCopy = CopyList(simulator.LastPlaybackEvents);
            var subTasksCopy = CopyList(simulator.LastSubTasks);
            var initialCopy = CopyList(initialOccupiedSlots);
            var replayPayload = SimReplayHtmlCodec.BuildPayload(
                eventsCopy,
                subTasksCopy,
                initialCopy,
                result.TotalSimSeconds,
                result,
                displayInfo.ScenarioName);

            string resolvedDirectory;
            try
            {
                resolvedDirectory = SimRunExporter.ResolveExportDirectory(directory);
            }
            catch (Exception ex)
            {
                onComplete?.Invoke(new Outcome { Error = ex });
                yield break;
            }

            var sw = Stopwatch.StartNew();
            var lastProgressLog = sw.Elapsed;
            Debug.Log("[WarehouseSimulation] 报告导出已在后台线程启动（含回放数据）…", host);

            var task = Task.Run(() => SimRunExporter.ExportLastRunAtDirectory(
                scenario,
                simulator,
                result,
                resolvedDirectory,
                displayInfo: displayInfo,
                replayPayload: replayPayload));

            while (!task.IsCompleted)
            {
                if (sw.Elapsed - lastProgressLog >= TimeSpan.FromSeconds(3))
                {
                    Debug.Log(
                        $"[WarehouseSimulation] 后台报告导出进行中… 已等待 {sw.Elapsed.TotalSeconds:F0}s",
                        host);
                    lastProgressLog = sw.Elapsed;
                }

                yield return null;
            }

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
                    $"[WarehouseSimulation] 报告导出异常（{sw.Elapsed.TotalSeconds:F2}s）：{outcome.Error.Message}",
                    host);
            }
            else
            {
                var (htmlPath, debugPath) = task.Result;
                outcome = new Outcome
                {
                    HtmlPath = htmlPath,
                    DebugReportPath = debugPath,
                    ElapsedSeconds = sw.Elapsed.TotalSeconds,
                };
                Debug.Log(
                    $"[WarehouseSimulation] 报告导出完成，墙钟耗时 {sw.Elapsed.TotalSeconds:F2}s。",
                    host);
            }

            onComplete?.Invoke(outcome);
        }

        private static List<T> CopyList<T>(IReadOnlyList<T> source)
        {
            if (source == null || source.Count == 0)
            {
                return new List<T>();
            }

            var list = new List<T>(source.Count);
            for (var i = 0; i < source.Count; i++)
            {
                list.Add(source[i]);
            }

            return list;
        }
    }
}
