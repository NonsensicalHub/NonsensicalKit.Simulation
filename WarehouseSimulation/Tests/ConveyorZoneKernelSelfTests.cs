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
    }
}
