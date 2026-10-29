using System;
using System.Globalization;
using System.IO;
using System.Text;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <summary>
    /// 将 <see cref="SimCargoJobValidationReport"/> 渲染为紧凑 JSON（gzip）+ 前端按需展开的任务章节。
    /// </summary>
    internal static class SimCargoJobHtmlReportBuilder
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>旧接口：单任务完整 HTML（保留给兼容调用；主报告已改走紧凑 JSON）。</summary>
        public static void AppendJobSection(
            StringBuilder sb,
            SimCargoJobValidationReport report,
            WarehouseConveyorMap map,
            ConveyorMapTopology topology)
        {
            var json = new StringBuilder(2048);
            AppendJobJsonObject(json, report, map, topology);
            AppendJobsShell(sb, writer =>
            {
                writer.Write('[');
                writer.Write(json.ToString());
                writer.Write(']');
            });
        }

        public static void AppendJobsShell(StringBuilder sb, string jobsJsonArray)
        {
            AppendJobsShell(sb, writer => writer.Write(jobsJsonArray ?? "[]"));
        }

        public static void AppendJobsShell(StringBuilder sb, Action<TextWriter> writeJobsJson)
        {
            sb.AppendLine("<details id=\"jobs\" class=\"section-fold card\" open>");
            sb.AppendLine("<summary><span class=\"fold-title\">任务与子任务明细</span></summary>");
            sb.AppendLine("<div class=\"fold-body\">");
            sb.AppendLine("<p class=\"hint\">点击任务行展开子任务时间轴；可用顶部搜索框按编号或货位筛选。明细以 gzip 压缩 JSON 按需渲染。</p>");
            sb.AppendLine("<div id=\"jobs-root\"><p class=\"meta\">正在解压任务数据…</p></div>");
            sb.AppendLine("<noscript><p><em>请启用 JavaScript 以查看任务明细。</em></p></noscript>");
            SimReportGzipHtml.AppendGzipJsonScript(sb, "jobs-data", writeJobsJson);
            sb.AppendLine("<script>");
            sb.AppendLine(SimReportGzipHtml.GunzipScript);
            sb.AppendLine(JobsRenderScript);
            sb.AppendLine("</script>");
            sb.AppendLine("</div></details>");
        }

        public static void AppendJobJsonObject(
            StringBuilder json,
            SimCargoJobValidationReport report,
            WarehouseConveyorMap map,
            ConveyorMapTopology topology)
        {
            var port = report.IsOutbound
                ? SimReportFormatting.FormatOutfeedPort(map, topology, report.OutfeedPortIndex)
                : SimReportFormatting.FormatInfeedPort(map, topology, report.InfeedPortIndex);
            var pickup = SimReportFormatting.FormatPickupPoint(map, report.PickupPointIndex);
            var stacker = SimReportFormatting.FormatStacker(report.StackerId);
            var slot = report.TargetSlot.ToString();

            json.Append('{');
            AppendNum(json, "i", report.JobId);
            json.Append(',');
            AppendNum(json, "o", report.IsOutbound ? 1 : 0);
            json.Append(',');
            AppendStr(json, "s", slot);
            json.Append(',');
            AppendStr(json, "p", port);
            json.Append(',');
            AppendStr(json, "k", pickup);
            json.Append(',');
            AppendStr(json, "r", stacker);
            json.Append(',');
            AppendNum(json, "a", report.JobStartSimTime);
            json.Append(',');
            AppendNum(json, "b", report.JobEndSimTime);
            json.Append(',');
            AppendNum(json, "d", report.JobDurationSeconds);
            json.Append(',');
            AppendNum(json, "n", report.PathNodeCount);
            if (!string.IsNullOrEmpty(report.PathNodeSummary))
            {
                json.Append(',');
                AppendStr(json, "q", report.PathNodeSummary);
            }

            json.Append(",\"g\":[");
            var segs = report.SegmentSchedule;
            if (segs != null)
            {
                for (var i = 0; i < segs.Length; i++)
                {
                    if (i > 0)
                    {
                        json.Append(',');
                    }

                    var seg = segs[i];
                    json.Append('[');
                    AppendRawString(json, SimEntityNaming.FormatSegment(map, seg.FromNodeIndex, seg.ToNodeIndex));
                    json.Append(',');
                    json.Append(seg.SlotIndex.ToString(Inv));
                    json.Append(',');
                    json.Append(seg.EntrySimTime.ToString("0.00", Inv));
                    json.Append(',');
                    json.Append(seg.ExitSimTime.ToString("0.00", Inv));
                    json.Append(',');
                    json.Append(seg.OccupancyEndSimTime.ToString("0.00", Inv));
                    json.Append(']');
                }
            }

            json.Append("],\"u\":[");
            var subs = report.SubTasks;
            if (subs != null)
            {
                for (var i = 0; i < subs.Count; i++)
                {
                    if (i > 0)
                    {
                        json.Append(',');
                    }

                    var e = subs[i];
                    json.Append('[');
                    json.Append(e.Sequence.ToString(Inv));
                    json.Append(',');
                    json.Append(e.SubTaskId.ToString(Inv));
                    json.Append(',');
                    AppendRawString(json, e.KindLabel ?? string.Empty);
                    json.Append(',');
                    json.Append(e.StartSimTime.ToString("0.00", Inv));
                    json.Append(',');
                    json.Append(e.EndSimTime.ToString("0.00", Inv));
                    json.Append(',');
                    json.Append(e.DurationSeconds.ToString("0.00", Inv));
                    json.Append(',');
                    AppendRawString(json, e.Detail ?? string.Empty);
                    json.Append(']');
                }
            }

            json.Append("]}");
        }

        private static void AppendNum(StringBuilder json, string key, int value)
        {
            json.Append('"');
            json.Append(key);
            json.Append("\":");
            json.Append(value.ToString(Inv));
        }

        private static void AppendNum(StringBuilder json, string key, double value)
        {
            json.Append('"');
            json.Append(key);
            json.Append("\":");
            json.Append(value.ToString("0.00", Inv));
        }

        private static void AppendStr(StringBuilder json, string key, string value)
        {
            json.Append('"');
            json.Append(key);
            json.Append("\":");
            AppendRawString(json, value ?? string.Empty);
        }

        private static void AppendRawString(StringBuilder json, string value)
        {
            json.Append('"');
            if (!string.IsNullOrEmpty(value))
            {
                for (var i = 0; i < value.Length; i++)
                {
                    var c = value[i];
                    switch (c)
                    {
                        case '"':
                            json.Append("\\\"");
                            break;
                        case '\\':
                            json.Append("\\\\");
                            break;
                        case '\n':
                            json.Append("\\n");
                            break;
                        case '\r':
                            json.Append("\\r");
                            break;
                        case '\t':
                            json.Append("\\t");
                            break;
                        default:
                            if (c < 0x20)
                            {
                                json.AppendFormat(Inv, "\\u{0:x4}", (int)c);
                            }
                            else
                            {
                                json.Append(c);
                            }

                            break;
                    }
                }
            }

            json.Append('"');
        }

        // 异步解压后渲染任务摘要；首次展开时再生成表格 DOM。
        private const string JobsRenderScript = @"
(async function(){
  var dataEl=document.getElementById('jobs-data');
  var root=document.getElementById('jobs-root');
  if(!dataEl||!root)return;
  var jobs;
  try{
    jobs=await nkLoadEmbeddedJson(dataEl);
  }catch(e){
    root.innerHTML='<p><em>任务数据解析失败：'+String(e&&e.message?e.message:e)+'</em></p>';
    return;
  }
  if(!jobs||!jobs.length){root.innerHTML='<p><em>无子任务记录</em></p>';return;}
  function esc(s){return String(s==null?'':s).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/""/g,'&quot;');}
  function dash(s){return (s==null||s==='')?'—':esc(s);}
  function kvTable(rows){
    var h='<div class=""table-wrap""><table><thead><tr><th>字段</th><th>值</th></tr></thead><tbody class=""kv"">';
    for(var i=0;i<rows.length;i++){h+='<tr><th>'+esc(rows[i][0])+'</th><td>'+rows[i][1]+'</td></tr>';}
    return h+'</tbody></table></div>';
  }
  function gridTable(headers,rows){
    var h='<div class=""table-wrap""><table><thead><tr>';
    for(var i=0;i<headers.length;i++)h+='<th>'+esc(headers[i])+'</th>';
    h+='</tr></thead><tbody>';
    for(var r=0;r<rows.length;r++){
      h+='<tr>';
      var row=rows[r];
      for(var c=0;c<row.length;c++){
        var cell=row[c], num=typeof cell==='number';
        h+='<td'+(num?' class=""num""':'')+'>'+(num?cell:dash(cell))+'</td>';
      }
      h+='</tr>';
    }
    return h+'</tbody></table></div>';
  }
  function fillBody(el,j){
    if(el.getAttribute('data-filled'))return;
    var portLabel=j.o?('出库口'):('入库口');
    var slotLabel=j.o?('源货位'):('目标货位');
    var rows=[[slotLabel,esc(j.s)],[portLabel,esc(j.p)],['堆垛机交互点',esc(j.k)],['堆垛机',esc(j.r)],
      ['任务时间',esc(j.a)+' s → '+esc(j.b)+' s（'+esc(j.d)+' s）']];
    if(j.n>0){rows.push(['输送路径',esc(j.n)+' 节点']); if(j.q)rows.push(['路径节点',esc(j.q)]);} else if(j.q){rows.push(['说明',esc(j.q)]);}
    if(j.g&&j.g.length)rows.push(['输送路段数',String(j.g.length)]);
    var html=kvTable(rows);
    if(j.g&&j.g.length){
      html+='<h4>输送路段预约</h4>';
      var segRows=[];
      for(var i=0;i<j.g.length;i++){var g=j.g[i];segRows.push([i+1,g[0],g[1],g[2],g[3],g[4]]);}
      html+=gridTable(['#','路段','槽位','进入(s)','离开(s)','占用止(s)'],segRows);
    }
    if(j.u&&j.u.length){
      html+='<h4>子任务时间轴</h4>';
      var subRows=[];
      for(var i=0;i<j.u.length;i++){var u=j.u[i];subRows.push([u[0],u[1],u[2],u[3],u[4],u[5],u[6]]);}
      html+=gridTable(['#','子任务ID','类型','开始(s)','结束(s)','时长(s)','说明'],subRows);
    }
    el.innerHTML=html;
    el.setAttribute('data-filled','1');
  }
  root.innerHTML='';
  var frag=document.createDocumentFragment();
  for(var i=0;i<jobs.length;i++){
    var j=jobs[i];
    var search='任务 '+j.i+' '+ (j.s||'')+' 堆垛机'+(j.r||'')+' '+(j.o?'出库口':'入库口')+(j.p||'');
    var d=document.createElement('details');
    d.className='job';
    d.setAttribute('data-search',search);
    var sum=document.createElement('summary');
    sum.textContent='任务 '+j.i+' · '+(j.s||'')+' · '+(j.d!=null?j.d:'')+' s';
    var body=document.createElement('div');
    body.className='job-body';
    d.appendChild(sum);
    d.appendChild(body);
    d.addEventListener('toggle',function(job,bodyEl){return function(){if(this.open)fillBody(bodyEl,job);};}(j,body));
    frag.appendChild(d);
  }
  root.appendChild(frag);
})();
";
    }
}
