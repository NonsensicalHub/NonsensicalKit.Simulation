using System;
using System.Collections.Generic;
using System.Text;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <summary>生成面向开发者的仿真调试信息 Markdown 报告。</summary>
    public static class SimRunDebugMarkdownReportBuilder
    {
        public static string Build(
            WarehouseSimScenario scenario,
            StackerWarehouseSimulator simulator,
            SimRunResult result,
            string linkedUserReportFileName = null,
            SimRunExportDisplayInfo displayInfo = default)
        {
            if (simulator == null)
            {
                throw new ArgumentNullException(nameof(simulator));
            }

            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            var subTasks = simulator.LastSubTasks;
            var events = simulator.LastPlaybackEvents;
            var diag = simulator.LastEventDiagnostics;

            var sb = new StringBuilder(64 * 1024);
            var flowLabel = SimFlowPlanResolver.FormatFlowKindLabel(SimFlowPlanResolver.Resolve(scenario));
            var title = !string.IsNullOrEmpty(displayInfo.ScenarioName)
                ? $"{flowLabel}仿真调试信息 — {displayInfo.ScenarioName}"
                : $"{flowLabel}仿真调试信息";
            sb.Append("# ");
            sb.AppendLine(title);
            sb.AppendLine();
            sb.AppendLine("> 仅供开发与排障；业务结论请以仿真任务报告（HTML）为准。");
            sb.AppendLine();

            AppendMetaSection(sb, scenario, result, linkedUserReportFileName, displayInfo);
            AppendInternalCountsSection(sb, subTasks, events);
            AppendResourceUtilizationSection(sb, result);
            SimEventDiagnosticsReportBuilder.AppendMarkdown(sb, diag);
            SimOccupancySelfCheckMarkdownBuilder.AppendSection(sb, result);
            SimSubTaskTimelineSelfCheckMarkdownBuilder.AppendSection(sb, result);
            return sb.ToString();
        }

        private static void AppendMetaSection(
            StringBuilder sb,
            WarehouseSimScenario scenario,
            SimRunResult result,
            string linkedUserReportFileName,
            SimRunExportDisplayInfo displayInfo)
        {
            var rows = new List<(string Label, string Value)>
            {
                ("导出时间（UTC+8）", SimReportFormatting.FormatExportDateTime()),
                ("结果", result.Success ? "成功" : "失败"),
                ("消息", result.Message ?? "—"),
            };

            if (scenario != null)
            {
                if (!string.IsNullOrEmpty(displayInfo.ScenarioName))
                {
                    rows.Add(("场景", displayInfo.ScenarioName));
                }

                if (!string.IsNullOrEmpty(displayInfo.HardwareName))
                {
                    rows.Add(("硬件绑定", displayInfo.HardwareName));
                }

                if (!string.IsNullOrEmpty(displayInfo.StrategyName))
                {
                    rows.Add(("策略配置", displayInfo.StrategyName));
                }
            }

            if (!string.IsNullOrEmpty(linkedUserReportFileName))
            {
                rows.Add(("仿真报告", $"[{linkedUserReportFileName}]({linkedUserReportFileName})"));
            }

            SimReportMarkdown.AppendKvTable(sb, rows);
        }

        private static void AppendResourceUtilizationSection(StringBuilder sb, SimRunResult result)
        {
            if (result.ResourceUtilizations == null || result.ResourceUtilizations.Count == 0)
            {
                return;
            }

            SimReportMarkdown.AppendHeading(sb, 2, "设备利用率");
            var rows = new List<string[]>(result.ResourceUtilizations.Count);
            for (var i = 0; i < result.ResourceUtilizations.Count; i++)
            {
                var stat = result.ResourceUtilizations[i];
                rows.Add(new[]
                {
                    SimResourceUtilizationBuilder.FormatKindLabel(stat.Kind),
                    stat.Label ?? "—",
                    stat.BusySeconds.ToString("F2"),
                    stat.TotalSeconds.ToString("F2"),
                    $"{stat.UtilizationPercent:F1}%",
                });
            }

            SimReportMarkdown.AppendTable(
                sb,
                new[] { "设备类型", "资源", "忙碌时长（秒）", "仿真时长（秒）", "利用率" },
                rows);
        }

        private static void AppendInternalCountsSection(
            StringBuilder sb,
            IReadOnlyList<SimSubTask> subTasks,
            IReadOnlyList<SimPlaybackEvent> events)
        {
            SimReportMarkdown.AppendHeading(sb, 2, "内部计数");
            SimReportMarkdown.AppendKvTable(sb, new[]
            {
                ("回放事件数", (events?.Count ?? 0).ToString()),
                ("子任务记录数", (subTasks?.Count ?? 0).ToString()),
            });
        }
    }
}
