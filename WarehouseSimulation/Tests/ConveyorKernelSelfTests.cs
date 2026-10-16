using System;
using System.Collections.Generic;
using System.Text;
using NonsensicalKit.Simulation.WarehouseSimulation.Core.Calendar;
using NonsensicalKit.Simulation.WarehouseSimulation.Core.Graph;
using NonsensicalKit.Simulation.WarehouseSimulation.Simulation.Kernel;
using NonsensicalKit.Simulation.WarehouseSimulation.Simulation.Routing;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Tests
{
    /// <summary>阶段3 DES 输送内核场景自测（文档 §12：T01–T05、T13、T14、T16–T18）。纯 C#。</summary>
    public static class ConveyorKernelSelfTests
    {
        private const double Tol = 1e-6;

        public static string RunAll()
        {
            var sb = new StringBuilder();
            var failed = 0;
            failed += Run(sb, "T01_SingleJob_NominalTime", T01);
            failed += Run(sb, "T02_SameLine_Following_TailClear", T02);
            failed += Run(sb, "T03_Merge_EqualPriority_ArrivalSequence", T03);
            failed += Run(sb, "T04_SharedLane_OppositeDirection_NoOverlap", T04);
            failed += Run(sb, "T05_DeadEnd_Process_Turnaround_Return", T05);
            failed += Run(sb, "T13_RepeatNode_OccurrenceCommittedOnce", T13);
            failed += Run(sb, "T14_HeadOn_DeadlockDiagnosed", T14);
            failed += Run(sb, "T16_Determinism_SameSignature", T16);
            failed += Run(sb, "T17_Arbitration_PriorityNotJobId", T17);
            failed += Run(sb, "T18_RetryOnlyFailedStep_NoDuplicateCommit", T18);
            failed += Run(sb, "Queue_ReleaseBeforeEntry_SameTime", TQueueOrder);
            failed += Run(sb, "T06_DeadEnd_NoReturn_FailsBeforeSim", T06);
            failed += Run(sb, "T11_SameInstant_ReleaseThenEnter_HalfOpen", T11);
            failed += Run(sb, "T12_BlockedMove_NoPartialReservation", T12);
            failed += Run(sb, "T19_Turnaround_ReverseDirectionLock_FixedSetup", T19);
            failed += Run(sb, "T20_PassThroughPolicy_None_Traversal_Exclusive", T20);
            sb.AppendLine(failed == 0 ? "ALL PASSED" : $"FAILED: {failed}");
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

        #region 场景

        private static string T01()
        {
            var g = new G();
            g.Node("A", ConveyorGraphNodeKind.InfeedPort).Node("B", ConveyorGraphNodeKind.PickupPoint).Link("A", "B", 10f);
            var graph = g.Build(out var err);
            if (graph == null) return err;
            var r = new ConveyorDesKernel(graph).Run(new[] { Job(graph, 1, "A", "B") });
            var e = ExpectFinished(r);
            if (e != null) return e;
            var j = r.GetJob(1);
            if (Math.Abs(j.CompletionTime - 10) > Tol) return $"完成时刻 {j.CompletionTime} != 10";
            if (Math.Abs(j.WaitSeconds) > Tol) return $"等待 {j.WaitSeconds} != 0";
            return null;
        }

        private static string T02()
        {
            var g = new G();
            g.Node("IN", ConveyorGraphNodeKind.InfeedPort).Node("B", ConveyorGraphNodeKind.Junction).Node("C", ConveyorGraphNodeKind.PickupPoint)
                .Link("IN", "B", 5f).Link("B", "C", 5f);
            var graph = g.Build(out var err);
            if (graph == null) return err;
            var r = new ConveyorDesKernel(graph).Run(new[]
            {
                Job(graph, 1, "IN", "C", seq: 0), Job(graph, 2, "IN", "C", seq: 1), Job(graph, 3, "IN", "C", seq: 2),
            });
            var e = ExpectFinished(r);
            if (e != null) return e;
            var expected = new[] { 10.0, 16.0, 22.0 };
            for (var i = 0; i < 3; i++)
            {
                var c = r.GetJob(i + 1).CompletionTime;
                if (Math.Abs(c - expected[i]) > Tol) return $"任务 {i + 1} 完成 {c} != {expected[i]}";
            }

            var tail1 = Find(r, KernelLogKind.TailCleared, 1, 0);
            var move2 = Find(r, KernelLogKind.MoveStarted, 2, 0);
            if (tail1 == null || move2 == null) return "缺少 TailCleared/MoveStarted 日志";
            if (move2.Time < tail1.Time - Tol) return $"后车进入 {move2.Time} 早于前车尾清 {tail1.Time}";
            return null;
        }

        private static string T03()
        {
            var graph = MergeGraph(1, out var err);
            if (graph == null) return err;
            // JobId 1 的 ArrivalSequence 更大；同优先级必须按到达序而非 JobId。
            var r = new ConveyorDesKernel(graph).Run(new[]
            {
                Job(graph, 1, "S1", "G", seq: 1), Job(graph, 2, "S2", "G", seq: 0),
            });
            var e = ExpectFinished(r);
            if (e != null) return e;
            if (!(r.GetJob(2).CompletionTime < r.GetJob(1).CompletionTime - Tol))
                return $"期望到达序 0 的任务2 先完成：t1={r.GetJob(1).CompletionTime}, t2={r.GetJob(2).CompletionTime}";
            return null;
        }

        private static string T04()
        {
            var g = new G();
            g.Node("L", ConveyorGraphNodeKind.InfeedPort).Node("R", ConveyorGraphNodeKind.InfeedPort)
                .Link("L", "R", 10f, bothWays: true, mode: ConveyorFlowMode.SharedReversibleLane);
            var graph = g.Build(out var err);
            if (graph == null) return err;
            var opt = new KernelOptions();
            opt.NodeHoldCapacity["L"] = 2;
            opt.NodeHoldCapacity["R"] = 2;
            var r = new ConveyorDesKernel(graph, opt).Run(new[]
            {
                Job(graph, 1, "L", "R", seq: 0), Job(graph, 2, "R", "L", seq: 1),
            });
            var e = ExpectFinished(r);
            if (e != null) return e;
            var w = new List<KernelLogEntry>();
            foreach (var x in r.Log)
            {
                if (x.Kind == KernelLogKind.ReservationCommitted && Contains(x.ResourceIds, "dirlock:")) w.Add(x);
            }

            if (w.Count != 2) return $"期望 2 次方向锁提交，实际 {w.Count}";
            var overlap = w[0].WindowStart < w[1].WindowEnd - Tol && w[1].WindowStart < w[0].WindowEnd - Tol;
            if (overlap) return $"对向方向锁窗口重叠：[{w[0].WindowStart},{w[0].WindowEnd}) vs [{w[1].WindowStart},{w[1].WindowEnd})";
            return null;
        }

        private static string T05()
        {
            var graph = DeadEndGraph(out var err);
            if (graph == null) return err;
            var r = new ConveyorDesKernel(graph).Run(new[] { Job(graph, 1, "IN", "PK", wrap: true) });
            var e = ExpectFinished(r);
            if (e != null) return e;
            var c = r.GetJob(1).CompletionTime;
            if (Math.Abs(c - 31) > Tol) return $"完成 {c} != 31 (5+5+10+1+5+5)";
            var p = FindKind(r, KernelLogKind.ProcessCompleted, 1);
            var t = FindKind(r, KernelLogKind.ActionCompleted, 1);
            KernelLogEntry back = null;
            foreach (var x in r.Log)
                if (x.Kind == KernelLogKind.MoveStarted && x.FromNodeId == "P1" && x.ToNodeId == "J1") back = x;
            if (p == null || t == null || back == null) return "缺少 加工/掉头/返程 日志";
            if (!(p.Index < t.Index && t.Index < back.Index)) return "顺序应为 加工→掉头→返程";
            if (t.StepKind != RouteStepKind.Turnaround) return "动作不是 Turnaround";
            return null;
        }

        private static string T13()
        {
            var graph = DeadEndGraph(out var err);
            if (graph == null) return err;
            var r = new ConveyorDesKernel(graph).Run(new[] { Job(graph, 1, "IN", "PK", wrap: true) });
            var e = ExpectFinished(r);
            if (e != null) return e;
            var holdOcc = new List<int>();
            var seen = new HashSet<int>();
            foreach (var x in r.Log)
            {
                if (x.Kind != KernelLogKind.ReservationCommitted) continue;
                if (!seen.Add(x.OccurrenceIndex)) return $"出现序号 {x.OccurrenceIndex} 被重复提交";
                if (Contains(x.ResourceIds, "hold:J1")) holdOcc.Add(x.OccurrenceIndex);
            }

            if (holdOcc.Count != 2) return $"J1 应被两个出现序号占用，实际 {holdOcc.Count}";
            if (holdOcc[0] == holdOcc[1]) return "J1 两次占用出现序号相同";
            return null;
        }

        private static string T14()
        {
            var g = new G();
            g.Node("X", ConveyorGraphNodeKind.InfeedPort).Node("Y", ConveyorGraphNodeKind.InfeedPort)
                .Node("G1", ConveyorGraphNodeKind.PickupPoint).Node("G2", ConveyorGraphNodeKind.PickupPoint)
                .Link("X", "Y", 5f, bothWays: true, mode: ConveyorFlowMode.SharedReversibleLane)
                .Link("X", "G1", 5f).Link("Y", "G2", 5f);
            var graph = g.Build(out var err);
            if (graph == null) return err;
            var r = new ConveyorDesKernel(graph).Run(new[]
            {
                Job(graph, 1, "X", "G2", seq: 0), Job(graph, 2, "Y", "G1", seq: 1),
            });
            if (r.Outcome != KernelOutcome.DeadlockDetected) return $"期望 DeadlockDetected，实际 {r.Outcome}: {r.Diagnostics.Message}";
            if (r.Diagnostics.CycleJobIds.Count != 2) return $"环长度 {r.Diagnostics.CycleJobIds.Count} != 2";
            if (r.Diagnostics.WaitEdges.Count != 2) return "等待边数量应为 2";
            foreach (var j in r.Jobs)
                if (j.Status != KernelJobStatus.Failed) return "未完成任务应标记 Failed";
            return null;
        }

        private static string T16()
        {
            var graph = MergeGraph(1, out var err);
            if (graph == null) return err;
            var a = new ConveyorDesKernel(graph).Run(ContentionJobs(graph, 0, 0, false)).Signature();
            var b = new ConveyorDesKernel(graph).Run(ContentionJobs(graph, 0, 0, false)).Signature();
            var c = new ConveyorDesKernel(graph).Run(ContentionJobs(graph, 0, 0, true)).Signature();
            if (a != b) return "两次运行签名不同";
            if (a != c) return "输入列表顺序改变导致签名不同";
            return null;
        }

        private static string T17()
        {
            var graph = MergeGraph(1, out var err);
            if (graph == null) return err;
            // 优先级：JobId 2（较大）优先级 5，应先通过合流点。
            var r = new ConveyorDesKernel(graph).Run(ContentionJobs(graph, 0, 5, false));
            var e = ExpectFinished(r);
            if (e != null) return e;
            var m1 = Find(r, KernelLogKind.MoveStarted, 1, 0);
            var m2 = Find(r, KernelLogKind.MoveStarted, 2, 0);
            if (!(m2.Time < m1.Time - Tol)) return $"优先级 5 的任务2 应先进入：m1={m1.Time} m2={m2.Time}";

            // 反向：JobId 1 优先级 5。
            r = new ConveyorDesKernel(graph).Run(ContentionJobs(graph, 5, 0, false));
            m1 = Find(r, KernelLogKind.MoveStarted, 1, 0);
            m2 = Find(r, KernelLogKind.MoveStarted, 2, 0);
            if (!(m1.Time < m2.Time - Tol)) return "优先级 5 的任务1 应先进入";
            return null;
        }

        private static string T18()
        {
            var graph = MergeGraph(1, out var err);
            if (graph == null) return err;
            var r = new ConveyorDesKernel(graph).Run(ContentionJobs(graph, 0, 5, false));
            var e = ExpectFinished(r);
            if (e != null) return e;
            var waited = false;
            var commits = new HashSet<string>();
            foreach (var x in r.Log)
            {
                if (x.Kind == KernelLogKind.WaitingForResource && x.JobId == 1 && x.OccurrenceIndex == 0) waited = true;
                if (x.Kind == KernelLogKind.ReservationCommitted && !commits.Add(x.JobId + "#" + x.OccurrenceIndex))
                    return $"任务 {x.JobId} 出现 {x.OccurrenceIndex} 重复提交";
            }

            if (!waited) return "失败方任务1 未记录 WaitingForResource(occ 0)";
            if (r.GetJob(1).WaitSeconds <= 0) return "失败方等待时间应 > 0";
            return null;
        }

        private static string TQueueOrder()
        {
            var q = new KernelEventQueue();
            q.Schedule(5, KernelEventKind.JobArrived, 1);
            q.Schedule(5, KernelEventKind.MoveStarted, 2);
            q.Schedule(5, KernelEventKind.TailCleared, 3);
            q.Schedule(5, KernelEventKind.MoveCompleted, 4);
            q.Schedule(5, KernelEventKind.TailCleared, 5);
            var order = new List<int>();
            while (q.TryDequeue(out var ev)) order.Add(ev.JobId);
            var s = string.Join(",", order);
            return s == "3,5,4,1,2" ? null : "同刻顺序错误: " + s;
        }


        private static string T06()
        {
            var g = new G();
            g.Node("IN", ConveyorGraphNodeKind.InfeedPort).Node("J1", ConveyorGraphNodeKind.Junction)
                .Node("P1", ConveyorGraphNodeKind.ProcessStation, n =>
                {
                    n.ProcessTag = "wrap";
                    n.ProcessServiceSeconds = 10f;
                    n.ReturnPolicy = ConveyorReturnPolicy.NoReturn;
                })
                .Node("PK", ConveyorGraphNodeKind.PickupPoint)
                .Link("IN", "J1", 5f).Link("J1", "P1", 5f).Link("J1", "PK", 5f);
            g.NoReturnValidation();
            var graph = g.Build(out var err);
            if (graph == null) return "建图应成功（关闭返程校验）：" + err;
            var plan = ConveyorRoutePlanner.TryPlan(graph, "IN", "PK",
                new[] { new ProcessRequirement { RequirementId = "wrap-0", Tag = "wrap", Sequence = 0, ServiceSeconds = 10f } });
            if (plan.Success) return "无返程死端应在规划阶段失败";
            if (plan.ErrorCode != RoutePlanErrorCode.NoLegalReturnRoute && plan.ErrorCode != RoutePlanErrorCode.Unreachable)
                return "错误码 " + plan.ErrorCode;

            // 即使人为把“只进不出”的计划塞给内核，也必须给出诊断终态而不是挂起。
            var bad = new RoutePlan
            {
                StartNodeId = "IN",
                GoalNodeId = "PK",
                Requirements = Array.Empty<ProcessRequirement>(),
                Steps = new[]
                {
                    new RouteStep { OccurrenceIndex = 0, Kind = RouteStepKind.Move, FromNodeId = "IN", ToNodeId = "J1", ArcId = "arc:IN>J1", NominalCostSeconds = 5f },
                    new RouteStep { OccurrenceIndex = 1, Kind = RouteStepKind.Move, FromNodeId = "J1", ToNodeId = "P1", ArcId = "arc:J1>P1", NominalCostSeconds = 5f },
                    new RouteStep { OccurrenceIndex = 2, Kind = RouteStepKind.Move, FromNodeId = "P1", ToNodeId = "J1", ArcId = "arc:P1>J1", NominalCostSeconds = 5f },
                },
            };
            var r = new ConveyorDesKernel(graph).Run(new[] { new KernelJobSpec { JobId = 1, Plan = bad } });
            if (r.Outcome != KernelOutcome.InvariantViolation) return "伪造的非法返程应得到 InvariantViolation，实际 " + r.Outcome;
            return null;
        }

        private static string T11()
        {
            var g = new G();
            g.Node("IN", ConveyorGraphNodeKind.InfeedPort).Node("B", ConveyorGraphNodeKind.Junction).Node("C", ConveyorGraphNodeKind.PickupPoint)
                .Link("IN", "B", 5f).Link("B", "C", 5f);
            var graph = g.Build(out var err);
            if (graph == null) return err;
            var r = new ConveyorDesKernel(graph).Run(new[] { Job(graph, 1, "IN", "C", seq: 0), Job(graph, 2, "IN", "C", seq: 1) });
            var e = ExpectFinished(r);
            if (e != null) return e;
            var tail1 = Find(r, KernelLogKind.TailCleared, 1, 0);
            var move2 = Find(r, KernelLogKind.MoveStarted, 2, 0);
            if (Math.Abs(move2.Time - tail1.Time) > Tol) return $"半开区间应紧接：尾清 {tail1.Time}，进入 {move2.Time}";
            KernelLogEntry commit2 = null;
            foreach (var x in r.Log)
                if (x.Kind == KernelLogKind.ReservationCommitted && x.JobId == 2 && x.OccurrenceIndex == 0) { commit2 = x; break; }
            if (commit2 == null || commit2.Index < tail1.Index) return "同刻应先记录释放再提交进入";
            return null;
        }

        private static string T12()
        {
            var g = new G();
            g.Node("X", ConveyorGraphNodeKind.InfeedPort).Node("Y", ConveyorGraphNodeKind.InfeedPort)
                .Node("G1", ConveyorGraphNodeKind.PickupPoint).Node("G2", ConveyorGraphNodeKind.PickupPoint)
                .Link("X", "Y", 5f, bothWays: true, mode: ConveyorFlowMode.SharedReversibleLane)
                .Link("X", "G1", 5f).Link("Y", "G2", 5f);
            var graph = g.Build(out var err);
            if (graph == null) return err;
            var k = new ConveyorDesKernel(graph);
            var r = k.Run(new[] { Job(graph, 1, "X", "G2", seq: 0), Job(graph, 2, "Y", "G1", seq: 1) });
            if (r.Outcome != KernelOutcome.DeadlockDetected) return "前置：应死锁，实际 " + r.Outcome;
            // 移动需要 方向锁+区段+目的占位；占位不可用 → 方向锁与区段都不得写入。
            var seg = WarehouseConveyorMapGraphAdapter.CanonicalSegmentId("X", "Y");
            foreach (var res in new[] { ResourceIds.DirectionLock(seg), ResourceIds.ConveyorZone(seg, 0) })
            {
                var list = k.Calendar.GetReservations(res);
                if (list.Count != 0) return $"{res} 遗留半张预约 {list.Count} 条";
            }

            return null;
        }

        private static string T19()
        {
            var graph = DeadEndGraph(out var err);
            if (graph == null) return err;
            var k = new ConveyorDesKernel(graph);
            var r = k.Run(new[] { Job(graph, 1, "IN", "PK", wrap: true) });
            var e = ExpectFinished(r);
            if (e != null) return e;
            var st = FindKind(r, KernelLogKind.ActionStarted, 1);
            var ct = FindKind(r, KernelLogKind.ActionCompleted, 1);
            if (st == null || ct == null || st.StepKind != RouteStepKind.Turnaround) return "缺少掉头动作";
            if (Math.Abs(ct.Time - st.Time - 1) > Tol) return $"掉头时长 {ct.Time - st.Time} != 1";
            var seg = WarehouseConveyorMapGraphAdapter.CanonicalSegmentId("J1", "P1");
            graph.TryGetArc("P1", "J1", out var back);
            graph.TryGetArc("J1", "P1", out var fwd);
            var turnLock = false;
            foreach (var h in k.Calendar.GetReservations(ResourceIds.DirectionLock(seg)))
            {
                if (h.RouteOccurrenceIndex != st.OccurrenceIndex) continue;
                turnLock = true;
                if (h.Direction != ResourceIds.DirectionForArc(back)) return "掉头方向锁应为返程方向";
                if (h.Direction == ResourceIds.DirectionForArc(fwd)) return "掉头方向锁与进站方向相同";
            }

            return turnLock ? null : "掉头未预约方向锁";
        }

        private static string T20()
        {
            var none = RunT20(StationPassThroughPolicy.None, out var e1);
            if (e1 != null) return "None: " + e1;
            var trav = RunT20(StationPassThroughPolicy.TraversalOnly, out var e2);
            if (e2 != null) return "TraversalOnly: " + e2;
            var excl = RunT20(StationPassThroughPolicy.ExclusiveWithService, out var e3);
            if (e3 != null) return "Exclusive: " + e3;
            // (A 加工开始, C 直通进入开始)
            if (Math.Abs(none.Item1 - 5) > Tol || Math.Abs(none.Item2 - 0) > Tol) return $"None 期望 (5,0)，实际 {none}";
            if (Math.Abs(trav.Item1 - 5) > Tol || Math.Abs(trav.Item2 - 6) > Tol) return $"TraversalOnly 期望 (5,6)，实际 {trav}";
            // Exclusive：B 独占 [0,6)，C 在 0 时刻预约未来窗口 [6,12)，A 的加工只能排到 12。
            if (Math.Abs(excl.Item1 - 12) > Tol || Math.Abs(excl.Item2 - 6) > Tol) return $"Exclusive 期望 (12,6)，实际 {excl}";
            return null;
        }

        /// <summary>A 从 IN1 进站加工；B、C 分别从 IN2、IN3 直通站点 S 到 G。返回 (A 加工开始, C 进入开始)。</summary>
        private static Tuple<double, double> RunT20(StationPassThroughPolicy policy, out string err)
        {
            var g = new G();
            g.Node("IN1", ConveyorGraphNodeKind.InfeedPort).Node("IN2", ConveyorGraphNodeKind.InfeedPort).Node("IN3", ConveyorGraphNodeKind.InfeedPort)
                .Node("S", ConveyorGraphNodeKind.ProcessStation, n =>
                {
                    n.ProcessTag = "wrap";
                    n.ProcessServiceSeconds = 10f;
                    n.PassThroughAllowed = true;
                    n.PassThroughPolicy = policy;
                })
                .Node("G", ConveyorGraphNodeKind.PickupPoint)
                .Link("IN1", "S", 5f).Link("IN2", "S", 5f).Link("IN3", "S", 5f).Link("S", "G", 5f);
            var graph = g.Build(out err);
            if (graph == null) return null;
            var opt = new KernelOptions();
            opt.NodeHoldCapacity["S"] = 3;
            var r = new ConveyorDesKernel(graph, opt).Run(new[]
            {
                Job(graph, 1, "IN1", "G", seq: 0, wrap: true), Job(graph, 2, "IN2", "G", seq: 1), Job(graph, 3, "IN3", "G", seq: 2),
            });
            err = ExpectFinished(r);
            if (err != null) return null;
            var p = FindKind(r, KernelLogKind.ProcessStarted, 1);
            var c = Find(r, KernelLogKind.MoveStarted, 3, 0);
            var b = Find(r, KernelLogKind.MoveStarted, 2, 0);
            if (p == null || c == null || b == null) { err = "缺少日志"; return null; }
            if (Math.Abs(b.Time) > Tol) { err = "B 应在 0 时刻直通"; return null; }
            return Tuple.Create(p.Time, c.Time);
        }

        #endregion

        #region 辅助

        private static List<KernelJobSpec> ContentionJobs(ConveyorGraph graph, int p1, int p2, bool reversed)
        {
            var list = new List<KernelJobSpec>
            {
                Job(graph, 1, "S1", "G", seq: 0, priority: p1),
                Job(graph, 2, "S2", "G", seq: 1, priority: p2),
            };
            if (reversed) list.Reverse();
            return list;
        }

        private static ConveyorGraph MergeGraph(int junctionCap, out string err)
        {
            var g = new G();
            g.Node("S1", ConveyorGraphNodeKind.InfeedPort).Node("S2", ConveyorGraphNodeKind.InfeedPort)
                .Node("J", ConveyorGraphNodeKind.Junction).Node("G", ConveyorGraphNodeKind.PickupPoint)
                .Link("S1", "J", 5f).Link("S2", "J", 5f).Link("J", "G", 5f);
            return g.Build(out err);
        }

        private static ConveyorGraph DeadEndGraph(out string err)
        {
            var g = new G();
            g.Node("IN", ConveyorGraphNodeKind.InfeedPort).Node("J1", ConveyorGraphNodeKind.Junction)
                .Node("P1", ConveyorGraphNodeKind.ProcessStation, n =>
                {
                    n.ProcessTag = "wrap";
                    n.ProcessServiceSeconds = 10f;
                    n.ReturnPolicy = ConveyorReturnPolicy.ReverseSameLane;
                    n.CollapsePortsToAny = false;
                })
                .Node("PK", ConveyorGraphNodeKind.PickupPoint)
                .Link("IN", "J1", 5f).Link("J1", "P1", 5f, bothWays: true, mode: ConveyorFlowMode.SharedReversibleLane, setup: 1f)
                .Link("J1", "PK", 5f);
            return g.Build(out err);
        }

        private static KernelJobSpec Job(ConveyorGraph graph, int id, string start, string goal, int seq = 0, int priority = 0, bool wrap = false)
        {
            var reqs = wrap
                ? new[] { new ProcessRequirement { RequirementId = "wrap-0", Tag = "wrap", Sequence = 0, ServiceSeconds = 10f } }
                : Array.Empty<ProcessRequirement>();
            var plan = ConveyorRoutePlanner.TryPlan(graph, start, goal, reqs);
            if (!plan.Success) throw new InvalidOperationException($"规划失败 {start}->{goal}: {plan.ErrorMessage}");
            return new KernelJobSpec { JobId = id, Plan = plan.Plan, ArrivalTime = 0, ArrivalSequence = seq, BasePriority = priority };
        }

        private static string ExpectFinished(KernelRunResult r) =>
            r.Outcome == KernelOutcome.AllJobsFinished ? null : $"结局 {r.Outcome}: {r.Diagnostics?.Message}";

        private static KernelLogEntry Find(KernelRunResult r, KernelLogKind kind, int jobId, int occ)
        {
            foreach (var x in r.Log)
                if (x.Kind == kind && x.JobId == jobId && x.OccurrenceIndex == occ) return x;
            return null;
        }

        private static KernelLogEntry FindKind(KernelRunResult r, KernelLogKind kind, int jobId)
        {
            foreach (var x in r.Log)
                if (x.Kind == kind && x.JobId == jobId) return x;
            return null;
        }

        private static bool Contains(IReadOnlyList<string> ids, string prefix)
        {
            foreach (var s in ids)
                if (s != null && s.StartsWith(prefix, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>小型图构建器（exit:/entry: 端口）。</summary>
        private sealed class G
        {
            private readonly ConveyorGraphBuildRequest _req = new ConveyorGraphBuildRequest
            {
                DefaultSpeedMetersPerSecond = 1f,
                DefaultProcessServiceSeconds = 10f,
                ValidateProcessStationReturns = true,
            };

            public G NoReturnValidation()
            {
                _req.ValidateProcessStationReturns = false;
                return this;
            }

            public G Node(string id, ConveyorGraphNodeKind kind, Action<ConveyorGraphNode> cfg = null)
            {
                var n = new ConveyorGraphNode
                {
                    NodeId = id, LogicalId = id, Kind = kind, CollapsePortsToAny = true,
                    ReturnPolicy = ConveyorReturnPolicy.LoopBack,
                };
                cfg?.Invoke(n);
                _req.Nodes.Add(n);
                return this;
            }

            public G Link(string a, string b, float len, bool bothWays = false,
                ConveyorFlowMode mode = ConveyorFlowMode.FixedOneWay, float setup = 0f)
            {
                var seg = WarehouseConveyorMapGraphAdapter.CanonicalSegmentId(a, b);
                _req.PhysicalSegments.Add(new PhysicalSegment
                {
                    PhysicalSegmentId = seg, LengthMeters = len, FlowMode = mode, ReverseSetupSeconds = setup,
                });
                Arc(a, b, seg, len);
                if (bothWays) Arc(b, a, seg, len);
                return this;
            }

            private void Arc(string a, string b, string seg, float len)
            {
                _req.Arcs.Add(new ConveyorArc
                {
                    ArcId = $"arc:{a}>{b}", PhysicalSegmentId = seg, FromNodeId = a, ToNodeId = b,
                    FromPortId = $"exit:{a}>{b}", ToPortId = $"entry:{a}>{b}",
                    LengthMeters = len, SpeedMetersPerSecond = 1f, RequiredTags = Array.Empty<string>(),
                });
            }

            public ConveyorGraph Build(out string err)
            {
                var b = ConveyorGraphBuilder.BuildAndValidate(_req);
                err = b.Success ? null : "建图失败: " + b.PrimaryErrorCode + " " + b.PrimaryErrorMessage;
                return b.Success ? b.Graph : null;
            }
        }

        #endregion
    }
}
