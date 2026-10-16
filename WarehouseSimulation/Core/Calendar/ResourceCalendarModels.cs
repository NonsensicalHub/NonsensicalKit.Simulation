using System;
using System.Collections.Generic;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Core.Calendar
{
    /// <summary>半开时间窗 [Start, End)。</summary>
    public readonly struct TimeWindow : IEquatable<TimeWindow>
    {
        public readonly double Start;
        public readonly double End;

        public TimeWindow(double start, double end)
        {
            Start = start;
            End = end;
        }

        public double Duration => End - Start;

        public bool IsValid => End > Start && !double.IsNaN(Start) && !double.IsNaN(End)
                               && !double.IsInfinity(Start) && !double.IsInfinity(End);

        public bool Overlaps(TimeWindow other) =>
            Start < other.End && other.Start < End;

        public bool Equals(TimeWindow other) =>
            Start.Equals(other.Start) && End.Equals(other.End);

        public override bool Equals(object obj) => obj is TimeWindow other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return (Start.GetHashCode() * 397) ^ End.GetHashCode();
            }
        }

        public override string ToString() => $"[{Start}, {End})";
    }

    public sealed class ResourceDefinition
    {
        public string ResourceId { get; set; }
        public ResourceKind Kind { get; set; }
        /// <summary>容量单位数；DirectionLock 的容量表示同向可并行租约上限（通常较大或 1）。</summary>
        public int Capacity { get; set; } = 1;
        /// <summary>若非空，该资源属于该冲突组（组容量通常为 1）。</summary>
        public string ConflictGroupId { get; set; }
    }

    /// <summary>查询用：相对锚点的资源窗口需求。FindEarliestWindow 绝不写入。</summary>
    public sealed class ResourceWindowRequest
    {
        public string ResourceId { get; set; }
        public double Duration { get; set; }
        public double OffsetFromAnchor { get; set; }
        public int Units { get; set; } = 1;
        public TravelDirection Direction { get; set; }

        public static ResourceWindowRequest Create(
            string resourceId,
            double duration,
            double offsetFromAnchor = 0,
            int units = 1,
            TravelDirection direction = TravelDirection.None) =>
            new ResourceWindowRequest
            {
                ResourceId = resourceId,
                Duration = duration,
                OffsetFromAnchor = offsetFromAnchor,
                Units = Math.Max(1, units),
                Direction = direction,
            };
    }

    /// <summary>精确提交规格：必须完全落在给定 [Start,End)。</summary>
    public sealed class ExactReservationSpec
    {
        public string ResourceId { get; set; }
        public double Start { get; set; }
        public double End { get; set; }
        public int Units { get; set; } = 1;
        public TravelDirection Direction { get; set; }
        public int JobId { get; set; }
        public int RouteOccurrenceIndex { get; set; } = -1;

        public TimeWindow Window => new TimeWindow(Start, End);
    }

    /// <summary>已提交预约句柄；释放/撤销必须用 ReservationId。</summary>
    public sealed class ReservationHandle
    {
        public long ReservationId { get; set; }
        public string ResourceId { get; set; }
        public double Start { get; set; }
        public double End { get; set; }
        public int Units { get; set; }
        public TravelDirection Direction { get; set; }
        public int JobId { get; set; }
        public int RouteOccurrenceIndex { get; set; }
    }

    public sealed class ResourceConflictInfo
    {
        public ResourceCalendarErrorCode Code { get; set; }
        public string ResourceId { get; set; }
        public string Message { get; set; }
        public double SuggestedRetryAt { get; set; }
    }

    public sealed class FindWindowResult
    {
        public bool Success { get; set; }
        public double AnchorStart { get; set; }
        public List<TimeWindow> Windows { get; set; } = new List<TimeWindow>();
        public ResourceConflictInfo Conflict { get; set; }

        public static FindWindowResult Ok(double anchor, List<TimeWindow> windows) =>
            new FindWindowResult { Success = true, AnchorStart = anchor, Windows = windows };

        public static FindWindowResult Fail(ResourceConflictInfo conflict) =>
            new FindWindowResult { Success = false, Conflict = conflict };
    }

    public sealed class CommitResult
    {
        public bool Success { get; set; }
        public List<ReservationHandle> Handles { get; set; } = new List<ReservationHandle>();
        public ResourceConflictInfo Conflict { get; set; }

        public static CommitResult Ok(List<ReservationHandle> handles) =>
            new CommitResult { Success = true, Handles = handles };

        public static CommitResult Fail(ResourceConflictInfo conflict) =>
            new CommitResult { Success = false, Conflict = conflict, Handles = new List<ReservationHandle>() };
    }
}
