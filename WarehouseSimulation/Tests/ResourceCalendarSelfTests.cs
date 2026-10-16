using System;
using System.Collections.Generic;
using System.Text;
using NonsensicalKit.Simulation.WarehouseSimulation.Core.Calendar;
using NonsensicalKit.Simulation.WarehouseSimulation.Core.Graph;
using NonsensicalKit.Simulation.WarehouseSimulation.Simulation.Routing;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Tests
{
    /// <summary>阶段2 ResourceCalendar 纯 C# 自测（T10/T11/T12/对向/随机容量等）。</summary>
    public static class ResourceCalendarSelfTests
    {
        public static string RunAll()
        {
            var sb = new StringBuilder();
            var failed = 0;
            failed += Run(sb, "T11_HalfOpenBoundary_BackToBack", TestT11HalfOpenBoundary);
            failed += Run(sb, "T12_AtomicBatch_FailNoSideEffect", TestT12AtomicFailRollback);
            failed += Run(sb, "Capacity_RejectsOverlapBeyondCap", TestCapacityReject);
            failed += Run(sb, "DirectionLock_OppositeConflict", TestOppositeDirectionConflict);
            failed += Run(sb, "DirectionLock_SameDirectionAllowed", TestSameDirectionAllowed);
            failed += Run(sb, "FindEarliest_DoesNotWrite", TestFindDoesNotWrite);
            failed += Run(sb, "CommitEarliest_ThenReleaseById", TestCommitEarliestAndRelease);
            failed += Run(sb, "T10_TailClear_OccupancyUntilEnd", TestT10TailClearOccupancy);
            failed += Run(sb, "T17_TwoJobs_DeterministicCommitOrder", TestT17TwoJobsDeterministic);
            failed += Run(sb, "T18_RetryAfterRelease_OnlyUncommitted", TestT18RetryAfterRelease);
            failed += Run(sb, "ConflictGroup_MutualExclusion", TestConflictGroup);
            failed += Run(sb, "PassThrough_DoesNotDemandProcessStation", TestPassThroughDemand);
            failed += Run(sb, "RandomReservations_NeverExceedCapacity", TestRandomNeverExceedCapacity);

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

        private static ResourceCalendar NewCap1(string id)
        {
            var cal = new ResourceCalendar();
            cal.Register(id, ResourceKind.ConveyorZone, capacity: 1);
            return cal;
        }

        private static string TestT11HalfOpenBoundary()
        {
            var cal = NewCap1("R");
            var a = cal.TryCommitExact(new[]
            {
                new ExactReservationSpec { ResourceId = "R", Start = 0, End = 10, Units = 1 },
            });
            if (!a.Success)
            {
                return "first commit failed";
            }

            var b = cal.TryCommitExact(new[]
            {
                new ExactReservationSpec { ResourceId = "R", Start = 10, End = 15, Units = 1 },
            });
            if (!b.Success)
            {
                return "back-to-back [10,15) should succeed after [0,10): " + b.Conflict?.Message;
            }

            if (cal.GetOccupancyAt("R", 10) != 1)
            {
                return "at t=10 only second reservation should be active";
            }

            if (cal.GetOccupancyAt("R", 9.999) != 1)
            {
                return "at t just before 10 first should still be active";
            }

            return null;
        }

        private static string TestT12AtomicFailRollback()
        {
            var cal = new ResourceCalendar();
            cal.Register("A", ResourceKind.ConveyorZone, 1);
            cal.Register("B", ResourceKind.ConveyorZone, 1);

            // 先占满 B
            var pre = cal.TryCommitExact(new[]
            {
                new ExactReservationSpec { ResourceId = "B", Start = 0, End = 20, Units = 1, JobId = 1 },
            });
            if (!pre.Success)
            {
                return "prefill B failed";
            }

            var before = cal.ReservationCount;
            var batch = cal.TryCommitExact(new[]
            {
                new ExactReservationSpec { ResourceId = "A", Start = 0, End = 10, Units = 1, JobId = 2 },
                new ExactReservationSpec { ResourceId = "B", Start = 0, End = 10, Units = 1, JobId = 2 },
            });

            if (batch.Success)
            {
                return "batch should fail because B is busy";
            }

            if (cal.ReservationCount != before)
            {
                return $"side effect: reservation count {cal.ReservationCount} != {before}";
            }

            if (cal.GetOccupancyAt("A", 5) != 0)
            {
                return "A should remain empty after failed batch";
            }

            return null;
        }

        private static string TestCapacityReject()
        {
            var cal = NewCap1("R");
            var a = cal.TryCommitExact(new[]
            {
                new ExactReservationSpec { ResourceId = "R", Start = 0, End = 10, Units = 1 },
            });
            var b = cal.TryCommitExact(new[]
            {
                new ExactReservationSpec { ResourceId = "R", Start = 5, End = 12, Units = 1 },
            });
            if (!a.Success || b.Success)
            {
                return "overlap beyond capacity should reject";
            }

            return null;
        }

        private static string TestOppositeDirectionConflict()
        {
            var cal = new ResourceCalendar();
            cal.Register("DL", ResourceKind.DirectionLock, capacity: 4);

            var fwd = cal.TryCommitExact(new[]
            {
                new ExactReservationSpec
                {
                    ResourceId = "DL", Start = 0, End = 10, Units = 1, Direction = TravelDirection.Forward,
                },
            });
            var rev = cal.TryCommitExact(new[]
            {
                new ExactReservationSpec
                {
                    ResourceId = "DL", Start = 5, End = 12, Units = 1, Direction = TravelDirection.Reverse,
                },
            });

            if (!fwd.Success)
            {
                return "forward should commit";
            }

            if (rev.Success || rev.Conflict?.Code != ResourceCalendarErrorCode.DirectionConflict)
            {
                return $"expected DirectionConflict, got success={rev.Success} code={rev.Conflict?.Code}";
            }

            // 对向在释放后可接
            cal.ReleaseAll(fwd.Handles);
            rev = cal.TryCommitExact(new[]
            {
                new ExactReservationSpec
                {
                    ResourceId = "DL", Start = 5, End = 12, Units = 1, Direction = TravelDirection.Reverse,
                },
            });
            if (!rev.Success)
            {
                return "reverse after release should succeed";
            }

            return null;
        }

        private static string TestSameDirectionAllowed()
        {
            var cal = new ResourceCalendar();
            cal.Register("DL", ResourceKind.DirectionLock, capacity: 2);
            var a = cal.TryCommitExact(new[]
            {
                new ExactReservationSpec
                {
                    ResourceId = "DL", Start = 0, End = 10, Units = 1, Direction = TravelDirection.Forward,
                },
            });
            var b = cal.TryCommitExact(new[]
            {
                new ExactReservationSpec
                {
                    ResourceId = "DL", Start = 0, End = 10, Units = 1, Direction = TravelDirection.Forward,
                },
            });
            if (!a.Success || !b.Success)
            {
                return "same-direction within capacity should allow";
            }

            var c = cal.TryCommitExact(new[]
            {
                new ExactReservationSpec
                {
                    ResourceId = "DL", Start = 0, End = 10, Units = 1, Direction = TravelDirection.Forward,
                },
            });
            if (c.Success)
            {
                return "third same-direction should exceed capacity 2";
            }

            return null;
        }

        private static string TestFindDoesNotWrite()
        {
            var cal = NewCap1("R");
            cal.TryCommitExact(new[]
            {
                new ExactReservationSpec { ResourceId = "R", Start = 0, End = 10, Units = 1 },
            });
            var before = cal.ReservationCount;
            var found = cal.FindEarliestWindow(
                new[] { ResourceWindowRequest.Create("R", 5) },
                earliestStart: 0);
            if (!found.Success || Math.Abs(found.AnchorStart - 10) > 1e-6)
            {
                return $"expected earliest at 10, got success={found.Success} anchor={found.AnchorStart}";
            }

            if (cal.ReservationCount != before)
            {
                return "FindEarliestWindow must not write";
            }

            return null;
        }

        private static string TestCommitEarliestAndRelease()
        {
            var cal = NewCap1("R");
            cal.TryCommitExact(new[]
            {
                new ExactReservationSpec { ResourceId = "R", Start = 0, End = 10, Units = 1 },
            });
            var commit = cal.CommitEarliest(
                new[] { ResourceWindowRequest.Create("R", 3) },
                earliestStart: 0,
                jobId: 7,
                routeOccurrenceIndex: 2);
            if (!commit.Success || commit.Handles.Count != 1)
            {
                return "CommitEarliest failed";
            }

            if (Math.Abs(commit.Handles[0].Start - 10) > 1e-6)
            {
                return "expected start at 10";
            }

            var id = commit.Handles[0].ReservationId;
            if (!cal.Release(id))
            {
                return "Release by id failed";
            }

            if (cal.GetOccupancyAt("R", 11) != 0)
            {
                return "occupancy should clear after release";
            }

            return null;
        }

        private static string TestT10TailClearOccupancy()
        {
            // 前车占用直到 TailClearTime=end；后车不能在 end 之前进入（容量1）
            var cal = NewCap1("zone");
            var head = cal.TryCommitExact(new[]
            {
                new ExactReservationSpec { ResourceId = "zone", Start = 0, End = 10, Units = 1, JobId = 1 },
            });
            var early = cal.TryCommitExact(new[]
            {
                new ExactReservationSpec { ResourceId = "zone", Start = 8, End = 12, Units = 1, JobId = 2 },
            });
            if (!head.Success || early.Success)
            {
                return "tail not cleared: follower must wait";
            }

            var ok = cal.TryCommitExact(new[]
            {
                new ExactReservationSpec { ResourceId = "zone", Start = 10, End = 14, Units = 1, JobId = 2 },
            });
            if (!ok.Success)
            {
                return "after tail clear should accept";
            }

            return null;
        }

        private static string TestT17TwoJobsDeterministic()
        {
            var cal = NewCap1("spur");
            // 两任务同刻请求：先提交者获胜；第二次失败且无写入。顺序由调用方（阶段3仲裁）决定。
            var j1 = cal.TryCommitExact(new[]
            {
                new ExactReservationSpec { ResourceId = "spur", Start = 0, End = 5, Units = 1, JobId = 1 },
            });
            var j2 = cal.TryCommitExact(new[]
            {
                new ExactReservationSpec { ResourceId = "spur", Start = 0, End = 5, Units = 1, JobId = 2 },
            });
            if (!j1.Success || j2.Success)
            {
                return "first commit wins, second must fail cleanly";
            }

            if (j2.Handles != null && j2.Handles.Count != 0)
            {
                return "failed commit must return empty handles";
            }

            return null;
        }

        private static string TestT18RetryAfterRelease()
        {
            var cal = NewCap1("R");
            var hold = cal.TryCommitExact(new[]
            {
                new ExactReservationSpec { ResourceId = "R", Start = 0, End = 10, Units = 1, JobId = 1 },
            });
            var find1 = cal.FindEarliestWindow(new[] { ResourceWindowRequest.Create("R", 4) }, 0);
            if (!find1.Success || Math.Abs(find1.AnchorStart - 10) > 1e-6)
            {
                return "should wait until 10";
            }

            // 未提交；释放占有者后可在 0 提交（模拟资源提前释放）
            cal.ReleaseAll(hold.Handles);
            var commit = cal.CommitEarliest(new[] { ResourceWindowRequest.Create("R", 4) }, 0, jobId: 2);
            if (!commit.Success || Math.Abs(commit.Handles[0].Start) > 1e-6)
            {
                return "after release, uncommitted step should commit at 0 without rebooking prior steps";
            }

            return null;
        }

        private static string TestConflictGroup()
        {
            var cal = new ResourceCalendar();
            cal.Register(new ResourceDefinition
            {
                ResourceId = "mov:NtoE",
                Kind = ResourceKind.JunctionMovement,
                Capacity = 1,
                ConflictGroupId = "J1-cross",
            }, out _);
            cal.Register(new ResourceDefinition
            {
                ResourceId = "mov:WtoS",
                Kind = ResourceKind.JunctionMovement,
                Capacity = 1,
                ConflictGroupId = "J1-cross",
            }, out _);

            var a = cal.TryCommitExact(new[]
            {
                new ExactReservationSpec { ResourceId = "mov:NtoE", Start = 0, End = 5, Units = 1 },
            });
            var b = cal.TryCommitExact(new[]
            {
                new ExactReservationSpec { ResourceId = "mov:WtoS", Start = 0, End = 5, Units = 1 },
            });
            if (!a.Success || b.Success)
            {
                return "conflict group should mutually exclude";
            }

            return null;
        }

        private static string TestPassThroughDemand()
        {
            var graph = BuildTinyGraphWithProcess(StationPassThroughPolicy.None);
            var step = new RouteStep
            {
                Kind = RouteStepKind.PassThrough,
                FromNodeId = "J1",
                ToNodeId = "P1",
                ArcId = "arc:J1>P1",
                PhysicalSegmentId = "seg:J1|P1",
                EntryPortId = "Any",
                ExitPortId = "exit:J1>P1",
                NominalCostSeconds = 5f,
            };
            var demands = new List<ResourceWindowRequest>();
            RouteStepResourceDemand.AppendDemandsForStep(graph, step, demands);
            for (var i = 0; i < demands.Count; i++)
            {
                if (demands[i].ResourceId.StartsWith("process:", StringComparison.Ordinal))
                {
                    return "PassThrough with policy None must not demand ProcessStation";
                }
            }

            var processStep = new RouteStep
            {
                Kind = RouteStepKind.Process,
                FromNodeId = "P1",
                ToNodeId = "P1",
                NominalCostSeconds = 10f,
            };
            var procDemands = new List<ResourceWindowRequest>();
            RouteStepResourceDemand.AppendDemandsForStep(graph, processStep, procDemands);
            if (procDemands.Count != 1 || procDemands[0].ResourceId != ResourceIds.ProcessStation("P1"))
            {
                return "Process step must demand ProcessStation";
            }

            return null;
        }

        private static string TestRandomNeverExceedCapacity()
        {
            var rng = new Random(20261016);
            var cal = new ResourceCalendar();
            cal.Register("R", ResourceKind.ConveyorZone, capacity: 3);
            cal.Register("DL", ResourceKind.DirectionLock, capacity: 2);

            for (var n = 0; n < 500; n++)
            {
                var useDir = rng.Next(2) == 0;
                var id = useDir ? "DL" : "R";
                var start = rng.NextDouble() * 100.0;
                var dur = 0.5 + rng.NextDouble() * 5.0;
                var dir = useDir
                    ? (rng.Next(2) == 0 ? TravelDirection.Forward : TravelDirection.Reverse)
                    : TravelDirection.None;
                cal.TryCommitExact(new[]
                {
                    new ExactReservationSpec
                    {
                        ResourceId = id,
                        Start = start,
                        End = start + dur,
                        Units = 1,
                        Direction = dir,
                        JobId = n,
                    },
                });
                // 无论成功失败，不变量都要成立
                if (!cal.ValidateCapacityInvariant(out var err))
                {
                    return $"invariant broken at n={n}: {err}";
                }
            }

            // 再随机释放一半
            var ids = new List<long>();
            // 无法枚举内部；通过再次随机 CommitEarliest/Find 验证
            for (var n = 0; n < 200; n++)
            {
                var found = cal.FindEarliestWindow(
                    new[] { ResourceWindowRequest.Create("R", 1.0) },
                    earliestStart: rng.NextDouble() * 50.0);
                if (found.Success)
                {
                    var c = cal.CommitEarliest(
                        new[] { ResourceWindowRequest.Create("R", 1.0) },
                        found.AnchorStart,
                        jobId: 10000 + n);
                    if (c.Success)
                    {
                        ids.Add(c.Handles[0].ReservationId);
                    }
                }

                if (!cal.ValidateCapacityInvariant(out var err))
                {
                    return $"invariant after commit n={n}: {err}";
                }
            }

            for (var i = 0; i < ids.Count; i += 2)
            {
                cal.Release(ids[i]);
            }

            if (!cal.ValidateCapacityInvariant(out var err2))
            {
                return "invariant after releases: " + err2;
            }

            return null;
        }

        private static ConveyorGraph BuildTinyGraphWithProcess(StationPassThroughPolicy policy)
        {
            var req = new ConveyorGraphBuildRequest
            {
                ValidateProcessStationReturns = false,
                DefaultSpeedMetersPerSecond = 1f,
            };
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
                PassThroughAllowed = true,
                PassThroughPolicy = policy,
                ReturnPolicy = ConveyorReturnPolicy.LoopBack,
                CollapsePortsToAny = true,
            });
            req.PhysicalSegments.Add(new PhysicalSegment
            {
                PhysicalSegmentId = "seg:J1|P1",
                LengthMeters = 5,
                FlowMode = ConveyorFlowMode.FixedOneWay,
            });
            req.Arcs.Add(new ConveyorArc
            {
                ArcId = "arc:J1>P1",
                PhysicalSegmentId = "seg:J1|P1",
                FromNodeId = "J1",
                ToNodeId = "P1",
                FromPortId = "exit:J1>P1",
                ToPortId = "entry:J1>P1",
                LengthMeters = 5,
                SpeedMetersPerSecond = 1,
                RequiredTags = Array.Empty<string>(),
            });
            var build = ConveyorGraphBuilder.BuildAndValidate(req);
            if (!build.Success)
            {
                throw new InvalidOperationException(build.PrimaryErrorMessage);
            }

            return build.Graph;
        }
    }
}
