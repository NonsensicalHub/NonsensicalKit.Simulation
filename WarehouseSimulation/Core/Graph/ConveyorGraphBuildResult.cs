using System.Collections.Generic;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Core.Graph
{
    public sealed class ConveyorGraphValidationIssue
    {
        public ConveyorGraphErrorCode Code { get; set; }
        public string Message { get; set; }
        public string NodeId { get; set; }
        public string ArcId { get; set; }
        public string PhysicalSegmentId { get; set; }
        public bool IsError { get; set; } = true;
    }

    public sealed class ConveyorGraphBuildResult
    {
        public bool Success { get; set; }
        public ConveyorGraph Graph { get; set; }
        public List<ConveyorGraphValidationIssue> Issues { get; } = new List<ConveyorGraphValidationIssue>();

        public ConveyorGraphErrorCode PrimaryErrorCode
        {
            get
            {
                for (var i = 0; i < Issues.Count; i++)
                {
                    if (Issues[i].IsError)
                    {
                        return Issues[i].Code;
                    }
                }

                return Success ? ConveyorGraphErrorCode.Ok : ConveyorGraphErrorCode.NullOrEmptyMap;
            }
        }

        public string PrimaryErrorMessage
        {
            get
            {
                for (var i = 0; i < Issues.Count; i++)
                {
                    if (Issues[i].IsError)
                    {
                        return Issues[i].Message;
                    }
                }

                return Success ? null : "图构建失败。";
            }
        }

        public void AddError(
            ConveyorGraphErrorCode code,
            string message,
            string nodeId = null,
            string arcId = null,
            string physicalSegmentId = null)
        {
            Issues.Add(new ConveyorGraphValidationIssue
            {
                Code = code,
                Message = message,
                NodeId = nodeId,
                ArcId = arcId,
                PhysicalSegmentId = physicalSegmentId,
                IsError = true,
            });
            Success = false;
        }

        public void AddWarning(
            ConveyorGraphErrorCode code,
            string message,
            string nodeId = null,
            string arcId = null,
            string physicalSegmentId = null)
        {
            Issues.Add(new ConveyorGraphValidationIssue
            {
                Code = code,
                Message = message,
                NodeId = nodeId,
                ArcId = arcId,
                PhysicalSegmentId = physicalSegmentId,
                IsError = false,
            });
        }
    }

    /// <summary>纯数据构建请求，便于单测且不依赖 ScriptableObject。</summary>
    public sealed class ConveyorGraphBuildRequest
    {
        public List<ConveyorGraphNode> Nodes { get; set; } = new List<ConveyorGraphNode>();
        public List<ConveyorArc> Arcs { get; set; } = new List<ConveyorArc>();
        public List<PhysicalSegment> PhysicalSegments { get; set; } = new List<PhysicalSegment>();
        public float DefaultProcessServiceSeconds { get; set; } = 30f;
        public float DefaultSpeedMetersPerSecond { get; set; } = 0.6f;
        /// <summary>若为 true，对 ProcessStation 且无显式回路时检查返程策略。</summary>
        public bool ValidateProcessStationReturns { get; set; } = true;
    }
}
