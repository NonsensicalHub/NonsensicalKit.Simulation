using System;
using System.Collections.Generic;
using System.Text;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <summary>检测单任务子任务时间轴上的重叠与空白时段。</summary>
    public static class SimSubTaskTimelineChecker
    {
        private const double TimeEpsilon = 1e-6;

        public sealed class CheckResult
        {
            public List<SimSubTaskTimelineIssueRecord> Issues = new();
        }

        public static CheckResult Run(IReadOnlyList<SimSubTask> subTasks)
        {
            var result = new CheckResult();
            if (subTasks == null || subTasks.Count == 0)
            {
                return result;
            }

            var byJob = new Dictionary<int, List<SimSubTask>>();
            for (var i = 0; i < subTasks.Count; i++)
            {
                var task = subTasks[i];
                if (task.Kind == SimSubTaskKind.Completed)
                {
                    continue;
                }

                if (!byJob.TryGetValue(task.JobId, out var list))
                {
                    list = new List<SimSubTask>();
                    byJob[task.JobId] = list;
                }

                list.Add(task);
            }

            foreach (var pair in byJob)
            {
                CheckJob(pair.Key, pair.Value, result.Issues);
            }

            result.Issues.Sort(CompareIssues);
            return result;
        }

        public static void CountIssuesByKind(
            IReadOnlyList<SimSubTaskTimelineIssueRecord> issues,
            out int overlapCount,
            out int gapCount)
        {
            overlapCount = 0;
            gapCount = 0;
            if (issues == null)
            {
                return;
            }

            for (var i = 0; i < issues.Count; i++)
            {
                if (issues[i].Kind == SimSubTaskTimelineIssueKind.Overlap)
                {
                    overlapCount++;
                }
                else
                {
                    gapCount++;
                }
            }
        }

        public static string FormatSummary(IReadOnlyList<SimSubTaskTimelineIssueRecord> issues)
        {
            if (issues == null || issues.Count == 0)
            {
                return string.Empty;
            }

            var sb = new StringBuilder(issues.Count * 96);
            for (var i = 0; i < issues.Count; i++)
            {
                var issue = issues[i];
                sb.Append("  [");
                sb.Append(i + 1);
                sb.Append("] Job ");
                sb.Append(issue.JobId);
                sb.Append(' ');
                sb.Append(FormatKindLabel(issue.Kind));
                sb.Append(' ');
                sb.Append(issue.IssueStart.ToString("F3"));
                sb.Append("~");
                sb.Append(issue.IssueEnd.ToString("F3"));
                sb.Append("s (");
                sb.Append(issue.IssueSeconds.ToString("F3"));
                sb.Append("s)");
                if (issue.Kind == SimSubTaskTimelineIssueKind.Overlap)
                {
                    sb.Append(" #");
                    sb.Append(issue.SubTaskIdA);
                    sb.Append(' ');
                    sb.Append(SimSubTaskQuery.GetKindLabel(issue.KindA));
                    sb.Append(" vs #");
                    sb.Append(issue.SubTaskIdB);
                    sb.Append(' ');
                    sb.Append(SimSubTaskQuery.GetKindLabel(issue.KindB));
                }
                else
                {
                    if (issue.SubTaskIdA >= 0)
                    {
                        sb.Append(" after #");
                        sb.Append(issue.SubTaskIdA);
                        sb.Append(' ');
                        sb.Append(SimSubTaskQuery.GetKindLabel(issue.KindA));
                    }

                    if (issue.SubTaskIdB >= 0)
                    {
                        sb.Append(" before #");
                        sb.Append(issue.SubTaskIdB);
                        sb.Append(' ');
                        sb.Append(SimSubTaskQuery.GetKindLabel(issue.KindB));
                    }
                }

                sb.AppendLine();
            }

            return sb.ToString().TrimEnd();
        }

        public static string FormatKindLabel(SimSubTaskTimelineIssueKind kind) =>
            kind switch
            {
                SimSubTaskTimelineIssueKind.Overlap => "重叠",
                SimSubTaskTimelineIssueKind.Gap => "空白",
                _ => kind.ToString(),
            };

        private static void CheckJob(int jobId, List<SimSubTask> tasks, List<SimSubTaskTimelineIssueRecord> issues)
        {
            if (tasks.Count < 2)
            {
                return;
            }

            tasks.Sort(CompareSubTasks);
            FindOverlaps(jobId, tasks, issues);
            FindGaps(jobId, tasks, issues);
        }

        private static void FindOverlaps(
            int jobId,
            IReadOnlyList<SimSubTask> tasks,
            List<SimSubTaskTimelineIssueRecord> issues)
        {
            for (var i = 0; i < tasks.Count; i++)
            {
                for (var j = i + 1; j < tasks.Count; j++)
                {
                    var a = tasks[i];
                    var b = tasks[j];
                    if (!TryGetOverlap(a.StartSimTime, a.EndSimTime, b.StartSimTime, b.EndSimTime, out var overlapStart, out var overlapEnd))
                    {
                        continue;
                    }

                    issues.Add(new SimSubTaskTimelineIssueRecord
                    {
                        JobId = jobId,
                        Kind = SimSubTaskTimelineIssueKind.Overlap,
                        SubTaskIdA = a.SubTaskId,
                        SubTaskIdB = b.SubTaskId,
                        KindA = a.Kind,
                        KindB = b.Kind,
                        SubTaskAStart = a.StartSimTime,
                        SubTaskAEnd = a.EndSimTime,
                        SubTaskBStart = b.StartSimTime,
                        SubTaskBEnd = b.EndSimTime,
                        IssueStart = overlapStart,
                        IssueEnd = overlapEnd,
                        IssueSeconds = overlapEnd - overlapStart,
                    });
                }
            }
        }

        private static void FindGaps(
            int jobId,
            IReadOnlyList<SimSubTask> tasks,
            List<SimSubTaskTimelineIssueRecord> issues)
        {
            var merged = new List<(double Start, double End, int SubTaskId, SimSubTaskKind Kind)>();
            for (var i = 0; i < tasks.Count; i++)
            {
                var task = tasks[i];
                var start = task.StartSimTime;
                var end = task.EndSimTime;
                if (end - start <= TimeEpsilon)
                {
                    continue;
                }

                MergeInterval(merged, start, end, task.SubTaskId, task.Kind);
            }

            if (merged.Count < 2)
            {
                return;
            }

            for (var i = 0; i < merged.Count - 1; i++)
            {
                var current = merged[i];
                var next = merged[i + 1];
                var gapStart = current.End;
                var gapEnd = next.Start;
                if (gapEnd - gapStart <= TimeEpsilon)
                {
                    continue;
                }

                ResolveGapBoundarySubTasks(
                    tasks,
                    gapStart,
                    gapEnd,
                    out var before,
                    out var after);

                issues.Add(new SimSubTaskTimelineIssueRecord
                {
                    JobId = jobId,
                    Kind = SimSubTaskTimelineIssueKind.Gap,
                    SubTaskIdA = before.SubTaskId,
                    SubTaskIdB = after.SubTaskId,
                    KindA = before.Kind,
                    KindB = after.Kind,
                    SubTaskAStart = before.StartSimTime,
                    SubTaskAEnd = before.EndSimTime,
                    SubTaskBStart = after.StartSimTime,
                    SubTaskBEnd = after.EndSimTime,
                    IssueStart = gapStart,
                    IssueEnd = gapEnd,
                    IssueSeconds = gapEnd - gapStart,
                });
            }
        }

        /// <summary>按时刻找空白段前后紧邻的子任务（不用合并区间上的代表 ID，避免误显示为 #3 与 #6 而忽略中间的 #4、#5）。</summary>
        private static void ResolveGapBoundarySubTasks(
            IReadOnlyList<SimSubTask> tasks,
            double gapStart,
            double gapEnd,
            out SimSubTask before,
            out SimSubTask after)
        {
            before = default;
            after = default;
            before.SubTaskId = -1;
            after.SubTaskId = -1;

            for (var i = 0; i < tasks.Count; i++)
            {
                var task = tasks[i];
                if (task.EndSimTime - task.StartSimTime <= TimeEpsilon)
                {
                    continue;
                }

                if (task.EndSimTime <= gapStart + TimeEpsilon
                    && (before.SubTaskId < 0 || task.EndSimTime > before.EndSimTime + TimeEpsilon))
                {
                    before = task;
                }

                if (task.StartSimTime >= gapEnd - TimeEpsilon
                    && (after.SubTaskId < 0 || task.StartSimTime < after.StartSimTime - TimeEpsilon))
                {
                    after = task;
                }
            }
        }

        private static void MergeInterval(
            List<(double Start, double End, int SubTaskId, SimSubTaskKind Kind)> merged,
            double start,
            double end,
            int subTaskId,
            SimSubTaskKind kind)
        {
            if (merged.Count == 0)
            {
                merged.Add((start, end, subTaskId, kind));
                return;
            }

            var last = merged[^1];
            if (start <= last.End + TimeEpsilon)
            {
                if (end > last.End)
                {
                    merged[^1] = (last.Start, end, subTaskId, kind);
                }

                return;
            }

            merged.Add((start, end, subTaskId, kind));
        }

        private static bool TryGetOverlap(
            double aStart,
            double aEnd,
            double bStart,
            double bEnd,
            out double overlapStart,
            out double overlapEnd)
        {
            overlapStart = Math.Max(aStart, bStart);
            overlapEnd = Math.Min(aEnd, bEnd);
            return overlapEnd - overlapStart > TimeEpsilon;
        }

        private static int CompareSubTasks(SimSubTask a, SimSubTask b)
        {
            var startCompare = a.StartSimTime.CompareTo(b.StartSimTime);
            if (startCompare != 0)
            {
                return startCompare;
            }

            var endCompare = a.EndSimTime.CompareTo(b.EndSimTime);
            return endCompare != 0 ? endCompare : a.SubTaskId.CompareTo(b.SubTaskId);
        }

        private static int CompareIssues(SimSubTaskTimelineIssueRecord a, SimSubTaskTimelineIssueRecord b)
        {
            var job = a.JobId.CompareTo(b.JobId);
            if (job != 0)
            {
                return job;
            }

            var kind = a.Kind.CompareTo(b.Kind);
            return kind != 0 ? kind : a.IssueStart.CompareTo(b.IssueStart);
        }
    }
}
