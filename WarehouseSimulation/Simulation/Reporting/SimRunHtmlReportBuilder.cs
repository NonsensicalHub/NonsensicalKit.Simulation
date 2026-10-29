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
            WarehouseSimEngine simulator,
            SimRunResult result,
            string debugMarkdownFileName = null,
            SimRunExportDisplayInfo displayInfo = default,
            SimReplayHtmlCodec.Payload replayPayload = null)
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
            if (replayPayload != null)
            {
                title += "（含回放）";
            }

            var map = scenario?.ResolvedHardwareBindings?.ConveyorMap;
            ConveyorMapTopology.TryBuild(map, out var topology, out _);
            var resourceUtilizations = ResolveResourceUtilizations(result, subTasks, endSimTime, map, topology);

            var sb = new StringBuilder(128 * 1024);
            SimReportHtml.AppendDocumentStart(sb, title);
            AppendToolbar(sb);
            AppendPageHeader(sb, scenario, result, displayInfo, replayPayload != null);
            AppendSummarySection(sb, scenario, result, simulator, debugMarkdownFileName, resourceUtilizations);
            SimReportTopologyHtmlBuilder.AppendSection(sb, map, displayInfo.ConveyorMapName);
            AppendStatsCards(sb, result);
            AppendResourceUtilizationSection(sb, resourceUtilizations);
            AppendResourceWaitSection(sb, result);
            AppendCompletionSection(sb, result, map, topology);
            AppendJobSection(sb, subTasks, endSimTime, map, topology);
            if (replayPayload != null)
            {
                SimReplayHtmlCodec.AppendEmbeddedReplay(sb, replayPayload);
            }

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
            sb.AppendLine("<a href=\"#replay-embed\">可回放数据</a>");
            sb.AppendLine("</div>");
        }

        private static void AppendPageHeader(
            StringBuilder sb,
            WarehouseSimScenario scenario,
            SimRunResult result,
            SimRunExportDisplayInfo displayInfo,
            bool includesReplay = false)
        {
            sb.AppendLine("<header>");
            var flowLabel = SimFlowPlanResolver.FormatFlowKindLabel(SimFlowPlanResolver.Resolve(scenario));
            sb.Append("<h1>");
            sb.Append(flowLabel);
            sb.Append("仿真任务报告");
            if (includesReplay)
            {
                sb.Append(" <span class=\"badge badge-ok\">含回放</span>");
            }

            sb.AppendLine("</h1>");
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
            WarehouseSimEngine simulator,
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
            SimSegmentCoOccupancyMarkdownBuilder.AppendHtmlSummary(sb, result);
            SimCargoSpacingChecker.AppendHtmlSummary(sb, result);
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
            || (result?.SegmentCoOccupancies != null && result.SegmentCoOccupancies.Count > 0)
            || (result?.CargoSpacingViolations != null && result.CargoSpacingViolations.Count > 0)
            || (result?.SubTaskTimelineIssues != null && result.SubTaskTimelineIssues.Count > 0);

        private static void AppendOccupancyRows(
            StringBuilder sb,
            WarehouseSimScenario scenario,
            WarehouseSimEngine simulator)
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
            sb.AppendLine("<div id=\"completions-root\"><p class=\"meta\">正在解压完成明细…</p></div>");
            sb.AppendLine("<noscript><p><em>请启用 JavaScript 以查看完成明细。</em></p></noscript>");

            var json = new StringBuilder(Math.Max(2048, sorted.Count * 96));
            json.Append('[');
            for (var i = 0; i < sorted.Count; i++)
            {
                if (i > 0)
                {
                    json.Append(',');
                }

                var c = sorted[i];
                var port = c.Direction == SimFlowDirection.Outbound
                    ? SimReportFormatting.FormatOutfeedPort(map, topology, c.OutfeedPortIndex)
                    : SimReportFormatting.FormatInfeedPort(map, topology, c.InfeedPortIndex);
                var pickup = SimReportFormatting.FormatPickupPoint(map, c.PickupPointIndex);
                var stacker = SimReportFormatting.FormatStacker(c.StackerId);
                var bottleneck = string.IsNullOrEmpty(c.BottleneckResource) ? string.Empty : c.BottleneckResource;

                json.Append('[');
                json.Append(c.JobId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                json.Append(',');
                json.Append(c.Direction == SimFlowDirection.Outbound ? 1 : 0);
                json.Append(',');
                json.Append(c.CompletedAt.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
                json.Append(',');
                json.Append(c.Duration.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
                json.Append(',');
                json.Append(c.ServiceTime.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
                json.Append(',');
                json.Append(c.WaitTime.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
                json.Append(',');
                AppendJsonString(json, bottleneck);
                json.Append(',');
                AppendJsonString(json, c.Slot.ToString());
                json.Append(',');
                AppendJsonString(json, stacker);
                json.Append(',');
                AppendJsonString(json, port);
                json.Append(',');
                AppendJsonString(json, pickup);
                json.Append(']');
            }

            json.Append(']');
            SimReportGzipHtml.AppendGzipJsonScript(sb, "completions-data", json.ToString());
            sb.AppendLine("<script>");
            // Gunzip helper 可能已由任务章节注入；此处再写一次无害且保证单独打开时可用。
            sb.AppendLine(SimReportGzipHtml.GunzipScript);
            sb.AppendLine(CompletionsRenderScript);
            sb.AppendLine("</script>");
            sb.AppendLine("</div></details>");
        }

        private static void AppendJsonString(StringBuilder json, string value)
        {
            json.Append('"');
            if (!string.IsNullOrEmpty(value))
            {
                for (var i = 0; i < value.Length; i++)
                {
                    var ch = value[i];
                    switch (ch)
                    {
                        case '"': json.Append("\\\""); break;
                        case '\\': json.Append("\\\\"); break;
                        case '\n': json.Append("\\n"); break;
                        case '\r': json.Append("\\r"); break;
                        case '\t': json.Append("\\t"); break;
                        default:
                            if (ch < 0x20)
                            {
                                json.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "\\u{0:x4}", (int)ch);
                            }
                            else
                            {
                                json.Append(ch);
                            }

                            break;
                    }
                }
            }

            json.Append('"');
        }

        private const string CompletionsRenderScript = @"
(async function(){
  var dataEl=document.getElementById('completions-data');
  var root=document.getElementById('completions-root');
  if(!dataEl||!root)return;
  var rows;
  try{rows=await nkLoadEmbeddedJson(dataEl);}catch(e){
    root.innerHTML='<p><em>完成明细解析失败：'+String(e&&e.message?e.message:e)+'</em></p>';
    return;
  }
  if(!rows||!rows.length){root.innerHTML='<p><em>无完成记录</em></p>';return;}
  function esc(s){return String(s==null?'':s).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/""/g,'&quot;');}
  function dash(s){return (s==null||s==='')?'—':esc(s);}
  var h='<div class=""table-wrap""><table><thead><tr>'+
    '<th>任务</th><th>流向</th><th>完成(s)</th><th>总耗时(s)</th><th>服务(s)</th><th>等待(s)</th>'+
    '<th>瓶颈</th><th>货位</th><th>堆垛机</th><th>入/出库口</th><th>堆垛机交互点</th>'+
    '</tr></thead><tbody>';
  for(var i=0;i<rows.length;i++){
    var r=rows[i];
    h+='<tr>'+
      '<td class=""num"">'+esc(r[0])+'</td>'+
      '<td>'+(r[1]?('出库'):('入库'))+'</td>'+
      '<td class=""num"">'+esc(r[2])+'</td>'+
      '<td class=""num"">'+esc(r[3])+'</td>'+
      '<td class=""num"">'+esc(r[4])+'</td>'+
      '<td class=""num"">'+esc(r[5])+'</td>'+
      '<td>'+dash(r[6])+'</td>'+
      '<td>'+dash(r[7])+'</td>'+
      '<td class=""num"">'+dash(r[8])+'</td>'+
      '<td class=""num"">'+dash(r[9])+'</td>'+
      '<td class=""num"">'+dash(r[10])+'</td>'+
      '</tr>';
  }
  h+='</tbody></table></div>';
  root.innerHTML=h;
})();
";
        private static void AppendJobSection(
            StringBuilder sb,
            IReadOnlyList<SimSubTask> subTasks,
            double endSimTime,
            WarehouseConveyorMap map,
            ConveyorMapTopology topology)
        {
            if (subTasks == null || subTasks.Count == 0)
            {
                SimCargoJobHtmlReportBuilder.AppendJobsShell(sb, "[]");
                return;
            }

            var jobIds = CollectJobIds(subTasks);
            jobIds.Sort();

            // 预估：每任务约数百字节紧凑 JSON，远小于原 HTML 表格。
            var json = new StringBuilder(Math.Max(4096, jobIds.Count * 512));
            json.Append('[');
            var written = 0;
            for (var i = 0; i < jobIds.Count; i++)
            {
                var jobId = jobIds[i];
                if (written > 0)
                {
                    json.Append(',');
                }

                if (!SimCargoJobValidationReportBuilder.TryBuild(
                        subTasks,
                        jobId,
                        endSimTime + 1e-6,
                        s_scratchReport,
                        map,
                        topology))
                {
                    // 无法构建时仍保留占位条目，便于筛选看到该任务号
                    json.Append("{\"i\":");
                    json.Append(jobId);
                    json.Append(",\"o\":0,\"s\":\"—\",\"p\":\"—\",\"k\":\"—\",\"r\":\"—\",\"a\":0,\"b\":0,\"d\":0,\"n\":0,\"q\":\"无法构建报告\",\"g\":[],\"u\":[]}");
                    written++;
                    continue;
                }

                SimCargoJobHtmlReportBuilder.AppendJobJsonObject(json, s_scratchReport, map, topology);
                written++;
            }

            json.Append(']');
            SimCargoJobHtmlReportBuilder.AppendJobsShell(sb, json.ToString());
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
