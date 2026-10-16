using System;
using System.Collections.Generic;
using System.Text;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <summary>
    /// 回放间距自检：按采样步长遍历各货物的输送时间轴（与回放同一套 zone 位置），
    /// 任意两件货物中心沿输送线的距离小于单箱长度就记一次（同一对货物连续违规合并为一条）。
    /// 覆盖：入库口 / 交互点起点、路段停留点、路口 / 加工站 / 取货点 / 出库口，以及停留点之间的行驶过程。
    /// </summary>
    public static class SimCargoSpacingChecker
    {
        public const double DefaultSampleSeconds = 0.1;
        private const double ToleranceMeters = 1e-3;

        private struct Piece
        {
            public double T0;
            public double T1;
            public ConveyorTrackPoint P;
            public ConveyorTrackPoint Q;
            public int EdgeFrom;
            public int EdgeTo;
            public bool Moving;
            public string Label;
        }

        private sealed class Track
        {
            public int JobId;
            public double Start;
            public double End;
            public List<Piece> Pieces = new List<Piece>();
            public int Cursor;
        }

        public static List<SimCargoSpacingViolationRecord> FindViolations(
            IEnumerable<WarehouseJob> jobs,
            WarehouseConveyorMap map,
            ConveyorMapTopology topology,
            double sampleSeconds = DefaultSampleSeconds)
        {
            var result = new List<SimCargoSpacingViolationRecord>();
            if (jobs == null || map == null || topology == null)
            {
                return result;
            }

            var geo = new ConveyorTrackGeometry(map, topology);
            var tracks = new List<Track>();
            foreach (var job in jobs)
            {
                var track = BuildTrack(job, geo, map);
                if (track != null && track.Pieces.Count > 0)
                {
                    tracks.Add(track);
                }
            }

            if (tracks.Count < 2)
            {
                return result;
            }

            tracks.Sort((a, b) => a.Start.CompareTo(b.Start));
            var tMin = double.PositiveInfinity;
            var tMax = double.NegativeInfinity;
            foreach (var t in tracks)
            {
                tMin = Math.Min(tMin, t.Start);
                tMax = Math.Max(tMax, t.End);
            }

            var dt = Math.Max(0.01, sampleSeconds);
            var limit = geo.CargoLength - ToleranceMeters;
            var open = new Dictionary<long, SimCargoSpacingViolationRecord>();
            var lastSeen = new Dictionary<long, double>();
            var activeIdx = new List<int>();
            var pos = new List<(int Job, ConveyorTrackPoint Point, string Label)>();
            var startCursor = 0;
            var steps = (long)Math.Ceiling((tMax - tMin) / dt);
            for (long s = 0; s <= steps; s++)
            {
                var time = tMin + s * dt;
                while (startCursor < tracks.Count && tracks[startCursor].Start <= time)
                {
                    activeIdx.Add(startCursor++);
                }

                pos.Clear();
                for (var i = activeIdx.Count - 1; i >= 0; i--)
                {
                    var tr = tracks[activeIdx[i]];
                    if (tr.End < time)
                    {
                        activeIdx.RemoveAt(i);
                        continue;
                    }

                    if (TrySample(tr, time, geo, out var p, out var label))
                    {
                        pos.Add((tr.JobId, p, label));
                    }
                }

                for (var i = 0; i < pos.Count; i++)
                {
                    for (var j = i + 1; j < pos.Count; j++)
                    {
                        var d = geo.Distance(pos[i].Point, pos[j].Point);
                        if (d >= limit)
                        {
                            continue;
                        }

                        var a = Math.Min(pos[i].Job, pos[j].Job);
                        var b = Math.Max(pos[i].Job, pos[j].Job);
                        var key = ((long)a << 32) | (uint)b;
                        if (open.TryGetValue(key, out var rec) && lastSeen[key] >= time - dt * 1.5)
                        {
                            rec.EndTime = time;
                            if (d < rec.MinDistanceMeters)
                            {
                                rec.MinDistanceMeters = d;
                                rec.LocationA = a == pos[i].Job ? pos[i].Label : pos[j].Label;
                                rec.LocationB = a == pos[i].Job ? pos[j].Label : pos[i].Label;
                            }
                        }
                        else
                        {
                            rec = new SimCargoSpacingViolationRecord
                            {
                                JobA = a,
                                JobB = b,
                                StartTime = time,
                                EndTime = time,
                                MinDistanceMeters = d,
                                CargoLengthMeters = geo.CargoLength,
                                LocationA = a == pos[i].Job ? pos[i].Label : pos[j].Label,
                                LocationB = a == pos[i].Job ? pos[j].Label : pos[i].Label,
                            };
                            open[key] = rec;
                            result.Add(rec);
                        }

                        lastSeen[key] = time;
                    }
                }
            }

            result.Sort((x, y) => x.StartTime.CompareTo(y.StartTime));
            return result;
        }

        private static Track BuildTrack(WarehouseJob job, ConveyorTrackGeometry geo, WarehouseConveyorMap map)
        {
            var chain = job?.ConveyorPathZones;
            if (chain == null || chain.Count == 0 || double.IsNaN(job.ConveyorStartMoveSimTime) || job.ConveyorStartMoveSimTime <= 0 && chain[0].ArriveSimTime <= 0)
            {
                return null;
            }

            var n = chain.Count;
            var pts = new ConveyorTrackPoint[n];
            for (var k = 0; k < n; k++)
            {
                if (!geo.TryGetZonePoint(chain[k], out pts[k]))
                {
                    return null;
                }
            }

            var track = new Track { JobId = job.JobId };
            var originNode = chain[0].FromNodeIndex;
            var origin = ConveyorTrackGeometry.Node(originNode);
            var originReady = job.Direction == SimFlowDirection.Outbound ? job.PickupCompleteSimTime : job.InfeedCompleteSimTime;
            var ms0 = job.ConveyorStartMoveSimTime;
            var originLabel = $"J{job.JobId} 起点 {geo.Describe(origin)}";
            if (ms0 > originReady)
            {
                Add(track, originReady, ms0, origin, origin, -1, -1, false, originLabel);
            }

            Add(track, ms0, chain[0].ArriveSimTime, origin, pts[0], chain[0].FromNodeIndex, chain[0].ToNodeIndex, true, originLabel);
            for (var k = 0; k < n; k++)
            {
                var z = chain[k];
                var label = $"J{job.JobId} {z.Kind}{(z.SlotIndex >= 0 ? "-S" + z.SlotIndex : string.Empty)} {geo.Describe(pts[k])}";
                if (k > 0)
                {
                    Add(track, chain[k - 1].LeaveSimTime, z.ArriveSimTime, pts[k - 1], pts[k], z.FromNodeIndex, z.ToNodeIndex, true, label);
                }

                var leave = k + 1 < n ? z.LeaveSimTime : Math.Max(z.ArriveSimTime, job.ConveyorGoalReleaseSimTime);
                Add(track, z.ArriveSimTime, leave, pts[k], pts[k], -1, -1, false, label);
            }

            track.Start = track.Pieces[0].T0;
            track.End = track.Pieces[track.Pieces.Count - 1].T1;
            return track;
        }

        private static void Add(Track t, double t0, double t1, ConveyorTrackPoint p, ConveyorTrackPoint q, int from, int to, bool moving, string label)
        {
            if (double.IsNaN(t0) || double.IsNaN(t1) || t1 < t0)
            {
                return;
            }

            t.Pieces.Add(new Piece { T0 = t0, T1 = t1, P = p, Q = q, EdgeFrom = from, EdgeTo = to, Moving = moving, Label = label });
        }

        private static bool TrySample(Track tr, double time, ConveyorTrackGeometry geo, out ConveyorTrackPoint point, out string label)
        {
            point = default;
            label = null;
            var pieces = tr.Pieces;
            while (tr.Cursor < pieces.Count - 1 && pieces[tr.Cursor].T1 < time)
            {
                tr.Cursor++;
            }

            var pc = pieces[tr.Cursor];
            if (time < pc.T0 || time > pc.T1)
            {
                return false;
            }

            label = pc.Label;
            if (!pc.Moving || pc.T1 <= pc.T0)
            {
                point = pc.Moving ? pc.Q : pc.P;
                return true;
            }

            point = geo.Interpolate(pc.P, pc.Q, pc.EdgeFrom, pc.EdgeTo, (time - pc.T0) / (pc.T1 - pc.T0));
            return true;
        }

        public static void AppendSection(StringBuilder sb, SimRunResult result)
        {
            SimReportMarkdown.AppendHeading(sb, 2, "货物间距自检");
            var list = result?.CargoSpacingViolations;
            if (list == null || list.Count == 0)
            {
                sb.AppendLine("通过：任意时刻两件货物中心沿输送线的距离都不小于单箱长度（含节点上、节点前停留点和刚离开节点的货）。");
                sb.AppendLine();
                return;
            }

            SimReportMarkdown.AppendKvTable(sb, new List<(string Label, string Value)>
            {
                ("结论", "失败"),
                ("违规段数", list.Count.ToString()),
                ("单箱长度", $"{list[0].CargoLengthMeters:0.###} m"),
            });
            sb.AppendLine("按 0.1 s 采样回放时间轴；同一对货物连续违规合并为一条。");
            sb.AppendLine();
            sb.AppendLine("| # | 货物 A | 货物 B | 开始 (s) | 结束 (s) | 最小中心距 (m) | A 位置 | B 位置 |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|");
            var show = Math.Min(list.Count, 200);
            for (var i = 0; i < show; i++)
            {
                var r = list[i];
                sb.AppendLine($"| {i + 1} | {r.JobA} | {r.JobB} | {r.StartTime:F2} | {r.EndTime:F2} | {r.MinDistanceMeters:F3} | {r.LocationA} | {r.LocationB} |");
            }

            if (list.Count > show)
            {
                sb.AppendLine($"| … | 另有 {list.Count - show} 条未列出 | | | | | | |");
            }

            sb.AppendLine();
        }

        public static void AppendHtmlSummary(StringBuilder sb, SimRunResult result)
        {
            var list = result?.CargoSpacingViolations;
            if (list == null || list.Count == 0)
            {
                SimReportHtml.AppendKvRow(sb, "货物间距校验", "通过");
                return;
            }

            SimReportHtml.AppendKvRow(sb, "货物间距校验", $"失败 — {list.Count} 段（中心距 < 单箱长度 {list[0].CargoLengthMeters:0.##} m）");
            for (var i = 0; i < Math.Min(list.Count, 10); i++)
            {
                var r = list[i];
                SimReportHtml.AppendKvRow(sb, $"  间距违规 {i + 1}", $"J{r.JobA}/J{r.JobB} {r.StartTime:F1}–{r.EndTime:F1}s 最小 {r.MinDistanceMeters:F2} m：{r.LocationA} ↔ {r.LocationB}");
            }
        }
    }
}
