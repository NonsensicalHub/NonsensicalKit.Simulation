using System;
using System.Collections.Generic;
using System.Text;
using NonsensicalKit.Simulation.WarehouseSimulation.Core.Graph;
using NonsensicalKit.Simulation.WarehouseSimulation.Simulation.Routing;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Tests
{
    /// <summary>
    /// 阶段1纯 C# 自测（不依赖 Unity Test Runner / NUnit）。
    /// 在 Editor 控制台调用 <see cref="RunAll"/>，或后续挂菜单。
    /// </summary>
    public static class ConveyorGraphRouteSelfTests
    {
        public static string RunAll()
        {
            var sb = new StringBuilder();
            var failed = 0;
            failed += Run(sb, "DeadEnd_NoReturn_PlanFails", TestDeadEndNoReturnPlanFails);
            failed += Run(sb, "DeadEnd_NoReturn_BuildFails", TestDeadEndNoReturnBuildFails);
            failed += Run(sb, "DeadEnd_Reverse_HasInboundProcessReturnSteps", TestDeadEndReverseRouteSteps);
            failed += Run(sb, "RepeatNode_OccurrenceIndicesStable", TestRepeatNodeOccurrenceStable);
            failed += Run(sb, "MultiProcess_OrderedRoute", TestMultiProcessOrdered);
            failed += Run(sb, "PlainPath_NoProcess", TestPlainPath);

            if (failed == 0)
            {
                sb.AppendLine("ALL PASSED");
            }
            else
            {
                sb.AppendLine($"FAILED: {failed}");
            }

            return sb.ToString();
        }

        private static int Run(StringBuilder sb, string name, Func<string> test)
        {
            try
            {
                var err = test();
                if (string.IsNullOrEmpty(err))
                {
                    sb.AppendLine($"[PASS] {name}");
                    return 0;
                }

                sb.AppendLine($"[FAIL] {name}: {err}");
                return 1;
            }
            catch (Exception ex)
            {
                sb.AppendLine($"[FAIL] {name}: exception {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
        }

        private static string TestDeadEndNoReturnPlanFails()
        {
            // Infeed -> J1 -> P1 (only forward). P1 NoReturn.
            var build = BuildDeadEnd(includeReverse: false, returnPolicy: ConveyorReturnPolicy.NoReturn, validateReturns: false);
            if (!build.Success)
            {
                return "expected build success when ValidateProcessStationReturns=false, got " + build.PrimaryErrorCode;
            }

            var plan = ConveyorRoutePlanner.TryPlan(
                build.Graph,
                "IN",
                "PK",
                new[]
                {
                    new ProcessRequirement { RequirementId = "wrap-0", Tag = "wrap", Sequence = 0, ServiceSeconds = 10f },
                });

            if (plan.Success)
            {
                return "expected plan failure for NoReturn dead-end";
            }

            if (plan.ErrorCode != RoutePlanErrorCode.NoLegalReturnRoute
                && plan.ErrorCode != RoutePlanErrorCode.Unreachable)
            {
                return $"unexpected error {plan.ErrorCode}: {plan.ErrorMessage}";
            }

            return null;
        }

        private static string TestDeadEndNoReturnBuildFails()
        {
            var build = BuildDeadEnd(includeReverse: false, returnPolicy: ConveyorReturnPolicy.NoReturn, validateReturns: true);
            if (build.Success)
            {
                return "expected build failure for dead-end NoReturn";
            }

            if (build.PrimaryErrorCode != ConveyorGraphErrorCode.DeadEndNoLegalReturn
                && build.PrimaryErrorCode != ConveyorGraphErrorCode.NoLegalReturnRoute)
            {
                return $"unexpected build error {build.PrimaryErrorCode}: {build.PrimaryErrorMessage}";
            }

            return null;
        }

        private static string TestDeadEndReverseRouteSteps()
        {
            var build = BuildDeadEnd(includeReverse: true, returnPolicy: ConveyorReturnPolicy.ReverseSameLane, validateReturns: true);
            if (!build.Success)
            {
                return "build failed: " + build.PrimaryErrorMessage;
            }

            var plan = ConveyorRoutePlanner.TryPlan(
                build.Graph,
                "IN",
                "PK",
                new[]
                {
                    new ProcessRequirement { RequirementId = "wrap-0", Tag = "wrap", Sequence = 0, ServiceSeconds = 10f },
                });

            if (!plan.Success)
            {
                return $"plan failed: {plan.ErrorCode} {plan.ErrorMessage}";
            }

            var steps = plan.Plan.Steps;
            var sawMoveIn = false;
            var sawProcess = false;
            var sawTurnaround = false;
            var sawMoveOut = false;
            for (var i = 0; i < steps.Count; i++)
            {
                var s = steps[i];
                if (s.Kind == RouteStepKind.Move && s.FromNodeId == "J1" && s.ToNodeId == "P1")
                {
                    sawMoveIn = true;
                }

                if (s.Kind == RouteStepKind.Process && s.FromNodeId == "P1")
                {
                    sawProcess = true;
                    if (s.ProcessRequirementId != "wrap-0")
                    {
                        return "process step missing requirement id";
                    }
                }

                if (s.Kind == RouteStepKind.Turnaround && s.FromNodeId == "P1")
                {
                    sawTurnaround = true;
                }

                if (s.Kind == RouteStepKind.Move && s.FromNodeId == "P1" && s.ToNodeId == "J1")
                {
                    sawMoveOut = true;
                }
            }

            if (!sawMoveIn || !sawProcess || !sawTurnaround || !sawMoveOut)
            {
                return $"missing steps moveIn={sawMoveIn} process={sawProcess} turn={sawTurnaround} moveOut={sawMoveOut}; dump={Dump(steps)}";
            }

            // J1 出现两次（去支路前 / 返程后），occurrence 必须严格递增且唯一。
            var seen = new HashSet<int>();
            for (var i = 0; i < steps.Count; i++)
            {
                if (steps[i].OccurrenceIndex != i)
                {
                    return $"OccurrenceIndex expected {i}, got {steps[i].OccurrenceIndex}";
                }

                if (!seen.Add(steps[i].OccurrenceIndex))
                {
                    return "duplicate OccurrenceIndex";
                }
            }

            return null;
        }

        private static string TestRepeatNodeOccurrenceStable()
        {
            var build = BuildDeadEnd(includeReverse: true, returnPolicy: ConveyorReturnPolicy.ReverseSameLane, validateReturns: true);
            if (!build.Success)
            {
                return build.PrimaryErrorMessage;
            }

            var a = ConveyorRoutePlanner.TryPlan(
                build.Graph, "IN", "PK",
                new[] { new ProcessRequirement { RequirementId = "wrap-0", Tag = "wrap", Sequence = 0, ServiceSeconds = 5f } });
            var b = ConveyorRoutePlanner.TryPlan(
                build.Graph, "IN", "PK",
                new[] { new ProcessRequirement { RequirementId = "wrap-0", Tag = "wrap", Sequence = 0, ServiceSeconds = 5f } });

            if (!a.Success || !b.Success)
            {
                return "plan failed";
            }

            if (a.Plan.Steps.Count != b.Plan.Steps.Count)
            {
                return "unstable step count";
            }

            for (var i = 0; i < a.Plan.Steps.Count; i++)
            {
                var x = a.Plan.Steps[i];
                var y = b.Plan.Steps[i];
                if (x.OccurrenceIndex != y.OccurrenceIndex
                    || x.Kind != y.Kind
                    || x.FromNodeId != y.FromNodeId
                    || x.ToNodeId != y.ToNodeId
                    || x.ArcId != y.ArcId
                    || x.EntryPortId != y.EntryPortId
                    || x.ExitPortId != y.ExitPortId)
                {
                    return $"unstable step {i}";
                }
            }

            // 路径中 J1 至少两次出现在 From/To 中
            var j1Hits = 0;
            for (var i = 0; i < a.Plan.Steps.Count; i++)
            {
                var s = a.Plan.Steps[i];
                if (s.FromNodeId == "J1" || s.ToNodeId == "J1")
                {
                    j1Hits++;
                }
            }

            if (j1Hits < 2)
            {
                return "expected repeated J1 in route";
            }

            return null;
        }

        private static string TestMultiProcessOrdered()
        {
            // IN -> J1 -> S1 -> J1 -> S2 -> J1 -> PK  with reverse on both spurs
            var req = new ConveyorGraphBuildRequest
            {
                DefaultProcessServiceSeconds = 10f,
                DefaultSpeedMetersPerSecond = 1f,
                ValidateProcessStationReturns = true,
            };

            void AddNode(string id, ConveyorGraphNodeKind kind, string tag = null, ConveyorReturnPolicy ret = ConveyorReturnPolicy.LoopBack, bool collapse = true)
            {
                req.Nodes.Add(new ConveyorGraphNode
                {
                    NodeId = id,
                    LogicalId = id,
                    Kind = kind,
                    ProcessTag = tag,
                    ProcessServiceSeconds = 10f,
                    ReturnPolicy = ret,
                    CollapsePortsToAny = collapse,
                    PassThroughAllowed = false,
                });
            }

            AddNode("IN", ConveyorGraphNodeKind.InfeedPort);
            AddNode("J1", ConveyorGraphNodeKind.Junction);
            AddNode("S1", ConveyorGraphNodeKind.ProcessStation, "shape", ConveyorReturnPolicy.ReverseSameLane, collapse: false);
            AddNode("S2", ConveyorGraphNodeKind.ProcessStation, "wrap", ConveyorReturnPolicy.ReverseSameLane, collapse: false);
            AddNode("PK", ConveyorGraphNodeKind.PickupPoint);

            void AddPair(string a, string b, float len = 5f)
            {
                var seg = WarehouseConveyorMapGraphAdapter.CanonicalSegmentId(a, b);
                if (req.PhysicalSegments.TrueForAll(s => s.PhysicalSegmentId != seg))
                {
                    req.PhysicalSegments.Add(new PhysicalSegment
                    {
                        PhysicalSegmentId = seg,
                        LengthMeters = len,
                        FlowMode = ConveyorFlowMode.SharedReversibleLane,
                    });
                }

                req.Arcs.Add(new ConveyorArc
                {
                    ArcId = $"arc:{a}>{b}",
                    PhysicalSegmentId = seg,
                    FromNodeId = a,
                    ToNodeId = b,
                    FromPortId = $"exit:{a}>{b}",
                    ToPortId = $"entry:{a}>{b}",
                    LengthMeters = len,
                    SpeedMetersPerSecond = 1f,
                    RequiredTags = Array.Empty<string>(),
                });
                req.Arcs.Add(new ConveyorArc
                {
                    ArcId = $"arc:{b}>{a}",
                    PhysicalSegmentId = seg,
                    FromNodeId = b,
                    ToNodeId = a,
                    FromPortId = $"exit:{b}>{a}",
                    ToPortId = $"entry:{b}>{a}",
                    LengthMeters = len,
                    SpeedMetersPerSecond = 1f,
                    RequiredTags = Array.Empty<string>(),
                });
            }

            AddPair("IN", "J1");
            AddPair("J1", "S1");
            AddPair("J1", "S2");
            AddPair("J1", "PK");

            var build = ConveyorGraphBuilder.BuildAndValidate(req);
            if (!build.Success)
            {
                return "build failed: " + build.PrimaryErrorMessage;
            }

            var plan = ConveyorRoutePlanner.TryPlan(
                build.Graph,
                "IN",
                "PK",
                new[]
                {
                    new ProcessRequirement { RequirementId = "shape-0", Tag = "shape", Sequence = 0, ServiceSeconds = 10f },
                    new ProcessRequirement { RequirementId = "wrap-0", Tag = "wrap", Sequence = 1, ServiceSeconds = 10f },
                });

            if (!plan.Success)
            {
                return $"plan failed: {plan.ErrorCode} {plan.ErrorMessage}";
            }

            var processOrder = new List<string>();
            for (var i = 0; i < plan.Plan.Steps.Count; i++)
            {
                if (plan.Plan.Steps[i].Kind == RouteStepKind.Process)
                {
                    processOrder.Add(plan.Plan.Steps[i].ProcessRequirementId);
                }
            }

            if (processOrder.Count != 2
                || processOrder[0] != "shape-0"
                || processOrder[1] != "wrap-0")
            {
                return "process order wrong: " + string.Join(",", processOrder);
            }

            return null;
        }

        private static string TestPlainPath()
        {
            var req = new ConveyorGraphBuildRequest
            {
                DefaultSpeedMetersPerSecond = 1f,
                ValidateProcessStationReturns = false,
            };
            req.Nodes.Add(new ConveyorGraphNode { NodeId = "A", LogicalId = "A", Kind = ConveyorGraphNodeKind.InfeedPort, CollapsePortsToAny = true });
            req.Nodes.Add(new ConveyorGraphNode { NodeId = "B", LogicalId = "B", Kind = ConveyorGraphNodeKind.PickupPoint, CollapsePortsToAny = true });
            req.PhysicalSegments.Add(new PhysicalSegment
            {
                PhysicalSegmentId = "seg:A|B",
                LengthMeters = 10f,
                FlowMode = ConveyorFlowMode.FixedOneWay,
            });
            req.Arcs.Add(new ConveyorArc
            {
                ArcId = "arc:A>B",
                PhysicalSegmentId = "seg:A|B",
                FromNodeId = "A",
                ToNodeId = "B",
                FromPortId = ConveyorPortIds.Any,
                ToPortId = ConveyorPortIds.Any,
                LengthMeters = 10f,
                SpeedMetersPerSecond = 1f,
                RequiredTags = Array.Empty<string>(),
            });

            var build = ConveyorGraphBuilder.BuildAndValidate(req);
            if (!build.Success)
            {
                return build.PrimaryErrorMessage;
            }

            var plan = ConveyorRoutePlanner.TryPlan(build.Graph, "A", "B");
            if (!plan.Success)
            {
                return plan.ErrorMessage;
            }

            if (plan.Plan.Steps.Count != 1 || plan.Plan.Steps[0].Kind != RouteStepKind.Move)
            {
                return "expected single move";
            }

            if (Math.Abs(plan.Plan.TotalNominalCostSeconds - 10f) > 1e-4f)
            {
                return $"cost {plan.Plan.TotalNominalCostSeconds} != 10";
            }

            return null;
        }

        private static ConveyorGraphBuildResult BuildDeadEnd(
            bool includeReverse,
            ConveyorReturnPolicy returnPolicy,
            bool validateReturns)
        {
            var req = new ConveyorGraphBuildRequest
            {
                DefaultProcessServiceSeconds = 10f,
                DefaultSpeedMetersPerSecond = 1f,
                ValidateProcessStationReturns = validateReturns,
            };

            req.Nodes.Add(new ConveyorGraphNode
            {
                NodeId = "IN", LogicalId = "IN", Kind = ConveyorGraphNodeKind.InfeedPort, CollapsePortsToAny = true,
            });
            req.Nodes.Add(new ConveyorGraphNode
            {
                NodeId = "J1", LogicalId = "J1", Kind = ConveyorGraphNodeKind.Junction, CollapsePortsToAny = true,
            });
            req.Nodes.Add(new ConveyorGraphNode
            {
                NodeId = "P1",
                LogicalId = "P1",
                Kind = ConveyorGraphNodeKind.ProcessStation,
                ProcessTag = "wrap",
                ProcessServiceSeconds = 10f,
                ReturnPolicy = returnPolicy,
                CollapsePortsToAny = returnPolicy != ConveyorReturnPolicy.ReverseSameLane,
                PassThroughAllowed = false,
            });
            req.Nodes.Add(new ConveyorGraphNode
            {
                NodeId = "PK", LogicalId = "PK", Kind = ConveyorGraphNodeKind.PickupPoint, CollapsePortsToAny = true,
            });

            void AddSeg(string a, string b, ConveyorFlowMode mode, bool bothWays)
            {
                var seg = WarehouseConveyorMapGraphAdapter.CanonicalSegmentId(a, b);
                req.PhysicalSegments.Add(new PhysicalSegment
                {
                    PhysicalSegmentId = seg,
                    LengthMeters = 5f,
                    FlowMode = mode,
                    ReverseSetupSeconds = bothWays ? 1f : 0f,
                });
                req.Arcs.Add(new ConveyorArc
                {
                    ArcId = $"arc:{a}>{b}",
                    PhysicalSegmentId = seg,
                    FromNodeId = a,
                    ToNodeId = b,
                    FromPortId = $"exit:{a}>{b}",
                    ToPortId = $"entry:{a}>{b}",
                    LengthMeters = 5f,
                    SpeedMetersPerSecond = 1f,
                    RequiredTags = Array.Empty<string>(),
                });
                if (bothWays)
                {
                    req.Arcs.Add(new ConveyorArc
                    {
                        ArcId = $"arc:{b}>{a}",
                        PhysicalSegmentId = seg,
                        FromNodeId = b,
                        ToNodeId = a,
                        FromPortId = $"exit:{b}>{a}",
                        ToPortId = $"entry:{b}>{a}",
                        LengthMeters = 5f,
                        SpeedMetersPerSecond = 1f,
                        RequiredTags = Array.Empty<string>(),
                    });
                }
            }

            AddSeg("IN", "J1", ConveyorFlowMode.SharedReversibleLane, bothWays: true);
            AddSeg("J1", "P1", includeReverse ? ConveyorFlowMode.SharedReversibleLane : ConveyorFlowMode.FixedOneWay, bothWays: includeReverse);
            AddSeg("J1", "PK", ConveyorFlowMode.SharedReversibleLane, bothWays: true);

            return ConveyorGraphBuilder.BuildAndValidate(req);
        }

        private static string Dump(IReadOnlyList<RouteStep> steps)
        {
            var sb = new StringBuilder();
            for (var i = 0; i < steps.Count; i++)
            {
                var s = steps[i];
                sb.Append(s.OccurrenceIndex).Append(':').Append(s.Kind)
                    .Append(' ').Append(s.FromNodeId).Append("->").Append(s.ToNodeId)
                    .Append(" [").Append(s.EntryPortId).Append('/').Append(s.ExitPortId).Append("]; ");
            }

            return sb.ToString();
        }
    }
}
