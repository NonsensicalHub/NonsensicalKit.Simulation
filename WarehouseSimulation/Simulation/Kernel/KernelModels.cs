using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using NonsensicalKit.Simulation.WarehouseSimulation.Simulation.Routing;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation.Kernel
{
    /// <summary>内核事件类型。同刻处理顺序由 <see cref="KernelEventPriority"/> 显式决定，而非枚举值。</summary>
    public enum KernelEventKind
    {
        TailCleared = 0,
        HoldReleased = 1,
        MoveCompleted = 10,
        ProcessCompleted = 11,
        ActionCompleted = 12,
        DispatchRound = 20,
        MoveStarted = 30,
        ProcessStarted = 31,
        ActionStarted = 32,
        JobArrived = 40,
    }

    /// <summary>同刻优先级契约（§8.3）：释放 → 完成 → 派发决策 → 开始/进入 → 新到达。</summary>
    public static class KernelEventPriority
    {
        public const int Release = 0;
        public const int Completion = 1;
        /// <summary>同刻到达先于派发，保证同刻到达的任务一起参与仲裁。</summary>
        public const int Arrival = 2;
        public const int Start = 3;
        /// <summary>派发（进入/提交）最后：同刻所有释放、完成、到达都已生效。</summary>
        public const int Dispatch = 4;

        public static int Of(KernelEventKind kind)
        {
            switch (kind)
            {
                case KernelEventKind.TailCleared:
                case KernelEventKind.HoldReleased:
                    return Release;
                case KernelEventKind.MoveCompleted:
                case KernelEventKind.ProcessCompleted:
                case KernelEventKind.ActionCompleted:
                    return Completion;
                case KernelEventKind.DispatchRound:
                    return Dispatch;
                case KernelEventKind.MoveStarted:
                case KernelEventKind.ProcessStarted:
                case KernelEventKind.ActionStarted:
                    return Start;
                default:
                    return Arrival;
            }
        }
    }

    public sealed class KernelEvent
    {
        public KernelEvent(double time, KernelEventKind kind, long sequence, int jobId, int stepIndex)
        {
            Time = time;
            Kind = kind;
            Priority = KernelEventPriority.Of(kind);
            Sequence = sequence;
            JobId = jobId;
            StepIndex = stepIndex;
        }

        public double Time { get; }
        public KernelEventKind Kind { get; }
        public int Priority { get; }
        /// <summary>全局单调递增的稳定序号（同刻同优先级的最终排序键）。</summary>
        public long Sequence { get; }
        public int JobId { get; }
        /// <summary>路线步骤下标 = RouteStep.OccurrenceIndex；-1 表示入场/非步骤事件。</summary>
        public int StepIndex { get; }
    }

    /// <summary>按 (Time, Priority, Sequence) 全序的事件队列。</summary>
    public sealed class KernelEventQueue
    {
        private sealed class Cmp : IComparer<KernelEvent>
        {
            public int Compare(KernelEvent a, KernelEvent b)
            {
                var c = a.Time.CompareTo(b.Time);
                if (c != 0) return c;
                c = a.Priority.CompareTo(b.Priority);
                if (c != 0) return c;
                return a.Sequence.CompareTo(b.Sequence);
            }
        }

        private readonly SortedSet<KernelEvent> _set = new SortedSet<KernelEvent>(new Cmp());
        private long _sequence;

        public int Count => _set.Count;

        public KernelEvent Schedule(double time, KernelEventKind kind, int jobId = -1, int stepIndex = -1)
        {
            var e = new KernelEvent(time, kind, ++_sequence, jobId, stepIndex);
            _set.Add(e);
            return e;
        }

        public bool TryDequeue(out KernelEvent e)
        {
            if (_set.Count == 0)
            {
                e = null;
                return false;
            }

            e = _set.Min;
            _set.Remove(e);
            return true;
        }
    }

    public enum KernelLogKind
    {
        JobArrived,
        JobEntered,
        ReservationCommitted,
        WaitingForResource,
        MoveStarted,
        MoveCompleted,
        TailCleared,
        HoldReleased,
        ProcessStarted,
        ProcessCompleted,
        ActionStarted,
        ActionCompleted,
        JobCompleted,
        JobFailed,
        Diagnostic,
    }

    /// <summary>不可变日志条目（数组字段在构造时拷贝）。</summary>
    public sealed class KernelLogEntry
    {
        private readonly string[] _resourceIds;
        private readonly long[] _reservationIds;

        public KernelLogEntry(
            int index,
            double time,
            KernelLogKind kind,
            int jobId,
            int occurrenceIndex,
            RouteStepKind? stepKind,
            string fromNodeId,
            string toNodeId,
            IReadOnlyList<string> resourceIds,
            IReadOnlyList<long> reservationIds,
            double windowStart,
            double windowEnd,
            string detail)
        {
            Index = index;
            Time = time;
            Kind = kind;
            JobId = jobId;
            OccurrenceIndex = occurrenceIndex;
            StepKind = stepKind;
            FromNodeId = fromNodeId;
            ToNodeId = toNodeId;
            _resourceIds = Copy(resourceIds);
            _reservationIds = Copy(reservationIds);
            WindowStart = windowStart;
            WindowEnd = windowEnd;
            Detail = detail;
        }

        public int Index { get; }
        public double Time { get; }
        public KernelLogKind Kind { get; }
        public int JobId { get; }
        public int OccurrenceIndex { get; }
        public RouteStepKind? StepKind { get; }
        public string FromNodeId { get; }
        public string ToNodeId { get; }
        public IReadOnlyList<string> ResourceIds => Array.AsReadOnly(_resourceIds);
        public IReadOnlyList<long> ReservationIds => Array.AsReadOnly(_reservationIds);
        public double WindowStart { get; }
        public double WindowEnd { get; }
        public string Detail { get; }

        public override string ToString()
        {
            var inv = CultureInfo.InvariantCulture;
            return string.Join("|",
                Index.ToString(inv),
                Time.ToString("R", inv),
                Kind.ToString(),
                "j" + JobId.ToString(inv),
                "o" + OccurrenceIndex.ToString(inv),
                StepKind?.ToString() ?? "-",
                (FromNodeId ?? "-") + ">" + (ToNodeId ?? "-"),
                string.Join(",", _resourceIds),
                string.Join(",", Array.ConvertAll(_reservationIds, x => x.ToString(inv))),
                WindowStart.ToString("R", inv) + "~" + WindowEnd.ToString("R", inv),
                Detail ?? string.Empty);
        }

        private static T[] Copy<T>(IReadOnlyList<T> src)
        {
            if (src == null || src.Count == 0) return Array.Empty<T>();
            var a = new T[src.Count];
            for (var i = 0; i < a.Length; i++) a[i] = src[i];
            return a;
        }
    }

    /// <summary>只追加日志；对外只读。</summary>
    public sealed class KernelEventLog
    {
        private readonly List<KernelLogEntry> _entries = new List<KernelLogEntry>();

        public IReadOnlyList<KernelLogEntry> Entries => _entries.AsReadOnly();

        internal KernelLogEntry Append(
            double time,
            KernelLogKind kind,
            int jobId,
            int occurrenceIndex,
            RouteStepKind? stepKind = null,
            string from = null,
            string to = null,
            IReadOnlyList<string> resourceIds = null,
            IReadOnlyList<long> reservationIds = null,
            double windowStart = double.NaN,
            double windowEnd = double.NaN,
            string detail = null)
        {
            var e = new KernelLogEntry(_entries.Count, time, kind, jobId, occurrenceIndex, stepKind, from, to,
                resourceIds, reservationIds, windowStart, windowEnd, detail);
            _entries.Add(e);
            return e;
        }
    }

    public sealed class KernelOptions
    {
        public double CargoLengthMeters = 1.0;
        /// <summary>“占位到离开为止”的节点保持预约时长；离开时按尾清时刻截短。</summary>
        public double HoldHorizonSeconds = 1e7;
        /// <summary>等待年龄提升间隔（严格正）。</summary>
        public double AgingIntervalSeconds = 60.0;
        public int MaxEvents = 200000;
        public bool CheckInvariantsEveryEvent = true;
        public int DefaultNodeHoldCapacity = 1;
        public Dictionary<string, int> NodeHoldCapacity = new Dictionary<string, int>(StringComparer.Ordinal);
        public int SegmentZoneCapacity = 1;
        public int DirectionLockSameWayCapacity = 8;
        public int ProcessStationCapacity = 1;
    }

    /// <summary>任务输入。同刻到达按 ArrivalSequence 排序，不按 JobId。</summary>
    public sealed class KernelJobSpec
    {
        public int JobId { get; set; }
        public RoutePlan Plan { get; set; }
        public double ArrivalTime { get; set; }
        public int ArrivalSequence { get; set; }
        public int BasePriority { get; set; }
    }

    public enum KernelJobStatus
    {
        Created,
        WaitingForResource,
        Committed,
        Moving,
        AtNode,
        Processing,
        InAction,
        Completed,
        Failed,
    }

    public sealed class KernelJobResult
    {
        public int JobId { get; internal set; }
        public KernelJobStatus Status { get; internal set; }
        public double ArrivalTime { get; internal set; }
        public double CompletionTime { get; internal set; } = double.NaN;
        public double MoveSeconds { get; internal set; }
        public double ProcessSeconds { get; internal set; }
        public double ActionSeconds { get; internal set; }
        /// <summary>完成时长中的等待部分 = 总时长 − 移动 − 加工 − 动作。</summary>
        public double WaitSeconds { get; internal set; }
        public int LastCompletedOccurrence { get; internal set; } = -1;
        public string FailureReason { get; internal set; }
    }

    public enum KernelOutcome
    {
        AllJobsFinished,
        NoFutureEvent,
        DeadlockDetected,
        EventLimitReached,
        InvariantViolation,
        InvalidConfiguration,
    }

    /// <summary>等待图的一条边：等待者 —(资源)→ 持有者。</summary>
    public sealed class KernelWaitEdge
    {
        public int WaiterJobId { get; internal set; }
        public int WaitingOccurrence { get; internal set; }
        public string ResourceId { get; internal set; }
        public IReadOnlyList<int> HolderJobIds { get; internal set; }
        public string Reason { get; internal set; }
    }

    public sealed class KernelDiagnostics
    {
        public string Message { get; internal set; }
        public IReadOnlyList<KernelWaitEdge> WaitEdges { get; internal set; } = Array.Empty<KernelWaitEdge>();
        public IReadOnlyList<int> CycleJobIds { get; internal set; } = Array.Empty<int>();
        public IReadOnlyList<int> UnfinishedJobIds { get; internal set; } = Array.Empty<int>();
        public IReadOnlyDictionary<KernelEventKind, int> EventKindHistogram { get; internal set; }
            = new Dictionary<KernelEventKind, int>();
    }

    public sealed class KernelRunResult
    {
        public KernelOutcome Outcome { get; internal set; }
        public double FinalTime { get; internal set; }
        public int ProcessedEvents { get; internal set; }
        public IReadOnlyList<KernelLogEntry> Log { get; internal set; }
        public IReadOnlyList<KernelJobResult> Jobs { get; internal set; }
        public KernelDiagnostics Diagnostics { get; internal set; }

        public KernelJobResult GetJob(int jobId)
        {
            if (Jobs == null) return null;
            for (var i = 0; i < Jobs.Count; i++)
            {
                if (Jobs[i].JobId == jobId) return Jobs[i];
            }

            return null;
        }

        /// <summary>确定性指纹：日志 + 任务结果的规范化文本（T16）。</summary>
        public string Signature()
        {
            var inv = CultureInfo.InvariantCulture;
            var sb = new System.Text.StringBuilder();
            sb.Append(Outcome).Append('\n');
            if (Log != null)
            {
                for (var i = 0; i < Log.Count; i++) sb.Append(Log[i]).Append('\n');
            }

            if (Jobs != null)
            {
                for (var i = 0; i < Jobs.Count; i++)
                {
                    var j = Jobs[i];
                    sb.Append(j.JobId.ToString(inv)).Append(':').Append(j.Status).Append(':')
                        .Append(j.CompletionTime.ToString("R", inv)).Append('\n');
                }
            }

            return sb.ToString();
        }
    }

    internal static class KernelResourceIds
    {
        public static string NodeHold(string nodeId) => "hold:" + nodeId;
    }
}
