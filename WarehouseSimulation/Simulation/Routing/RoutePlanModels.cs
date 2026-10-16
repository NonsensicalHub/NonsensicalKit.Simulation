using System;
using System.Collections.Generic;
using NonsensicalKit.Simulation.WarehouseSimulation.Core.Graph;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation.Routing
{
    public enum RouteStepKind
    {
        Move = 0,
        Process = 1,
        Turnaround = 2,
        Transfer = 3,
        PassThrough = 4,
    }

    /// <summary>有序工艺需求。阶段索引 = 在排序后列表中的位置。</summary>
    public sealed class ProcessRequirement
    {
        public string RequirementId { get; set; }
        public string Tag { get; set; }
        public int Sequence { get; set; }
        /// <summary>≤0 表示使用站点或图默认服务时间。</summary>
        public float ServiceSeconds { get; set; }
    }

    public sealed class RouteStep
    {
        public int OccurrenceIndex { get; set; }
        public RouteStepKind Kind { get; set; }
        public string FromNodeId { get; set; }
        public string ToNodeId { get; set; }
        public string ArcId { get; set; }
        public string EntryPortId { get; set; }
        public string ExitPortId { get; set; }
        public string ToEntryPortId { get; set; }
        public string ResourceId { get; set; }
        public string ProcessRequirementId { get; set; }
        public string PhysicalSegmentId { get; set; }
        /// <summary>静态名义成本（秒）：移动/服务/换向/移载，不含动态等待。</summary>
        public float NominalCostSeconds { get; set; }
    }

    public sealed class RoutePlan
    {
        public string StartNodeId { get; set; }
        public string GoalNodeId { get; set; }
        public IReadOnlyList<RouteStep> Steps { get; set; }
        public float TotalNominalCostSeconds { get; set; }
        public IReadOnlyList<ProcessRequirement> Requirements { get; set; }
    }

    public sealed class RoutePlanResult
    {
        public bool Success { get; set; }
        public RoutePlanErrorCode ErrorCode { get; set; }
        public string ErrorMessage { get; set; }
        public RoutePlan Plan { get; set; }

        public static RoutePlanResult Fail(RoutePlanErrorCode code, string message) =>
            new RoutePlanResult
            {
                Success = false,
                ErrorCode = code,
                ErrorMessage = message,
            };

        public static RoutePlanResult Ok(RoutePlan plan) =>
            new RoutePlanResult
            {
                Success = true,
                ErrorCode = RoutePlanErrorCode.Ok,
                Plan = plan,
            };
    }
}
