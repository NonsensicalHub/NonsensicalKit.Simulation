using System;
using System.Collections.Generic;
using System.Text;
using NonsensicalKit.Simulation.WarehouseSimulation.Allocation;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <summary>生成本地仿真的人类可读 HTML 任务报告。</summary>
    public static class SimRunHtmlReportBuilder
    {
        private static readonly SimCargoJobValidationReport s_scratchReport = new();

        public static string Build(
            WarehouseSimScenario scenario,
            StackerWarehouseSimulator simulator,
            SimRunResult result,
            string debugMarkdownFileName = null,
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
            var endSimTime = result.TotalSimSeconds > 0
                ? result.TotalSimSeconds
                : SimSubTaskQuery.GetTimelineEnd(subTasks);

            var flowLabel = SimFlowPlanResolver.FormatFlowKindLabel(SimFlowPlanResolver.Resolve(scenario));
            var title = !string.IsNullOrEmpty(displayInfo.ScenarioName)
                ? $"{flowLabel}仿真报告 — {displayInfo.ScenarioName}"
                : $"{flowLabel}仿真报告";

            var map = scenario?.ResolvedHardwareBindings?.ConveyorMap;
            ConveyorMapTopology.TryBuild(map, out var topology, out _);
            var resourceUtilizations = ResolveResourceUtilizations(result, subTasks, endSimTime, map, topology);

            var sb = new StringBuilder(128 * 1024);
            SimReportHtml.AppendDocumentStart(sb, title);
            AppendToolbar(sb);
            AppendPageHeader(sb, scenario, result, displayInfo);
            AppendSummarySection(sb, scenario, result, simulator, debugMarkdownFileName, resourceUtilizations);
            SimReportTopologyHtmlBuilder.AppendSection(sb, map, displayInfo.ConveyorMapName);
            AppendStatsCards(sb, result);
            AppendResourceUtilizationSection(sb, resourceUtilizations);
            AppendResourceWaitSection(sb, result);
            AppendCompletionSection(sb, result, map, topology);
            AppendJobSection(sb, subTasks, endSimTime, map, topology);
            SimReportHtml.AppendDocumentEnd(sb);
            return sb.ToString();
        }

        private static List<SimResourceUtilizationStat> ResolveResourceUtilizations(
            SimRunResult result,
            IReadOnlyList<SimSubTask> subTasks,
            double totalSimSeconds,
            WarehouseConveyorMap map,
            ConveyorMapTopology topology)
        {
            if (result?.ResourceUtilizations != null && result.ResourceUtilizations.Count > 0)
            {
                return result.ResourceUtilizations;
            }

            return SimResourceUtilizationBuilder.Build(subTasks, totalSimSeconds, map, topology);
        }

        private static void AppendToolbar(StringBuilder sb)
        {
            sb.AppendLine("<div class=\"toolbar\">");
            sb.AppendLine("<input id=\"job-filter\" type=\"search\" placeholder=\"筛选任务（编号、货位、堆垛机…）\" autocomplete=\"off\">");
            sb.AppendLine("<a href=\"#summary\">概要</a>");
            sb.AppendLine("<a href=\"#topology\">地图拓扑</a>");
            sb.AppendLine("<a href=\"#utilization\">设备利用率</a>");
            sb.AppendLine("<a href=\"#completions\">完成明细</a>");
            sb.AppendLine("<a href=\"#jobs\">任务明细</a>");
            sb.AppendLine("</div>");
        }

        private static void AppendPageHeader(
            StringBuilder sb,
            WarehouseSimScenario scenario,
            SimRunResult result,
            SimRunExportDisplayInfo displayInfo)
        {
            sb.AppendLine("<header>");
            var flowLabel = SimFlowPlanResolver.FormatFlowKindLabel(SimFlowPlanResolver.Resolve(scenario));
            sb.Append("<h1>");
            sb.Append(flowLabel);
            sb.AppendLine("仿真任务报告</h1>");
            sb.Append("<p class=\"meta\">导出时间（UTC+8）：");
            sb.Append(SimReportFormatting.FormatExportDateTime());
            sb.AppendLine("</p>");

            if (scenario != null)
            {
                sb.AppendLine("<dl class=\"meta\">");
                if (!string.IsNullOrEmpty(displayInfo.ScenarioName))
                {
                    AppendMetaRow(sb, "场景", displayInfo.ScenarioName);
                }

                if (!string.IsNullOrEmpty(displayInfo.HardwareName))
                {
                    AppendMetaRow(sb, "硬件绑定", displayInfo.HardwareName);
                }

                if (!string.IsNullOrEmpty(displayInfo.StrategyName))
                {
                    AppendMetaRow(sb, "策略配置", displayInfo.StrategyName);
                }

                AppendMetaRow(sb, "计划任务数", result.TargetJobCount.ToString());
                AppendMetaRow(sb, "流程计划", result.FlowPlanSummaryLabel ?? SimStrategyLabels.FormatFlowPlan(SimFlowPlanResolver.Resolve(scenario)));
                AppendMetaRow(
                    sb,
                    "初始占用",
                    SimStrategyLabels.FormatInitialOccupancy(
                        scenario.InitialOccupancyRatio,
                        scenario.InitialOccupancyRandom));
                AppendMetaRow(
                    sb,
                    "堆垛机放置策略",
                    SimStrategyLabels.FormatStackerPlacement(scenario.ResolvedStrategy.StackerPlacementStrategy));
                AppendMetaRow(
                    sb,
                    "入库口选择策略",
                    SimStrategyLabels.FormatInfeedPortSelection(scenario.ResolvedStrategy.InfeedPortSelectionStrategy));
                AppendMetaRow(
                    sb,
                    "输送路径策略",
                    SimStrategyLabels.FormatConveyorRouting(scenario.ResolvedStrategy.ConveyorRoutingStrategy));
                sb.AppendLine("</dl>");
            }
            else if (!string.IsNullOrEmpty(result.StackerPlacementStrategyLabel)
                     || !string.IsNullOrEmpty(result.FlowPlanSummaryLabel)
                     || !string.IsNullOrEmpty(result.InfeedPortSelectionStrategyLabel))
            {
                sb.AppendLine("<dl class=\"meta\">");
                if (!string.IsNullOrEmpty(result.StackerPlacementStrategyLabel))
                {
                    AppendMetaRow(sb, "堆垛机放置策略", result.StackerPlacementStrategyLabel);
                }

                if (!string.IsNullOrEmpty(result.FlowPlanSummaryLabel))
                {
                    AppendMetaRow(sb, "流程计划", result.FlowPlanSummaryLabel);
                }

                if (!string.IsNullOrEmpty(result.InfeedPortSelectionStrategyLabel))
                {
                    AppendMetaRow(sb, "入库口选择策略", result.InfeedPortSelectionStrategyLabel);
                }

                if (!string.IsNullOrEmpty(result.ConveyorRoutingStrategyLabel))
                {
                    AppendMetaRow(sb, "输送路径策略", result.ConveyorRoutingStrategyLabel);
                }

                sb.AppendLine("</dl>");
            }

            sb.Append("<p style=\"margin-top:12px\">");
            sb.Append(result.Success ? "<span class=\"badge badge-ok\">成功</span>" : "<span class=\"badge badge-fail\">失败</span>");
            sb.Append(" <strong>");
            sb.Append(SimReportHtml.Escape(result.Message));
            sb.AppendLine("</strong></p>");
            sb.AppendLine("</header>");
        }

        private static void AppendMetaRow(StringBuilder sb, string label, string value)
        {
            sb.Append("<dt>");
            sb.Append(SimReportHtml.Escape(label));
            sb.Append("</dt><dd>");
            sb.Append(SimReportHtml.Escape(value));
            sb.AppendLine("</dd>");
        }

        private static void AppendSummarySection(
            StringBuilder sb,
            WarehouseSimScenario scenario,
            SimRunResult result,
            StackerWarehouseSimulator simulator,
            string debugMarkdownFileName,
            IReadOnlyList<SimResourceUtilizationStat> resourceUtilizations)
        {
            sb.AppendLine("<details id=\"summary\" class=\"section-fold card\" open>");
            sb.AppendLine("<summary><span class=\"fold-title\">仿真概要</span></summary>");
            sb.AppendLine("<div class=\"fold-body\">");
            SimReportHtml.AppendTableStart(sb);
            SimReportHtml.AppendTableHead(sb, "指标", "数值");
            sb.AppendLine("<tbody>");
            SimReportHtml.AppendKvRow(sb, "完成任务数", $"{result.CompletedJobCount} / {result.TargetJobCount}");
            SimReportHtml.AppendKvRow(
                sb,
                "可存储货位 / 堆垛机可达可分配",
                $"{result.StorageSlotCount} / {result.AllocatableStorageSlotCount}");
            SimReportHtml.AppendKvRow(
                sb,
                "初始占用货位",
                result.InitialOccupiedStorageSlotCount.ToString());
            if (result.FailureCounts != null && result.FailureCounts.Count > 0)
            {
                SimReportHtml.AppendKvRow(
                    sb,
                    "失败原因统计",
                    SimJobFailureReasonLabels.FormatSummary(result.FailureCounts));
            }

            SimReportHtml.AppendKvRow(
                sb,
                "失败统计说明",
                "Message 中的失败数可含未建单的等候箱，与完成明细条数之和可能小于目标数。");
            SimReportHtml.AppendKvRow(sb, "堆垛机放置策略", result.StackerPlacementStrategyLabel ?? "—");
            SimReportHtml.AppendKvRow(sb, "流程计划", result.FlowPlanSummaryLabel ?? "—");
            SimReportHtml.AppendKvRow(sb, "入库口选择策略", result.InfeedPortSelectionStrategyLabel ?? "—");
            SimReportHtml.AppendKvRow(sb, "输送路径策略", result.ConveyorRoutingStrategyLabel ?? "—");
            SimReportHtml.AppendKvRow(
                sb,
                "总仿真时长",
                $"{result.TotalSimSeconds:F2} s（{SimReportFormatting.FormatDuration(result.TotalSimSeconds)}）");
            SimReportHtml.AppendKvRow(sb, "吞吐", $"{result.ThroughputPerHour:F1} 箱/小时");
            AppendUtilizationSummaryRows(sb, resourceUtilizations);
            SimOccupancySelfCheckHtmlBuilder.AppendToSummary(sb, result);
            SimSubTaskTimelineSelfCheckHtmlBuilder.AppendToSummary(sb, result);
            AppendOccupancyRows(sb, scenario, simulator);
            if (HasSelfCheckFailures(result) && !string.IsNullOrEmpty(debugMarkdownFileName))
            {
                SimReportHtml.AppendKvRow(sb, "调试明细文件", debugMarkdownFileName);
            }

            sb.AppendLine("</tbody>");
            SimReportHtml.AppendTableEnd(sb);
            sb.AppendLine("</div></details>");
        }

        private static bool HasSelfCheckFailures(SimRunResult result) =>
            (result?.OccupancyConflicts != null && result.OccupancyConflicts.Count > 0)
            || (result?.SubTaskTimelineIssues != null && result.SubTaskTimelineIssues.Count > 0);

        private static void AppendOccupancyRows(
            StringBuilder sb,
            WarehouseSimScenario scenario,
            StackerWarehouseSimulator simulator)
        {
            var snapshot = simulator.ExportOccupancySnapshot();
            if (snapshot == null || snapshot.Length == 0)
            {
                SimReportHtml.AppendKvRow(sb, "结束占用货位", "—");
                return;
            }

            var allocator = simulator.SlotAllocator;
            SimReportHtml.AppendKvRow(sb, "货位布局", allocator?.SlotLayoutDescription ?? "—");

            if (allocator == null)
            {
                SimReportHtml.AppendKvRow(sb, "可存储货位", "—");
                SimReportHtml.AppendKvRow(sb, "结束占用货位", "—");
                return;
            }

            var physical = allocator.PhysicalSlotCount > 0 ? allocator.PhysicalSlotCount : snapshot.Length;
            var storage = allocator.StorageSlotCount;
            var excluded = physical - storage;
            var occupiedStorage = allocator.CountOccupiedStorageSlots(snapshot);
            var occupancyPct = storage > 0 ? occupiedStorage * 100.0 / storage : 0;
            SimReportHtml.AppendKvRow(
                sb,
                "可存储货位",
                $"{storage}（物理格 {physical}，排除区 {excluded} 格）");
            SimReportHtml.AppendKvRow(
                sb,
                "结束占用货位",
                $"{occupiedStorage} / {storage}（{occupancyPct:F1}%）");
        }

        private static void AppendStatsCards(StringBuilder sb, SimRunResult result)
        {
            sb.AppendLine("<div class=\"grid-2\">");
            AppendStatCard(sb, "单箱耗时（秒）", new[]
            {
                ("最小", result.DurationMinSeconds),
                ("均值", result.DurationMeanSeconds),
                ("P50", result.DurationP50Seconds),
                ("P95", result.DurationP95Seconds),
                ("最大", result.DurationMaxSeconds),
            });
            AppendStatCard(sb, "等待时间（秒）", new[]
            {
                ("均值", result.WaitTimeMeanSeconds),
                ("最大", result.WaitTimeMaxSeconds),
            });
            sb.AppendLine("</div>");
        }

        private static void AppendStatCard(StringBuilder sb, string title, (string Label, double Value)[] rows)
        {
            sb.AppendLine("<details class=\"section-fold card\" open>");
            sb.Append("<summary><span class=\"fold-title\">");
            sb.Append(SimReportHtml.Escape(title));
            sb.AppendLine("</span></summary>");
            sb.AppendLine("<div class=\"fold-body\">");
            SimReportHtml.AppendTableStart(sb);
            SimReportHtml.AppendTableHead(sb, "指标", "数值");
            sb.AppendLine("<tbody>");
            for (var i = 0; i < rows.Length; i++)
            {
                SimReportHtml.AppendKvRow(sb, rows[i].Label, rows[i].Value.ToString("F2"), numeric: true);
            }

            sb.AppendLine("</tbody>");
            SimReportHtml.AppendTableEnd(sb);
            sb.AppendLine("</div></details>");
        }

        private static void AppendUtilizationSummaryRows(
            StringBuilder sb,
            IReadOnlyList<SimResourceUtilizationStat> resourceUtilizations)
        {
            if (resourceUtilizations == null || resourceUtilizations.Count == 0)
            {
                return;
            }

            if (SimResourceUtilizationBuilder.TryGetAverageUtilizationPercent(
                    resourceUtilizations,
                    SimResourceUtilizationKind.Stacker,
                    out var stackerAvg))
            {
                SimReportHtml.AppendKvRow(sb, "堆垛机平均利用率", $"{stackerAvg:F1}%");
            }

            if (SimResourceUtilizationBuilder.TryGetAverageUtilizationPercent(
                    resourceUtilizations,
                    SimResourceUtilizationKind.InfeedPort,
                    out var infeedAvg))
            {
                SimReportHtml.AppendKvRow(sb, "入库口平均利用率", $"{infeedAvg:F1}%");
            }

            if (SimResourceUtilizationBuilder.TryGetAverageUtilizationPercent(
                    resourceUtilizations,
                    SimResourceUtilizationKind.OutfeedPort,
                    out var outfeedAvg))
            {
                SimReportHtml.AppendKvRow(sb, "出库口平均利用率", $"{outfeedAvg:F1}%");
            }
        }

        private static void AppendResourceUtilizationSection(
            StringBuilder sb,
            IReadOnlyList<SimResourceUtilizationStat> resourceUtilizations)
        {
            if (resourceUtilizations == null || resourceUtilizations.Count == 0)
            {
                return;
            }

            sb.AppendLine("<details id=\"utilization\" class=\"section-fold card\" open>");
            sb.AppendLine("<summary><span class=\"fold-title\">设备利用率</span></summary>");
            sb.AppendLine("<div class=\"fold-body\">");
            sb.AppendLine(
                "<p class=\"hint\">利用率 = 设备忙碌时长 ÷ 总仿真时长。堆垛机忙碌含驶向、取货、移动、放货；入库口/出库口分别为放货服务与发运服务。</p>");
            SimReportHtml.AppendTableStart(sb);
            SimReportHtml.AppendTableHead(sb, "设备类型", "资源", "忙碌时长（秒）", "仿真时长（秒）", "利用率");
            sb.AppendLine("<tbody>");

            var stats = resourceUtilizations;
            for (var i = 0; i < stats.Count; i++)
            {
                var stat = stats[i];
                sb.AppendLine("<tr>");
                AppendTd(sb, SimResourceUtilizationBuilder.FormatKindLabel(stat.Kind));
                AppendTd(sb, string.IsNullOrEmpty(stat.Label) ? "—" : stat.Label);
                AppendTd(sb, stat.BusySeconds.ToString("F2"), numeric: true);
                AppendTd(sb, stat.TotalSeconds.ToString("F2"), numeric: true);
                AppendTd(sb, $"{stat.UtilizationPercent:F1}%", numeric: true);
                sb.AppendLine("</tr>");
            }

            sb.AppendLine("</tbody>");
            SimReportHtml.AppendTableEnd(sb);
            sb.AppendLine("</div></details>");
        }

        private static void AppendResourceWaitSection(StringBuilder sb, SimRunResult result)
        {
            if (result.ResourceWaitTotals == null || result.ResourceWaitTotals.Count == 0)
            {
                return;
            }

            sb.AppendLine("<details class=\"section-fold card\" open>");
            sb.AppendLine("<summary><span class=\"fold-title\">资源等待汇总</span></summary>");
            sb.AppendLine("<div class=\"fold-body\">");
            SimReportHtml.AppendTableStart(sb);
            SimReportHtml.AppendTableHead(sb, "资源", "累计等待（秒）");
            sb.AppendLine("<tbody>");

            var keys = new List<string>(result.ResourceWaitTotals.Keys);
            keys.Sort(StringComparer.Ordinal);
            for (var i = 0; i < keys.Count; i++)
            {
                var key = keys[i];
                SimReportHtml.AppendKvRow(sb, key, result.ResourceWaitTotals[key].ToString("F2"), numeric: true);
            }

            sb.AppendLine("</tbody>");
            SimReportHtml.AppendTableEnd(sb);
            sb.AppendLine("</div></details>");
        }

        private static void AppendCompletionSection(
            StringBuilder sb,
            SimRunResult result,
            WarehouseConveyorMap map,
            ConveyorMapTopology topology)
        {
            if (result.Completions == null || result.Completions.Count == 0)
            {
                return;
            }

            var sorted = new List<JobCompletionRecord>(result.Completions);
            sorted.Sort((a, b) => a.JobId.CompareTo(b.JobId));

            sb.AppendLine("<details id=\"completions\" class=\"section-fold card\">");
            sb.Append("<summary><span class=\"fold-title\">单箱完成明细</span>");
            sb.Append("<span class=\"fold-meta\">");
            sb.Append(sorted.Count);
            sb.AppendLine(" 条记录</span></summary>");
            sb.AppendLine("<div class=\"fold-body\">");
            SimReportHtml.AppendTableStart(sb);
            SimReportHtml.AppendTableHead(
                sb,
                "任务", "流向", "完成(s)", "总耗时(s)", "服务(s)", "等待(s)",
                "瓶颈", "货位", "堆垛机", "入/出库口", "堆垛机交互点");
            sb.AppendLine("<tbody>");

            for (var i = 0; i < sorted.Count; i++)
            {
                var c = sorted[i];
                sb.AppendLine("<tr>");
                AppendTd(sb, c.JobId.ToString(), numeric: true);
                AppendTd(sb, c.Direction == SimFlowDirection.Outbound ? "出库" : "入库");
                AppendTd(sb, c.CompletedAt.ToString("F2"), numeric: true);
                AppendTd(sb, c.Duration.ToString("F2"), numeric: true);
                AppendTd(sb, c.ServiceTime.ToString("F2"), numeric: true);
                AppendTd(sb, c.WaitTime.ToString("F2"), numeric: true);
                AppendTd(sb, string.IsNullOrEmpty(c.BottleneckResource) ? "—" : c.BottleneckResource);
                AppendTd(sb, c.Slot.ToString());
                AppendTd(sb, SimReportFormatting.FormatStacker(c.StackerId), numeric: true);
                AppendTd(
                    sb,
                    c.Direction == SimFlowDirection.Outbound
                        ? SimReportFormatting.FormatOutfeedPort(map, topology, c.OutfeedPortIndex)
                        : SimReportFormatting.FormatInfeedPort(map, topology, c.InfeedPortIndex),
                    numeric: true);
                AppendTd(sb, SimReportFormatting.FormatPickupPoint(map, c.PickupPointIndex), numeric: true);
                sb.AppendLine("</tr>");
            }

            sb.AppendLine("</tbody>");
            SimReportHtml.AppendTableEnd(sb);
            sb.AppendLine("</div></details>");
        }

        private static void AppendJobSection(
            StringBuilder sb,
            IReadOnlyList<SimSubTask> subTasks,
            double endSimTime,
            WarehouseConveyorMap map,
            ConveyorMapTopology topology)
        {
            sb.AppendLine("<details id=\"jobs\" class=\"section-fold card\" open>");
            sb.AppendLine("<summary><span class=\"fold-title\">任务与子任务明细</span></summary>");
            sb.AppendLine("<div class=\"fold-body\">");
            sb.AppendLine("<p class=\"hint\">点击任务行展开子任务时间轴；可用顶部搜索框按编号或货位筛选。</p>");

            if (subTasks == null || subTasks.Count == 0)
            {
                sb.AppendLine("<p><em>无子任务记录</em></p>");
                sb.AppendLine("</section>");
                return;
            }

            var jobIds = CollectJobIds(subTasks);
            jobIds.Sort();

            for (var i = 0; i < jobIds.Count; i++)
            {
                var jobId = jobIds[i];
                if (!SimCargoJobValidationReportBuilder.TryBuild(
                        subTasks,
                        jobId,
                        endSimTime + 1e-6,
                        s_scratchReport,
                        map,
                        topology))
                {
                    sb.Append("<details class=\"job\" data-search=\"");
                    sb.Append(SimReportHtml.Escape($"任务 {jobId}"));
                    sb.AppendLine("\">");
                    sb.Append("<summary>任务 ");
                    sb.Append(jobId);
                    sb.AppendLine(" —无法构建报告</summary></details>");
                    continue;
                }

                SimCargoJobHtmlReportBuilder.AppendJobSection(sb, s_scratchReport, map, topology);
            }

            sb.AppendLine("</div></details>");
        }

        private static List<int> CollectJobIds(IReadOnlyList<SimSubTask> subTasks)
        {
            var ids = new HashSet<int>();
            for (var i = 0; i < subTasks.Count; i++)
            {
                ids.Add(subTasks[i].JobId);
            }

            return new List<int>(ids);
        }

        private static void AppendTd(StringBuilder sb, string text, bool numeric = false)
        {
            sb.Append("<td");
            if (numeric)
            {
                sb.Append(" class=\"num\"");
            }

            sb.Append(">");
            sb.Append(SimReportHtml.Escape(text));
            sb.Append("</td>");
        }
    }
}
