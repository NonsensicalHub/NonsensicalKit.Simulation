using System;
using System.Collections.Generic;
using System.Text;
using NonsensicalKit.Simulation.WarehouseSimulation.Simulation.Kernel.Zone;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Tests
{
    /// <summary>ConveyorZoneKernel（Primary 模式输送内核）自测。纯 C#。</summary>
    public static class ConveyorZoneKernelSelfTests
    {
        private const double Tol = 1e-6;

        public static string RunAll()
        {
            var sb = new StringBuilder();
            var failed = 0;
            failed += Run(sb, "Z01_SingleCargo_NominalTimes", Z01);
            failed += Run(sb, "Z02_Follower_WaitsForLeaderMoveOut", Z02);
            failed += Run(sb, "Z03_Goal_HeldUntilReleaseGoal", Z03);
            failed += Run(sb, "Z04_OppositeLock_Exclusive_SameDirShared", Z04);
            failed += Run(sb, "Z05_Fairness_OppositeWaiterNotStarved", Z05);
            failed += Run(sb, "Z06_Dwell_ProcessService", Z06);
            failed += Run(sb, "Z07_Proximity_NeighbourBlocksEntry", Z07);
            failed += Run(sb, "Z08_ClaimZone_WithLocks_TransferAndRelease", Z08);
            failed += Run(sb, "Z09_HeadOnCorridor_BundleDoesNotDeadlock", Z09);
            failed += Run(sb, "Z10_DirectionToken_OppositeWaitsSameDirectionEnters", Z10);
            failed += Run(sb, "Z11_PlacedCargo_NotYieldingToOppositeWaiter", Z11);
            sb.AppendLine(failed == 0 ? "ALL PASSED" : $"FAILED: {failed}");
            return sb.ToString();
        }

        private static int Run(StringBuilder sb, string name, Func<string> test)
        {
            string err;
            try { err = test(); }
            catch (Exception ex) { err = ex.ToString(); }
            sb.AppendLine(err == null ? $"[PASS] {name}" : $"[FAIL] {name}: {err}");
            return err == null ? 0 : 1;
        }

        private static List<ZoneKernelStep> Line(string prefix, int count, double hop, params string[] shared)
        {
            var list = new List<ZoneKernelStep>();
            for (var i = 0; i < count; i++)
            {
                var key = shared != null && i < shared.Length && shared[i] != null ? shared[i] : $"{prefix}{i}";
                list.Add(new ZoneKernelStep { ZoneKey = key, HopSeconds = hop });
            }

            return list;
        }

        private static void Drain(ConveyorZoneKernel k, double until)
        {
            while (k.TryPeekNextEventTime(out var t) && t <= until)
            {
                k.AdvanceTo(t);
            }

            k.AdvanceTo(until);
        }

        // 车身间距：a1 与 b0 离得太近（不足一个箱长），b 必须等 a 离开 a1 才能进 b0。
        private static string Z07()
        {
            var k = new ConveyorZoneKernel
            {
                ZonesTooClose = (x, y) => (x == "a1" && y == "b0") || (x == "b0" && y == "a1"),
            };
            k.Submit(1, Line("a", 3, 2), null, 0);  // a0 到 2，a1 到 4，a2 到 6
            k.Submit(2, Line("b", 2, 1), null, 3);  // 想在 3 进 b0（到 4），但 a1 从 2 开始被占
            Drain(k, 100);
            k.TryGetTimeline(1, out var ms1, out _, out _);
            k.TryGetTimeline(2, out var ms2, out _, out _);
            if (Math.Abs(ms2[0] - ms1[2]) > 1e-9) return $"b0 应在 a 驶离 a1 时（{ms1[2]}）才开始驶入，实际 {ms2[0]}";
            return k.Violations.Count == 0 ? null : string.Join(";", k.Violations);
        }

        // 占起点并一并拿锁：锁被占时占不上；SubmitPlaced 后锁归货物，驶离起点后下一步不需要就释放。
        private static string Z08()
        {
            var k = new ConveyorZoneKernel();
            var near = new List<ZoneLockClaim> { new ZoneLockClaim { Key = "near:J", Capacity = 1, Direction = 0 } };
            if (!k.TryClaimZone("o", 1, near)) return "首次占起点应成功";
            if (k.TryClaimZone("o2", 2, near)) return "邻域锁被 1 占着，2 不应占上";
            var steps = Line("x", 3, 1, "o");
            k.SubmitPlaced(1, steps, null, 0);
            Drain(k, 0.5);
            if (!k.TryClaimZone("o2", 2, near)) return "1 驶离起点后邻域锁应释放";
            k.ReleaseClaim("o2", 2);
            Drain(k, 100);
            return k.Violations.Count == 0 ? null : string.Join(";", k.Violations);
        }

        private static string Z01()
        {
            var k = new ConveyorZoneKernel();
            k.Submit(1, Line("a", 3, 2), null, 10);
            Drain(k, 100);
            k.TryGetTimeline(1, out var ms, out var arr, out _);
            if (Math.Abs(arr[2] - 16) > Tol) return $"arrive goal {arr[2]} != 16";
            if (Math.Abs(ms[0] - 10) > Tol) return $"start {ms[0]}";
            return k.Violations.Count == 0 ? null : string.Join(";", k.Violations);
        }

        private static string Z02()
        {
            var k = new ConveyorZoneKernel();
            var steps = Line("s", 3, 1);
            steps[1].MinDwellSeconds = 5; // 前车在 s1 停 5s
            k.Submit(1, steps, null, 0);
            k.Submit(2, Line("s", 3, 1), null, 0);
            Drain(k, 100);
            k.TryGetTimeline(1, out var ms1, out var arr1, out _);
            k.TryGetTimeline(2, out var ms2, out var arr2, out _);
            // 跟随者进 s1 只能在前车开始驶离 s1 之后。
            if (ms2[1] < ms1[2] - Tol) return $"follower entered s1 at {ms2[1]} before leader left at {ms1[2]}";
            return null;
        }

        private static string Z03()
        {
            var k = new ConveyorZoneKernel();
            k.Submit(1, Line("g", 2, 1), null, 0);
            k.Submit(2, Line("g", 2, 1), null, 0);
            Drain(k, 50);
            k.TryGetTimeline(2, out var ms2, out _, out _);
            if (!double.IsNaN(ms2[1])) return "second cargo entered goal while first still holds it";
            k.ReleaseGoal(1, 50);
            Drain(k, 100);
            k.TryGetTimeline(2, out ms2, out var arr2, out _);
            if (Math.Abs(ms2[1] - 50) > Tol || Math.Abs(arr2[1] - 51) > Tol) return $"after release ms={ms2[1]} arr={arr2[1]}";
            return null;
        }

        private static string Z04()
        {
            var k = new ConveyorZoneKernel();
            ZoneKernelStep S(string key, string lockKey, int dir) => new ZoneKernelStep
            {
                ZoneKey = key,
                HopSeconds = 1,
                AcquireOnMoveIn = lockKey == null ? new List<ZoneLockClaim>() : new List<ZoneLockClaim> { new ZoneLockClaim { Key = lockKey, Capacity = 5, Direction = dir } },
            };
            var a = new List<ZoneKernelStep> { S("a0", "L", 1), S("a1", null, 0), S("a2", null, 0) };
            var b = new List<ZoneKernelStep> { S("b0", "L", 1), S("b1", null, 0), S("b2", null, 0) };
            var c = new List<ZoneKernelStep> { S("c0", "L", 2), S("c1", null, 0) };
            var rel = new List<ZoneLockRelease> { new ZoneLockRelease { Key = "L", StepIndex = 2, DelaySeconds = 0 } };
            var relC = new List<ZoneLockRelease> { new ZoneLockRelease { Key = "L", StepIndex = 1, DelaySeconds = 0 } };
            k.Submit(1, a, rel, 0);
            k.Submit(2, b, rel, 0);
            k.Submit(3, c, relC, 0.5);
            Drain(k, 100);
            k.TryGetTimeline(2, out var msB, out _, out _);
            k.TryGetTimeline(3, out var msC, out _, out _);
            if (Math.Abs(msB[0]) > Tol) return $"same direction should share lock, b started {msB[0]}";
            if (msC[0] < 3 - Tol) return $"opposite started {msC[0]} before lock cleared at 3";
            return null;
        }

        private static string Z05()
        {
            var k = new ConveyorZoneKernel();
            // 同向车流不断续上时，较早等待的对向车必须在锁清空后先拿到。
            List<ZoneKernelStep> Mk(string p, int dir) => new List<ZoneKernelStep>
            {
                new ZoneKernelStep { ZoneKey = p + "0", HopSeconds = 1, AcquireOnMoveIn = new List<ZoneLockClaim> { new ZoneLockClaim { Key = "L", Capacity = 10, Direction = dir } } },
                new ZoneKernelStep { ZoneKey = p + "1", HopSeconds = 4 },
            };
            var rel = new List<ZoneLockRelease> { new ZoneLockRelease { Key = "L", StepIndex = 1, DelaySeconds = 0 } };
            k.Submit(1, Mk("x", 1), rel, 0);
            k.Submit(9, Mk("y", 2), rel, 0.5);
            k.Submit(2, Mk("x", 1), rel, 1.5); // x0 在 1s 已空出，但对向 9 等得更早
            Drain(k, 100);
            k.TryGetTimeline(9, out var ms9, out _, out _);
            k.TryGetTimeline(2, out var ms2, out _, out _);
            if (!(ms9[0] < ms2[0])) return $"opposite waiter starved: y={ms9[0]} x2={ms2[0]}";
            return null;
        }

        private static string Z06()
        {
            var k = new ConveyorZoneKernel();
            var steps = Line("p", 3, 1);
            steps[1].MinDwellSeconds = 30;
            k.Submit(1, steps, null, 0);
            Drain(k, 100);
            k.TryGetTimeline(1, out var ms, out var arr, out _);
            if (Math.Abs(ms[2] - (arr[1] + 30)) > Tol) return $"service leave {ms[2]} != {arr[1] + 30}";
            return null;
        }

        /// <summary>
        /// 双向直段逐段占锁会对顶：两端各进一步后互相占着对方下一段。
        /// 进入前一次拿齐整段锁时，先到的先走完，另一辆再进。
        /// </summary>
        private static string Z09()
        {
            var perSegment = HeadOn("逐段", bundle: false);
            if (perSegment == null)
            {
                return "逐段占锁应死锁，实际两车都到了终点";
            }

            var bundled = HeadOn("整段", bundle: true);
            return bundled;
        }

        private static string HeadOn(string label, bool bundle)
        {
            var k = new ConveyorZoneKernel();
            ZoneLockClaim Lock(string key, int dir) => new ZoneLockClaim { Key = key, Capacity = 1, Direction = dir };
            List<ZoneKernelStep> East()
            {
                var steps = new List<ZoneKernelStep>
                {
                    new ZoneKernelStep { ZoneKey = "E0", HopSeconds = 1 },
                    new ZoneKernelStep { ZoneKey = "S1", HopSeconds = 1 },
                    new ZoneKernelStep { ZoneKey = "S2", HopSeconds = 1 },
                    new ZoneKernelStep { ZoneKey = "S3", HopSeconds = 1 },
                    new ZoneKernelStep { ZoneKey = "GE", HopSeconds = 1 },
                };
                if (bundle)
                {
                    steps[0].AcquireOnMoveIn.Add(Lock("L1", 1));
                    steps[0].AcquireOnMoveIn.Add(Lock("L2", 1));
                    steps[0].AcquireOnMoveIn.Add(Lock("L3", 1));
                }
                else
                {
                    steps[1].AcquireOnMoveIn.Add(Lock("L1", 1));
                    steps[2].AcquireOnMoveIn.Add(Lock("L2", 1));
                    steps[3].AcquireOnMoveIn.Add(Lock("L3", 1));
                }

                return steps;
            }

            List<ZoneKernelStep> West()
            {
                var steps = new List<ZoneKernelStep>
                {
                    new ZoneKernelStep { ZoneKey = "W0", HopSeconds = 1 },
                    new ZoneKernelStep { ZoneKey = "S3", HopSeconds = 1 },
                    new ZoneKernelStep { ZoneKey = "S2", HopSeconds = 1 },
                    new ZoneKernelStep { ZoneKey = "S1", HopSeconds = 1 },
                    new ZoneKernelStep { ZoneKey = "GW", HopSeconds = 1 },
                };
                if (bundle)
                {
                    steps[0].AcquireOnMoveIn.Add(Lock("L1", 2));
                    steps[0].AcquireOnMoveIn.Add(Lock("L2", 2));
                    steps[0].AcquireOnMoveIn.Add(Lock("L3", 2));
                }
                else
                {
                    steps[1].AcquireOnMoveIn.Add(Lock("L3", 2));
                    steps[2].AcquireOnMoveIn.Add(Lock("L2", 2));
                    steps[3].AcquireOnMoveIn.Add(Lock("L1", 2));
                }

                return steps;
            }

            List<ZoneLockRelease> Rel(params string[] keys)
            {
                var list = new List<ZoneLockRelease>();
                foreach (var key in keys)
                {
                    list.Add(new ZoneLockRelease { Key = key, StepIndex = 4, DelaySeconds = 0 });
                }

                return list;
            }

            k.Submit(1, East(), Rel("L1", "L2", "L3"), 0);
            k.Submit(2, West(), Rel("L1", "L2", "L3"), 0);
            Drain(k, 100);
            k.TryGetTimeline(1, out _, out var a1, out _);
            k.TryGetTimeline(2, out _, out var a2, out _);
            var eastDone = !double.IsNaN(a1[4]);
            var westDone = !double.IsNaN(a2[4]);
            if (eastDone && westDone)
            {
                return k.Violations.Count == 0 ? null : label + " " + string.Join(";", k.Violations);
            }

            if (k.TryPeekNextEventTime(out _))
            {
                return label + " 未到终点但仍有事件";
            }

            return label + $" 对顶 east={eastDone} west={westDone} active={k.ActiveCount}";
        }

        /// <summary>
        /// 直道方向锁容量不限：同向可在锁还没放时进入；对向要等持有者到终点释放后才进。
        /// </summary>
        private static string Z10()
        {
            var k = new ConveyorZoneKernel();
            ZoneLockClaim Token(int dir) => new ZoneLockClaim { Key = "corr", Capacity = int.MaxValue, Direction = dir };
            List<ZoneKernelStep> Go(string prefix, string shared, int dir)
            {
                return new List<ZoneKernelStep>
                {
                    new ZoneKernelStep { ZoneKey = prefix + "0", HopSeconds = 1, AcquireOnMoveIn = new List<ZoneLockClaim> { Token(dir) } },
                    new ZoneKernelStep { ZoneKey = shared, HopSeconds = 1 },
                    new ZoneKernelStep { ZoneKey = prefix + "2", HopSeconds = 1 },
                };
            }

            var rel = new List<ZoneLockRelease> { new ZoneLockRelease { Key = "corr", StepIndex = 2, DelaySeconds = 0 } };
            k.Submit(1, Go("e", "S", 1), rel, 0);
            k.Submit(3, Go("f", "T", 1), rel, 0.5);
            k.Submit(2, Go("w", "U", 2), rel, 1.5);
            Drain(k, 100);
            k.TryGetTimeline(1, out _, out var leadArrive, out _);
            k.TryGetTimeline(3, out var follow, out var followArrive, out _);
            k.TryGetTimeline(2, out var opp, out _, out _);
            if (double.IsNaN(leadArrive[2]) || double.IsNaN(followArrive[2]) || double.IsNaN(opp[2])) return "方向锁下有车没走完";
            if (!(follow[0] < leadArrive[2])) return $"同向应在先导车释放前进入，follow={follow[0]} leadDone={leadArrive[2]}";
            if (!(opp[0] >= followArrive[2] - Tol)) return $"对向进早了 opp={opp[0]} followDone={followArrive[2]}";
            return k.Violations.Count == 0 ? null : string.Join(";", k.Violations);
        }

        /// <summary>
        /// 已经占着位置的车不能为了等对向而停在路口上，否则后面的车出不去。
        /// 还没进入线路的车仍然把锁让给更早的对向。
        /// </summary>
        private static string Z11()
        {
            var k = new ConveyorZoneKernel();
            ZoneLockClaim Token(int dir) => new ZoneLockClaim { Key = "corr", Capacity = int.MaxValue, Direction = dir };
            List<ZoneLockRelease> Rel() => new List<ZoneLockRelease> { new ZoneLockRelease { Key = "corr", StepIndex = 1, DelaySeconds = 0 } };
            k.Submit(3, new List<ZoneKernelStep>
            {
                new ZoneKernelStep { ZoneKey = "H0", HopSeconds = 1, AcquireOnMoveIn = new List<ZoneLockClaim> { Token(1) } },
                new ZoneKernelStep { ZoneKey = "H1", HopSeconds = 10 },
            }, Rel(), 0);
            k.Submit(2, new List<ZoneKernelStep>
            {
                new ZoneKernelStep { ZoneKey = "W0", HopSeconds = 1, AcquireOnMoveIn = new List<ZoneLockClaim> { Token(2) } },
                new ZoneKernelStep { ZoneKey = "W1", HopSeconds = 1 },
            }, Rel(), 0);
            if (!k.TryClaimZone("P", 1)) return "占不住起点";
            k.SubmitPlaced(1, new List<ZoneKernelStep>
            {
                new ZoneKernelStep { ZoneKey = "P", HopSeconds = 0 },
                new ZoneKernelStep { ZoneKey = "N", HopSeconds = 1, AcquireOnMoveIn = new List<ZoneLockClaim> { Token(1) } },
            }, Rel(), 0);
            k.AdvanceTo(0);
            k.TryGetTimeline(1, out var placed, out _, out _);
            k.TryGetTimeline(2, out var opp, out _, out _);
            if (double.IsNaN(placed[1])) return "已在线上的车被对向让行拦住了";
            if (!double.IsNaN(opp[0])) return "对向不该在方向锁还被占着时进入";
            return k.Violations.Count == 0 ? null : string.Join(";", k.Violations);
        }
    }
}
