using System.Text;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 串链 FK / CCD IK（≥4 轴）。连杆长度与轴向来自 <see cref="RobotArmAnim"/> 配置，
    /// 不依赖场景中关节的实际间距（可用「从层级测量」同步）。
    /// </summary>
    public static class RobotArmIk
    {
        private const float MaxCcdStepDegrees = 22f;
        private const float J1SweepStep = 30f;
        private const int J1SweepCount = 6; // ±30…±180

        /// <summary>
        /// 第 5 轴连杆与朝下轴点积达到该值视为对齐（arccos(0.9999)≈0.81°）。
    /// 旧阈值 0.98≈11.5°，会把「差一点」的倾斜当成成功。
    /// </summary>
        public const float TipDownAcceptDot = 0.9999f;

        /// <summary>CCD / 精修停止点积（arccos(0.999995)≈0.18°）。</summary>
        private const float TipDownDoneDot = 0.999995f;

        /// <summary>肘低于肩时的折向惩罚（度/米），避免近点选中「臂折向下 + 腕翻」解。</summary>
        private const float ElbowDropPenaltyPerMeter = 900f;

        /// <summary>|腕翻转相关轴| 超过该阈值后追加惩罚（度/度）。</summary>
        private const float WristFlipSoftLimit = 120f;
        private const float WristFlipPenaltyScale = 0.4f;

        private static readonly FkChain s_fk = new FkChain();
        private static float[] s_seed;
        private static float[] s_trialSeed;
        private static float[] s_trialOut;
        private static float[] s_best;

        private static bool IsReady(RobotArmAnim arm, float[] angles)
        {
            return arm != null &&
                   arm.JointCount >= RobotArmAnim.MinJointCount &&
                   angles != null &&
                   angles.Length >= arm.JointCount;
        }

        /// <summary>由关节角求末端位姿（世界）。</summary>
        public static void Forward(
            RobotArmAnim arm,
            float[] angles,
            out Vector3 tipPosition,
            out Quaternion tipRotation)
        {
            tipPosition = arm != null ? arm.transform.position : Vector3.zero;
            tipRotation = arm != null ? arm.transform.rotation : Quaternion.identity;
            if (!IsReady(arm, angles))
                return;

            s_fk.Bind(arm);
            s_fk.Evaluate(angles);
            tipPosition = s_fk.Tip;
            tipRotation = s_fk.TipRot;
        }

        /// <summary>FK 各关节原点与末端（用于 Gizmos / 调试）。</summary>
        public static void ForwardPositions(
            RobotArmAnim arm,
            float[] angles,
            out Vector3[] jointOrigins,
            out Vector3 tipPosition)
        {
            int n = arm != null ? arm.JointCount : 0;
            jointOrigins = n > 0 ? new Vector3[n] : System.Array.Empty<Vector3>();
            tipPosition = arm != null ? arm.transform.position : Vector3.zero;
            if (!IsReady(arm, angles))
                return;

            s_fk.Bind(arm);
            s_fk.Evaluate(angles);
            s_fk.CopyOrigins(jointOrigins);
            tipPosition = s_fk.Tip;
        }

        /// <summary>
        /// 位置 IK：多种子阻尼 CCD，在达到容差的解中选相对 seed 关节行程最小者，
        /// 避免肘部构型翻转导致点到点插值折臂。
    /// </summary>
        public static bool SolvePosition(
            RobotArmAnim arm,
            Vector3 targetWorld,
            float[] seedAngles,
            float[] outAngles)
        {
            if (!IsReady(arm, outAngles))
                return false;

            int n = arm.JointCount;
            arm.EnsureAngleBuffer(outAngles);
            EnsureAngles(ref s_seed, n);
            EnsureAngles(ref s_trialSeed, n);
            EnsureAngles(ref s_trialOut, n);
            EnsureAngles(ref s_best, n);

            if (seedAngles != null && seedAngles.Length >= n)
            {
                for (int i = 0; i < n; i++)
                    s_seed[i] = arm.ClampAngle(i, seedAngles[i]);
            }
            else
            {
                arm.CopyHomeAnglesTo(s_seed);
            }

            s_fk.Bind(arm);

            float tolerance = arm.IkPositionTolerance;
            float acceptSqr = tolerance * tolerance * 4f;

            float bestCost = float.PositiveInfinity;
            float bestErrSqr = float.PositiveInfinity;
            bool foundReachable = false;

            void Consider(float[] startSeed)
            {
                RunCcd(arm, targetWorld, startSeed, s_trialOut, arm.IkIterations);
                float errSqr = (targetWorld - s_fk.Tip).sqrMagnitude;
                float cost = ConfigurationCostFromChain(arm, s_seed, s_trialOut);
                bool reachable = errSqr <= acceptSqr;

                if (reachable)
                {
                    if (!foundReachable || cost < bestCost - 1e-4f ||
                        (Mathf.Abs(cost - bestCost) <= 1e-4f && errSqr < bestErrSqr))
                    {
                        foundReachable = true;
                        bestCost = cost;
                        bestErrSqr = errSqr;
                        CopyAngles(s_trialOut, s_best, n);
                    }
                }
                else if (!foundReachable && errSqr < bestErrSqr)
                {
                    bestErrSqr = errSqr;
                    bestCost = cost;
                    CopyAngles(s_trialOut, s_best, n);
                }
            }

            Consider(s_seed);
            bool seedReached = foundReachable;

            // 扫 J1：仅在 seed 不可达时启用。对侧目标时 CCD 易锁在肘部翻转盆地。
            if (!seedReached)
            {
                for (int k = 1; k <= J1SweepCount; k++)
                {
                    float delta = J1SweepStep * k;
                    CopyAngles(s_seed, s_trialSeed, n);
                    s_trialSeed[0] = arm.ClampAngle(0, s_seed[0] + delta);
                    Consider(s_trialSeed);

                    CopyAngles(s_seed, s_trialSeed, n);
                    s_trialSeed[0] = arm.ClampAngle(0, s_seed[0] - delta);
                    Consider(s_trialSeed);
                }
            }

            // 保持肘部同侧：强化 J3 符号后再扫少量 J1（≥3 轴时 J3 存在）
            if (n >= 3 && Mathf.Abs(s_seed[2]) > 1e-3f)
            {
                CopyAngles(s_seed, s_trialSeed, n);
                float mag = Mathf.Max(45f, Mathf.Abs(s_seed[2]));
                s_trialSeed[2] = arm.ClampAngle(2, Mathf.Sign(s_seed[2]) * mag);
                Consider(s_trialSeed);

                if (!seedReached)
                {
                    for (int k = 2; k <= 5; k += 3)
                    {
                        float delta = J1SweepStep * k;
                        s_trialSeed[0] = arm.ClampAngle(0, s_seed[0] + delta);
                        s_trialSeed[2] = arm.ClampAngle(2, Mathf.Sign(s_seed[2]) * mag);
                        Consider(s_trialSeed);
                        s_trialSeed[0] = arm.ClampAngle(0, s_seed[0] - delta);
                        Consider(s_trialSeed);
                    }
                }
            }

            // 对侧肘构型种子：近点时同侧盆地常得到「肘下压+腕翻」，需主动尝试对侧
            if (n >= 3)
            {
                float elbowSign = Mathf.Abs(s_seed[2]) > 1e-3f ? Mathf.Sign(s_seed[2]) : 1f;
                CopyAngles(s_seed, s_trialSeed, n);
                s_trialSeed[1] = arm.ClampAngle(1, Mathf.Clamp(-s_seed[1], -100f, 100f));
                s_trialSeed[2] = arm.ClampAngle(2, -elbowSign * Mathf.Max(70f, Mathf.Abs(s_seed[2])));
                Consider(s_trialSeed);

                CopyAngles(s_seed, s_trialSeed, n);
                s_trialSeed[1] = arm.ClampAngle(1, 35f);
                s_trialSeed[2] = arm.ClampAngle(2, -90f);
                Consider(s_trialSeed);

                CopyAngles(s_seed, s_trialSeed, n);
                s_trialSeed[1] = arm.ClampAngle(1, -35f);
                s_trialSeed[2] = arm.ClampAngle(2, 90f);
                Consider(s_trialSeed);
            }

            CopyAngles(s_best, outAngles, n);
            AlignAnglesToSeed(s_seed, outAngles, arm);
            s_fk.Evaluate(outAngles);
            return (targetWorld - s_fk.Tip).sqrMagnitude <= acceptSqr;
        }

        /// <summary>
        /// 位置 IK，可选末端朝下（第 5 轴连杆对齐世界下方）或完整朝向约束。
    /// 朝下模式走腕心 IK：先把第 5 轴原点放到 TCP 上方，再锁连杆朝下。
    /// </summary>
        public static bool SolveToTarget(
            RobotArmAnim arm,
            Vector3 targetWorld,
            float[] seedAngles,
            float[] outAngles,
            bool keepTipDown = false,
            bool constrainOrientation = false,
            Quaternion targetRotation = default,
            Vector3 downWorldAxis = default)
        {
            if (keepTipDown)
                return SolveKeepTipDown(arm, targetWorld, seedAngles, outAngles, downWorldAxis);

            if (!SolvePosition(arm, targetWorld, seedAngles, outAngles))
            {
                // SolvePosition 失败时仍保留最近解，继续尝试姿态精修
            }

            if (constrainOrientation)
                RefineOrientation(arm, targetRotation, outAngles);

            if (!IsReady(arm, outAngles))
                return false;

            s_fk.Bind(arm);
            s_fk.Evaluate(outAngles);
            float acceptSqr = arm.IkPositionTolerance * arm.IkPositionTolerance * 4f;
            return (targetWorld - s_fk.Tip).sqrMagnitude <= acceptSqr;
        }

        /// <summary>
        /// 朝下约束局部轴：第 5 轴原点 → TCP（在第 5 轴 FK 坐标系下）。
    /// 比「第 5 轴→子节点」连杆更贴近「末端朝下」；本机 J6 连杆与 J5 连杆不共线，
        /// 只锁连杆会留下约 3–4° 的 TCP 倾角。
    /// </summary>
        public static Vector3 TipDownConstraintLocal(RobotArmAnim arm, float[] angles)
        {
            if (!IsReady(arm, angles))
                return FallbackTipDownLocal(arm);

            s_fk.Bind(arm);
            s_fk.Evaluate(angles);
            return TipDownConstraintLocalFromFk(arm);
        }

        /// <summary>朝下约束方向在世界空间的当前值（第 5 轴原点 → TCP）。</summary>
        public static Vector3 TipDownDirectionWorld(RobotArmAnim arm, float[] angles)
        {
            if (!IsReady(arm, angles))
                return Vector3.down;

            s_fk.Bind(arm);
            s_fk.Evaluate(angles);
            int frame = Mathf.Clamp(arm.TipDownJointIndex, 0, arm.JointCount - 1);
            Vector3 world = s_fk.Tip - s_fk.Origins[frame];
            if (world.sqrMagnitude < 1e-12f)
                return s_fk.WorldRot[frame] * FallbackTipDownLocal(arm);
            return world.normalized;
        }

        private static Vector3 TipDownConstraintLocalFromFk(RobotArmAnim arm)
        {
            int frame = Mathf.Clamp(arm.TipDownJointIndex, 0, arm.JointCount - 1);
            Vector3 world = s_fk.Tip - s_fk.Origins[frame];
            if (world.sqrMagnitude < 1e-12f)
                return FallbackTipDownLocal(arm);
            return (Quaternion.Inverse(s_fk.WorldRot[frame]) * world).normalized;
        }

        private static Vector3 FallbackTipDownLocal(RobotArmAnim arm)
        {
            Vector3 local = arm.TipDownAxisLocal;
            return local.sqrMagnitude > 1e-8f ? local.normalized : Vector3.up;
        }

        /// <summary>朝下约束方向与世界目标轴的点积（1 = 完全对齐）。</summary>
        public static float TipAxisDot(RobotArmAnim arm, float[] angles, Vector3 worldAxis)
        {
            if (!IsReady(arm, angles))
                return -1f;
            if (worldAxis.sqrMagnitude < 1e-8f)
                worldAxis = Vector3.down;
            else
                worldAxis.Normalize();

            return Vector3.Dot(TipDownDirectionWorld(arm, angles), worldAxis);
        }

        public static bool IsTipAxisAligned(
            RobotArmAnim arm,
            float[] angles,
            Vector3 worldAxis,
            float minDot = TipDownAcceptDot)
        {
            return TipAxisDot(arm, angles, worldAxis) >= minDot;
        }

        /// <summary>
        /// 硬锁朝下：一次转到位。腕部能对齐则只动腕；
        /// 腕部因限位差几度时，用 J1–J5 补齐剩余倾角（第 6 轴不参与）。
    /// </summary>
        public static void ForceTipDown(
            RobotArmAnim arm,
            float[] angles,
            Vector3 downWorldAxis = default)
        {
            if (!IsReady(arm, angles))
                return;

            if (downWorldAxis.sqrMagnitude < 1e-8f)
                downWorldAxis = arm.DownWorldAxis;
            else
                downWorldAxis.Normalize();

            int n = arm.JointCount;
            int frame = Mathf.Clamp(arm.TipDownJointIndex, 0, n - 1);
            // J6 在朝下求解中保持不动，J5→TCP 局部方向对 J1–J5 不变，可一次取定。
            Vector3 axisLocal = TipDownConstraintLocal(arm, angles);

            s_fk.Bind(arm);
            s_fk.Evaluate(angles);
            Vector3 current = s_fk.WorldRot[frame] * axisLocal;
            // 超过 90°（含从竖直朝上翻到朝下）时先动肩/肘，避免 J5 贪心翻到 ±175° 限位。
            bool largeSwing = Vector3.Dot(current, downWorldAxis) < 0f;
            SnapTipDown(arm, axisLocal, downWorldAxis, angles, frame, wristOnly: !largeSwing);
            PolishTipAxis(
                arm, axisLocal, downWorldAxis, angles, frame,
                largeSwing ? 0 : arm.WristStartIndex);

            if (!IsTipAxisAligned(arm, angles, downWorldAxis, TipDownAcceptDot))
            {
                SnapTipDown(arm, axisLocal, downWorldAxis, angles, frame, wristOnly: false);
                PolishTipAxis(arm, axisLocal, downWorldAxis, angles, frame, 0);
            }
        }

        /// <summary>
        /// 末端朝下：先把第 5 轴原点放到腕心（TCP − 朝下×J5→TCP 长度），再锁 J5→TCP 到世界下方。
    /// 禁止「先够 TCP 再翻腕」——翻腕会把末端甩开。第 6 轴角保持 seed，不参与朝下。
    /// </summary>
        private static bool SolveKeepTipDown(
            RobotArmAnim arm,
            Vector3 targetWorld,
            float[] seedAngles,
            float[] outAngles,
            Vector3 downWorldAxis)
        {
            if (!IsReady(arm, outAngles))
                return false;

            if (downWorldAxis.sqrMagnitude < 1e-8f)
                downWorldAxis = Vector3.down;
            else
                downWorldAxis.Normalize();

            int n = arm.JointCount;
            int frame = Mathf.Clamp(arm.TipDownJointIndex, 0, n - 1);
            int armMax = Mathf.Clamp(arm.ArmJointCount - 1, 0, Mathf.Max(0, frame - 1));
            float acceptSqr = arm.IkPositionTolerance * arm.IkPositionTolerance * 4f;
            // 多种子初选须达到朝下合格点积，避免 Home 下 J5 翻到限位（约差 5°）被当成成功。
            float tipDownSeedDot = TipDownAcceptDot;

            EnsureAngles(ref s_seed, n);
            if (seedAngles != null && seedAngles.Length >= n)
            {
                for (int i = 0; i < n; i++)
                    s_seed[i] = arm.ClampAngle(i, seedAngles[i]);
            }
            else
            {
                arm.CopyHomeAnglesTo(s_seed);
            }

            // 约束轴 = J5→TCP；与 AlignedDistal 共线时腕心 = 目标 − 朝下×长度。
            Vector3 tipDownLocal = TipDownConstraintLocal(arm, s_seed);
            s_fk.Bind(arm);
            s_fk.Evaluate(s_seed);
            Vector3 localDistal =
                Quaternion.Inverse(s_fk.WorldRot[frame]) * (s_fk.Tip - s_fk.Origins[frame]);
            Vector3 wristTarget =
                targetWorld - AlignedDistalWorld(localDistal, tipDownLocal, downWorldAxis);

            CopyAngles(s_seed, outAngles, n);
            TryReachWristKeepTipDown(
                arm, wristTarget, targetWorld, s_seed, outAngles,
                tipDownLocal, downWorldAxis, frame, armMax,
                acceptSqr, tipDownSeedDot);

            // 位置与朝下交替收紧：臂关节补倾角会挪腕心，必须再拉回 TCP。
            for (int round = 0; round < 4; round++)
            {
                s_fk.Bind(arm);
                s_fk.Evaluate(outAngles);
                Vector3 distal = s_fk.Tip - s_fk.Origins[frame];
                SolvePositionToJointOrigin(
                    arm, targetWorld - distal, outAngles, outAngles, frame, armMax);
                ForceTipDown(arm, outAngles, downWorldAxis);
                PreserveJointsAfter(outAngles, s_seed, frame + 1, n);

                s_fk.Evaluate(outAngles);
                bool posOk = (targetWorld - s_fk.Tip).sqrMagnitude <= acceptSqr;
                bool oriOk = IsTipAxisAligned(arm, outAngles, downWorldAxis);
                if (posOk && oriOk)
                    break;
            }

            AlignAnglesToSeed(s_seed, outAngles, arm);
            return IsTipAxisAligned(arm, outAngles, downWorldAxis);
        }

        /// <summary>
        /// 多种子把第 5 轴原点送到腕心，再锁朝下。成功：TCP 够近且朝下点积够高。
    /// </summary>
        private static bool TryReachWristKeepTipDown(
            RobotArmAnim arm,
            Vector3 wristTarget,
            Vector3 tipTarget,
            float[] seedAngles,
            float[] outAngles,
            Vector3 tipDownLocal,
            Vector3 downWorldAxis,
            int frame,
            int armMax,
            float acceptSqr,
            float tipDownMinDot)
        {
            int n = arm.JointCount;
            int armCount = arm.ArmJointCount;
            EnsureAngles(ref s_trialSeed, n);
            EnsureAngles(ref s_trialOut, n);
            EnsureAngles(ref s_best, n);

            float bestErrSqr = float.PositiveInfinity;
            float bestDot = -2f;
            bool found = false;

            void Consider(float[] startSeed)
            {
                if (found && bestErrSqr <= acceptSqr * 0.25f)
                    return;

                CopyAngles(startSeed, s_trialOut, n);
                SolvePositionToJointOrigin(
                    arm, wristTarget, s_trialOut, s_trialOut, frame, armMax);
                SnapTipDown(
                    arm, tipDownLocal, downWorldAxis, s_trialOut, frame, wristOnly: true);
                PreserveJointsAfter(s_trialOut, startSeed, frame + 1, n);

                s_fk.Bind(arm);
                s_fk.Evaluate(s_trialOut);
                float errSqr = (tipTarget - s_fk.Tip).sqrMagnitude;
                float dot = Vector3.Dot(s_fk.WorldRot[frame] * tipDownLocal, downWorldAxis);
                bool ok = errSqr <= acceptSqr && dot >= tipDownMinDot;

                if (ok)
                {
                    if (!found || errSqr < bestErrSqr - 1e-6f ||
                        (Mathf.Abs(errSqr - bestErrSqr) <= 1e-6f && dot > bestDot))
                    {
                        found = true;
                        bestErrSqr = errSqr;
                        bestDot = dot;
                        CopyAngles(s_trialOut, s_best, n);
                    }
                }
                else if (!found &&
                         (dot > bestDot + 0.01f ||
                          (dot >= bestDot - 0.01f && errSqr < bestErrSqr)))
                {
                    bestErrSqr = errSqr;
                    bestDot = dot;
                    CopyAngles(s_trialOut, s_best, n);
                }
            }

            Consider(seedAngles);
            if (found && bestErrSqr <= acceptSqr)
            {
                CopyAngles(s_best, outAngles, n);
                return true;
            }

            if (armCount >= 2 && !(found && bestErrSqr <= acceptSqr * 0.25f))
            {
                CopyAngles(seedAngles, s_trialSeed, n);
                s_trialSeed[1] = arm.ClampAngle(1, 70f);
                if (armCount >= 3)
                    s_trialSeed[2] = arm.ClampAngle(2, 80f);
                Consider(s_trialSeed);

                // J5 从 ±90° 起步，避免从 0° 贪心翻转到 ±175° 限位。
                s_trialSeed[frame] = arm.ClampAngle(frame, 90f);
                Consider(s_trialSeed);
                s_trialSeed[frame] = arm.ClampAngle(frame, -90f);
                Consider(s_trialSeed);
            }

            if (armCount >= 1)
            {
                for (int k = 1; k <= J1SweepCount; k++)
                {
                    if (found && bestErrSqr <= acceptSqr * 0.25f)
                        break;

                    float delta = J1SweepStep * k;
                    CopyAngles(seedAngles, s_trialSeed, n);
                    s_trialSeed[0] = arm.ClampAngle(0, seedAngles[0] + delta);
                    Consider(s_trialSeed);

                    CopyAngles(seedAngles, s_trialSeed, n);
                    s_trialSeed[0] = arm.ClampAngle(0, seedAngles[0] - delta);
                    Consider(s_trialSeed);
                }
            }

            if (armCount >= 3 && !(found && bestErrSqr <= acceptSqr * 0.25f))
            {
                float elbowSign = Mathf.Abs(seedAngles[2]) > 1e-3f ? Mathf.Sign(seedAngles[2]) : 1f;
                CopyAngles(seedAngles, s_trialSeed, n);
                s_trialSeed[1] = arm.ClampAngle(1, Mathf.Clamp(-seedAngles[1], -100f, 100f));
                s_trialSeed[2] = arm.ClampAngle(2, -elbowSign * Mathf.Max(70f, Mathf.Abs(seedAngles[2])));
                Consider(s_trialSeed);

                CopyAngles(seedAngles, s_trialSeed, n);
                s_trialSeed[1] = arm.ClampAngle(1, 35f);
                s_trialSeed[2] = arm.ClampAngle(2, -90f);
                Consider(s_trialSeed);

                CopyAngles(seedAngles, s_trialSeed, n);
                s_trialSeed[1] = arm.ClampAngle(1, -35f);
                s_trialSeed[2] = arm.ClampAngle(2, 90f);
                Consider(s_trialSeed);
            }

            CopyAngles(s_best, outAngles, n);
            return found;
        }

        private static void PreserveJointsAfter(float[] angles, float[] seed, int startIndex, int count)
        {
            if (angles == null || seed == null)
                return;
            for (int i = startIndex; i < count && i < angles.Length && i < seed.Length; i++)
                angles[i] = seed[i];
        }

        /// <summary>
        /// 朝下锁定后，第 5 轴原点到 TCP 的世界偏移。连杆与约束轴共线时等于 down * 远端长度。
    /// </summary>
        private static Vector3 AlignedDistalWorld(
            Vector3 localDistal,
            Vector3 axisLocal,
            Vector3 down)
        {
            if (axisLocal.sqrMagnitude < 1e-8f)
                axisLocal = Vector3.up;
            else
                axisLocal.Normalize();

            float along = Vector3.Dot(localDistal, axisLocal);
            Vector3 perp = localDistal - axisLocal * along;
            if (perp.sqrMagnitude < 1e-10f)
                return down * along;

            Quaternion align;
            if (Vector3.Dot(axisLocal, down) < -0.999f)
            {
                Vector3 ortho = Vector3.Cross(axisLocal, Vector3.right);
                if (ortho.sqrMagnitude < 1e-8f)
                    ortho = Vector3.Cross(axisLocal, Vector3.forward);
                align = Quaternion.AngleAxis(180f, ortho.normalized);
            }
            else
            {
                align = Quaternion.FromToRotation(axisLocal, down);
            }

            return down * along + align * perp;
        }

        private static void SnapTipDown(
            RobotArmAnim arm,
            Vector3 axisLocal,
            Vector3 downWorldAxis,
            float[] angles,
            int frame,
            bool wristOnly)
        {
            int minJoint = wristOnly ? arm.WristStartIndex : 0;
            RefineTipAxis(
                arm, axisLocal, downWorldAxis, angles, 16, minJoint, 180f, frame);
        }

        /// <summary>
        /// 仅调整臂部关节拉近 TCP，保留腕部角（末端朝下不被冲掉）。
    /// </summary>
        public static bool SolvePositionArmOnly(
            RobotArmAnim arm,
            Vector3 targetWorld,
            float[] seedAngles,
            float[] outAngles,
            int armJointCount = -1)
        {
            if (!IsReady(arm, outAngles))
                return false;

            int n = arm.JointCount;
            arm.EnsureAngleBuffer(outAngles);
            EnsureAngles(ref s_seed, n);
            if (seedAngles != null && seedAngles.Length >= n)
            {
                for (int i = 0; i < n; i++)
                    s_seed[i] = arm.ClampAngle(i, seedAngles[i]);
            }
            else
            {
                arm.CopyHomeAnglesTo(s_seed);
            }

            if (armJointCount < 0)
                armJointCount = arm.ArmJointCount;
            armJointCount = Mathf.Clamp(armJointCount, 1, n);

            s_fk.Bind(arm);
            RunCcd(arm, targetWorld, s_seed, outAngles, arm.IkIterations, 0, armJointCount - 1);

            // 腕部保持 seed
            for (int i = armJointCount; i < n; i++)
                outAngles[i] = s_seed[i];

            float acceptSqr = arm.IkPositionTolerance * arm.IkPositionTolerance * 4f;
            s_fk.Evaluate(outAngles);
            return (targetWorld - s_fk.Tip).sqrMagnitude <= acceptSqr;
        }

        /// <summary>
        /// 仅用臂部把指定关节原点送到目标（朝下模式的腕心），其后关节角保持 seed。
    /// </summary>
        private static bool SolvePositionToJointOrigin(
            RobotArmAnim arm,
            Vector3 targetWorld,
            float[] seedAngles,
            float[] outAngles,
            int jointIndex,
            int maxJointIndex)
        {
            if (!IsReady(arm, outAngles) || seedAngles == null || seedAngles.Length < arm.JointCount)
                return false;

            int n = arm.JointCount;
            arm.EnsureAngleBuffer(outAngles);
            jointIndex = Mathf.Clamp(jointIndex, 0, n - 1);
            maxJointIndex = Mathf.Clamp(maxJointIndex, 0, Mathf.Max(0, jointIndex - 1));

            s_fk.Bind(arm);
            RunCcd(
                arm, targetWorld, seedAngles, outAngles,
                Mathf.Max(arm.IkIterations, 32),
                0, maxJointIndex, jointIndex);

            float acceptSqr = arm.IkPositionTolerance * arm.IkPositionTolerance * 4f;
            s_fk.Evaluate(outAngles);
            return (targetWorld - s_fk.Origins[jointIndex]).sqrMagnitude <= acceptSqr;
        }

        /// <summary>
        /// 将指定关节坐标系下的本地轴对齐到世界方向。
    /// 末端朝下时：frameJointIndex = 第 5 轴，axisLocal = 其连杆方向。
    /// </summary>
        /// <param name="minJointIndex">参与调整的最靠基座关节（含）；默认腕部起点。</param>
        /// <param name="maxStepDegrees">单次关节步长上限；朝下约束可加大以便一次转到位。</param>
        /// <param name="frameJointIndex">
        /// 方向所在关节（0-based）。&lt;0 表示末端（TipRot）。朝下用 TipDownJointIndex。
    /// </param>
        public static void RefineTipAxis(
            RobotArmAnim arm,
            Vector3 axisLocal,
            Vector3 worldTargetAxis,
            float[] angles,
            int passCount = 12,
            int minJointIndex = -1,
            float maxStepDegrees = -1f,
            int frameJointIndex = -1)
        {
            if (!IsReady(arm, angles))
                return;

            int n = arm.JointCount;
            if (axisLocal.sqrMagnitude < 1e-8f)
                axisLocal = Vector3.up;
            else
                axisLocal.Normalize();

            if (worldTargetAxis.sqrMagnitude < 1e-8f)
                return;
            worldTargetAxis.Normalize();

            if (minJointIndex < 0)
                minJointIndex = arm.WristStartIndex;
            minJointIndex = Mathf.Clamp(minJointIndex, 0, n - 1);
            if (maxStepDegrees < 1f)
                maxStepDegrees = MaxCcdStepDegrees;

            int frame = frameJointIndex < 0 ? n - 1 : Mathf.Clamp(frameJointIndex, 0, n - 1);
            // 只能用到 frame 及之前的关节来改该帧朝向；其后（如 J6）不参与朝下
            int maxJoint = frame;
            minJointIndex = Mathf.Min(minJointIndex, maxJoint);

            s_fk.Bind(arm);
            s_fk.Evaluate(angles);

            for (int pass = 0; pass < passCount; pass++)
            {
                bool moved = false;
                for (int j = maxJoint; j >= minJointIndex; j--)
                {
                    Vector3 current = s_fk.WorldRot[frame] * axisLocal;
                    if (Vector3.Dot(current, worldTargetAxis) >= TipDownDoneDot)
                        return;

                    Vector3 axisWorld = s_fk.AxesWorld[j];
                    Vector3 curProj = Vector3.ProjectOnPlane(current, axisWorld);
                    Vector3 tgtProj = Vector3.ProjectOnPlane(worldTargetAxis, axisWorld);
                    if (curProj.sqrMagnitude < 1e-10f || tgtProj.sqrMagnitude < 1e-10f)
                        continue;

                    float delta = Vector3.SignedAngle(curProj, tgtProj, axisWorld);
                    if (Mathf.Abs(delta) < 1e-5f)
                        continue;

                    // 近 180° 时选剩余行程更大的转向，避免 Normalize 后绕反方向。
                    if (Mathf.Abs(delta) > 179f)
                    {
                        arm.GetJointLimits(j, out float jMin, out float jMax);
                        float towardMax = jMax - angles[j];
                        float towardMin = angles[j] - jMin;
                        delta = towardMax >= towardMin
                            ? Mathf.Min(180f, towardMax)
                            : -Mathf.Min(180f, towardMin);
                    }

                    delta = Mathf.Clamp(delta, -maxStepDegrees, maxStepDegrees);
                    float next = arm.ClampAngle(
                        j, RobotArmAnim.NormalizeSignedAngle(angles[j] + delta));
                    if (Mathf.Abs(Mathf.DeltaAngle(angles[j], next)) < 1e-5f)
                        continue;

                    angles[j] = next;
                    s_fk.UpdateFrom(angles, j);
                    moved = true;
                }

                if (!moved)
                    break;
            }
        }

        /// <summary>
        /// 无额外步长限制的朝下精修，把腕部/臂部可贡献关节一次转到投影对准。
    /// </summary>
        private static void PolishTipAxis(
            RobotArmAnim arm,
            Vector3 axisLocal,
            Vector3 downWorldAxis,
            float[] angles,
            int frame,
            int minJointIndex)
        {
            RefineTipAxis(
                arm, axisLocal, downWorldAxis, angles,
                12, minJointIndex, 180f, frame);
        }

        /// <summary>在位置 IK 之后，用末端三轴尽量对齐目标朝向（仍受关节限位）。</summary>
        public static void RefineOrientation(
            RobotArmAnim arm,
            Quaternion targetRotation,
            float[] angles,
            int passCount = 8)
        {
            if (!IsReady(arm, angles))
                return;

            int n = arm.JointCount;
            int wristStart = arm.WristStartIndex;

            s_fk.Bind(arm);
            s_fk.Evaluate(angles);

            for (int pass = 0; pass < passCount; pass++)
            {
                for (int j = n - 1; j >= wristStart; j--)
                {
                    Vector3 axisWorld = s_fk.AxesWorld[j];
                    Vector3 tipForward = s_fk.TipRot * Vector3.forward;
                    Vector3 targetForward = targetRotation * Vector3.forward;
                    Vector3 tipProj = Vector3.ProjectOnPlane(tipForward, axisWorld);
                    Vector3 targetProj = Vector3.ProjectOnPlane(targetForward, axisWorld);
                    if (tipProj.sqrMagnitude < 1e-8f || targetProj.sqrMagnitude < 1e-8f)
                        continue;

                    float delta = Vector3.SignedAngle(tipProj, targetProj, axisWorld);
                    delta = Mathf.Clamp(delta, -MaxCcdStepDegrees, MaxCcdStepDegrees);
                    angles[j] = arm.ClampAngle(
                        j, RobotArmAnim.NormalizeSignedAngle(angles[j] + delta));
                    s_fk.UpdateFrom(angles, j);
                }
            }
        }

        public static float JointTravelCost(float[] from, float[] to, int jointCount = -1)
        {
            float cost = 0f;
            int n = Mathf.Min(
                from != null ? from.Length : 0,
                to != null ? to.Length : 0);
            if (jointCount >= 0)
                n = Mathf.Min(n, jointCount);
            for (int i = 0; i < n; i++)
                cost += Mathf.Abs(Mathf.DeltaAngle(from[i], to[i]));
            return cost;
        }

        /// <summary>
        /// 相对 seed 的构型代价：关节行程 + 肘下压/腕翻转惩罚，
        /// 使近点优先选肘抬起的自然折向，而非行程略短的折臂向下解。
    /// </summary>
        public static float ConfigurationCost(RobotArmAnim arm, float[] seed, float[] angles)
        {
            int n = arm != null ? arm.JointCount : 0;
            float cost = JointTravelCost(seed, angles, n);
            if (!IsReady(arm, angles))
                return cost;

            s_fk.Bind(arm);
            s_fk.Evaluate(angles);
            return cost + ConfigurationPenaltyFromChain(arm, angles);
        }

        private static float ConfigurationCostFromChain(RobotArmAnim arm, float[] seed, float[] angles)
        {
            return JointTravelCost(seed, angles, arm.JointCount) +
                   ConfigurationPenaltyFromChain(arm, angles);
        }

        private static float ConfigurationPenaltyFromChain(RobotArmAnim arm, float[] angles)
        {
            float cost = 0f;
            int n = arm.JointCount;
            if (s_fk.Count >= 3)
            {
                float elbowDrop = s_fk.Origins[1].y - s_fk.Origins[2].y;
                if (elbowDrop > 0.02f)
                    cost += elbowDrop * ElbowDropPenaltyPerMeter;
            }

            // 腕翻转相关轴：六轴为 J5(index4)；五轴为末腕前一轴；四轴跳过
            if (n >= 5)
            {
                int wristFlipIndex = Mathf.Min(4, n - 2);
                float wrist = Mathf.Abs(RobotArmAnim.NormalizeSignedAngle(angles[wristFlipIndex]));
                if (wrist > WristFlipSoftLimit)
                    cost += (wrist - WristFlipSoftLimit) * WristFlipPenaltyScale;
            }

            return cost;
        }

        private static void RunCcd(
            RobotArmAnim arm,
            Vector3 targetWorld,
            float[] seedAngles,
            float[] outAngles,
            int iterations,
            int minJointIndex = 0,
            int maxJointIndex = -1,
            int endJointIndex = -1)
        {
            int n = arm.JointCount;
            if (maxJointIndex < 0)
                maxJointIndex = n - 1;

            for (int i = 0; i < n; i++)
                outAngles[i] = arm.ClampAngle(i, seedAngles[i]);

            minJointIndex = Mathf.Clamp(minJointIndex, 0, n - 1);
            maxJointIndex = Mathf.Clamp(maxJointIndex, minJointIndex, n - 1);

            float tolSqr = arm.IkPositionTolerance * arm.IkPositionTolerance;
            s_fk.Evaluate(outAngles);

            for (int iter = 0; iter < iterations; iter++)
            {
                Vector3 currentEnd = s_fk.EndPosition(endJointIndex);
                if ((targetWorld - currentEnd).sqrMagnitude <= tolSqr)
                    return;

                for (int j = maxJointIndex; j >= minJointIndex; j--)
                {
                    Vector3 jointPos = s_fk.Origins[j];
                    currentEnd = s_fk.EndPosition(endJointIndex);
                    Vector3 toTip = currentEnd - jointPos;
                    Vector3 toTarget = targetWorld - jointPos;
                    if (toTip.sqrMagnitude < 1e-10f || toTarget.sqrMagnitude < 1e-10f)
                        continue;

                    Vector3 axisWorld = s_fk.AxesWorld[j];
                    Vector3 toTipProj = Vector3.ProjectOnPlane(toTip, axisWorld);
                    Vector3 toTargetProj = Vector3.ProjectOnPlane(toTarget, axisWorld);
                    if (toTipProj.sqrMagnitude < 1e-10f || toTargetProj.sqrMagnitude < 1e-10f)
                        continue;

                    float delta = Vector3.SignedAngle(toTipProj, toTargetProj, axisWorld);
                    if (Mathf.Abs(delta) < 1e-4f)
                        continue;

                    delta = Mathf.Clamp(delta, -MaxCcdStepDegrees, MaxCcdStepDegrees);
                    outAngles[j] = arm.ClampAngle(
                        j, RobotArmAnim.NormalizeSignedAngle(outAngles[j] + delta));
                    s_fk.UpdateFrom(outAngles, j);

                    if ((targetWorld - s_fk.EndPosition(endJointIndex)).sqrMagnitude <= tolSqr)
                        return;
                }
            }
        }

        /// <summary>把各轴角折到相对 seed 的最短差，便于后续 LerpAngle 连续。</summary>
        private static void AlignAnglesToSeed(float[] seed, float[] angles, RobotArmAnim arm)
        {
            int n = arm.JointCount;
            for (int i = 0; i < n; i++)
            {
                float aligned = seed[i] + Mathf.DeltaAngle(seed[i], angles[i]);
                angles[i] = arm.ClampAngle(i, RobotArmAnim.NormalizeSignedAngle(aligned));
            }
        }

        private static void CopyAngles(float[] src, float[] dst, int count = -1)
        {
            int n = count >= 0
                ? Mathf.Min(count, Mathf.Min(src.Length, dst.Length))
                : Mathf.Min(src.Length, dst.Length);
            for (int i = 0; i < n; i++)
                dst[i] = src[i];
        }

        private static void EnsureAngles(ref float[] buffer, int n)
        {
            if (buffer == null || buffer.Length < n)
                buffer = new float[n];
        }

        /// <summary>
        /// 当前姿态诊断文本：FK 连杆、场景层级连杆、朝下夹角、限位与 TCP 误差。
    /// </summary>
        public static string BuildPoseDebugReport(RobotArmAnim arm)
        {
            var sb = new StringBuilder(4096);
            sb.AppendLine("======== RobotArm 姿态诊断 ========");
            if (arm == null)
            {
                sb.AppendLine("arm = null");
                return sb.ToString();
            }

            sb.AppendLine($"对象: {arm.name}");
            sb.AppendLine($"KeepTipDown={arm.KeepTipDown}  DownWorld={FmtVec(arm.DownWorldAxis)}");
            sb.AppendLine(
                $"合格点积={TipDownAcceptDot:F6}（夹角≤{AngleFromDot(TipDownAcceptDot):F2}°）");
            sb.AppendLine($"根位置={FmtVec(arm.transform.position)}  根欧拉={FmtVec(arm.transform.eulerAngles)}");

            int n = arm.JointCount;
            if (n <= 0)
            {
                sb.AppendLine("JointCount=0，无法继续。");
                sb.Append("======== 结束 ========");
                return sb.ToString();
            }

            var angles = new float[n];
            arm.ReadCurrentAngles(angles);
            s_fk.Bind(arm);
            s_fk.Evaluate(angles);

            Vector3 down = arm.DownWorldAxis;
            int frame = Mathf.Clamp(arm.TipDownJointIndex, 0, n - 1);

            Vector3 fkTipDir = TipDownDirectionWorld(arm, angles);
            float fkTipDot = Vector3.Dot(fkTipDir, down);
            float fkTipAng = AngleFromDot(fkTipDot);

            Vector3 fkLinkDir = s_fk.WorldRot[frame] * arm.TipDownAxisLocal;
            if (fkLinkDir.sqrMagnitude > 1e-12f)
                fkLinkDir.Normalize();
            float fkLinkDot = Vector3.Dot(fkLinkDir, down);
            float fkLinkAng = AngleFromDot(fkLinkDot);

            Transform j5Point = arm.GetAxisPoint(frame);
            Transform j5Child = arm.GetLinkChild(frame);
            Transform j5Drive = arm.GetDriveJoint(frame);
            Vector3 sceneAxisLink = Vector3.zero;
            float sceneAxisAng = float.NaN;
            if (j5Point != null && j5Child != null)
            {
                sceneAxisLink = j5Child.position - j5Point.position;
                if (sceneAxisLink.sqrMagnitude > 1e-12f)
                {
                    sceneAxisLink.Normalize();
                    sceneAxisAng = Vector3.Angle(sceneAxisLink, down);
                }
            }

            Vector3 sceneDriveDir = Vector3.zero;
            float sceneDriveAng = float.NaN;
            if (j5Drive != null)
            {
                sceneDriveDir = j5Drive.TransformDirection(arm.TipDownAxisLocal);
                if (sceneDriveDir.sqrMagnitude > 1e-12f)
                {
                    sceneDriveDir.Normalize();
                    sceneDriveAng = Vector3.Angle(sceneDriveDir, down);
                }
            }

            Vector3 sceneTipDir = Vector3.zero;
            float sceneTipAng = float.NaN;
            if (j5Point != null && arm.ToolTip != null)
            {
                sceneTipDir = arm.ToolTip.position - j5Point.position;
                if (sceneTipDir.sqrMagnitude > 1e-12f)
                {
                    sceneTipDir.Normalize();
                    sceneTipAng = Vector3.Angle(sceneTipDir, down);
                }
            }

            sb.AppendLine();
            sb.AppendLine("【朝下】IK 约束 = 第 5 轴原点 → TCP（末端朝下）");
            sb.AppendLine(
                $"  FK J5→TCP = {FmtVec(fkTipDir)}  点积={fkTipDot:F6}  夹角={fkTipAng:F3}°  " +
                $"合格={(fkTipDot >= TipDownAcceptDot ? "YES" : "NO")}");
            sb.AppendLine(
                $"  场景 轴点J5→TCP = {FmtVec(sceneTipDir)}  夹角={FmtDeg(sceneTipAng)}");
            sb.AppendLine();
            sb.AppendLine("【参考】第 5 轴→子节点连杆（非 IK 约束，J6 偏置时与 TCP 可差几度）");
            sb.AppendLine(
                $"  FK WorldRot[J5]*LinkDir = {FmtVec(fkLinkDir)}  点积={fkLinkDot:F6}  夹角={fkLinkAng:F3}°");
            sb.AppendLine(
                $"  场景 轴点J5→子节点 = {FmtVec(sceneAxisLink)}  夹角={FmtDeg(sceneAxisAng)}");
            sb.AppendLine(
                $"  场景 驱动.TransformDirection(LinkDir) = {FmtVec(sceneDriveDir)}  夹角={FmtDeg(sceneDriveAng)}");
            if (!float.IsNaN(sceneTipAng) && Mathf.Abs(sceneTipAng - fkTipAng) > 0.5f)
            {
                sb.AppendLine(
                    "  提示: FK 与场景 J5→TCP 夹角不一致（>0.5°）→ Rest / 驱动关节 / ToolTip 绑定可能和层级对不上。");
            }
            else if (fkTipDot < TipDownAcceptDot)
            {
                sb.AppendLine(
                    "  提示: FK J5→TCP 未对准朝下 → IK 未锁住末端（限位或构型）。");
            }
            else if (!float.IsNaN(sceneTipAng) && sceneTipAng > AngleFromDot(TipDownAcceptDot) + 0.5f)
            {
                sb.AppendLine(
                    "  提示: 场景 TCP 仍偏 → 检查 ToolTip 是否绑在 FK 末端、或 J6 是否在求解后被改写。");
            }

            Vector3 sceneTip = arm.ToolTip != null ? arm.ToolTip.position : Vector3.zero;
            sb.AppendLine();
            sb.AppendLine("【TCP】");
            sb.AppendLine($"  FK Tip={FmtVec(s_fk.Tip)}");
            if (arm.ToolTip != null)
            {
                sb.AppendLine($"  场景 Tip={FmtVec(sceneTip)}  FK差={Vector3.Distance(s_fk.Tip, sceneTip):F4} m");
            }

            if (arm.IkTarget != null)
            {
                sb.AppendLine(
                    $"  IK目标={FmtVec(arm.IkTarget.position)}  " +
                    $"FK到目标={Vector3.Distance(s_fk.Tip, arm.IkTarget.position):F4} m  " +
                    $"容差={arm.IkPositionTolerance:F4}");
            }

            sb.AppendLine();
            sb.AppendLine("【关节】角 / 限位 / 轴 / 连杆（FK vs 场景）");
            float[] home = arm.HomeAngles;
            for (int i = 0; i < n; i++)
            {
                arm.GetJointLimits(i, out float jMin, out float jMax);
                float ang = angles[i];
                float roomMin = ang - jMin;
                float roomMax = jMax - ang;
                string limitTag = "";
                if (roomMin <= 0.51f)
                    limitTag = " [撞下限]";
                else if (roomMax <= 0.51f)
                    limitTag = " [撞上限]";

                Transform axis = arm.GetAxisPoint(i);
                Transform drive = arm.GetDriveJoint(i);
                Transform child = arm.GetLinkChild(i);
                bool driveDiff = axis != null && drive != null && axis != drive;

                Vector3 fkLink = Vector3.zero;
                if (i + 1 < n)
                    fkLink = s_fk.Origins[i + 1] - s_fk.Origins[i];
                else
                    fkLink = s_fk.Tip - s_fk.Origins[i];

                Vector3 sceneLink = Vector3.zero;
                float sceneLen = 0f;
                if (axis != null && child != null)
                {
                    sceneLink = child.position - axis.position;
                    sceneLen = sceneLink.magnitude;
                }

                Vector3 driveLink = Vector3.zero;
                if (drive != null && child != null)
                    driveLink = child.position - drive.position;

                float homeAng = home != null && i < home.Length ? home[i] : 0f;
                Quaternion rest = arm.GetRestLocalRotation(i);

                sb.AppendLine(
                    $"  J{i + 1} 角={ang,8:F3}°  Home={homeAng,8:F3}°  限位=[{jMin:F1},{jMax:F1}]  " +
                    $"余量=[{roomMin:F2},{roomMax:F2}]{limitTag}");
                sb.AppendLine(
                    $"      轴点={(axis != null ? axis.name : "-")}  " +
                    $"驱动={(drive != null ? drive.name : "-")}" +
                    (driveDiff ? "  (驱动≠轴点)" : "") +
                    $"  Rest欧拉={FmtVec(rest.eulerAngles)}");
                sb.AppendLine(
                    $"      旋转轴 local={FmtVec(arm.GetAxisLocal(i))}  " +
                    $"FK world={FmtVec(s_fk.AxesWorld[i])}  " +
                    $"与朝下夹角={Vector3.Angle(s_fk.AxesWorld[i], down):F2}°");
                sb.AppendLine(
                    $"      配置连杆 L={arm.GetLinkLength(i):F4}  dirLocal={FmtVec(arm.GetLinkDirectionLocal(i))}");
                sb.AppendLine(
                    $"      FK原点={FmtVec(s_fk.Origins[i])}  FK连杆={FmtVec(fkLink)}  |L|={fkLink.magnitude:F4}");
                sb.AppendLine(
                    $"      场景轴点→子 = {FmtVec(sceneLink)}  |L|={sceneLen:F4}");
                if (driveDiff)
                    sb.AppendLine($"      场景驱动→子 = {FmtVec(driveLink)}  |L|={driveLink.magnitude:F4}");
            }

            sb.AppendLine();
            sb.AppendLine($"HasRest={arm.HasRestPose}  HasHome={arm.HasHome}  ToolOffset={FmtVec(arm.ToolOffsetLocal)}");
            sb.Append("======== 结束 ========");
            return sb.ToString();
        }

        private static string FmtVec(Vector3 v)
        {
            return $"({v.x:F4}, {v.y:F4}, {v.z:F4})";
        }

        private static string FmtDeg(float deg)
        {
            return float.IsNaN(deg) ? "n/a" : $"{deg:F3}°";
        }

        private static float AngleFromDot(float dot)
        {
            return Mathf.Acos(Mathf.Clamp(dot, -1f, 1f)) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// 主线程 FK 缓存：一次 Bind 后增量更新，避免 CCD 内每关节整链重算与数组分配。
    /// </summary>
        private sealed class FkChain
        {
            public int Count;
            public Vector3[] Origins = System.Array.Empty<Vector3>();
            public Vector3[] AxesWorld = System.Array.Empty<Vector3>();
            public Quaternion[] WorldRot = System.Array.Empty<Quaternion>();
            public Vector3 Tip;
            public Quaternion TipRot;

            private Vector3[] m_axisLocal = System.Array.Empty<Vector3>();
            private Vector3[] m_linkLocal = System.Array.Empty<Vector3>();
            private Quaternion[] m_restLocal = System.Array.Empty<Quaternion>();
            private Vector3 m_basePos;
            private Quaternion m_baseRot;
            private Vector3 m_toolOffset;
            private RobotArmAnim m_arm;

            public void Bind(RobotArmAnim arm)
            {
                m_arm = arm;
                int n = arm.JointCount;
                Count = n;
                Ensure(n);
                m_basePos = arm.transform.position;
                m_baseRot = arm.transform.rotation;
                m_toolOffset = arm.ToolOffsetLocal;
                arm.EnsureRestPose();
                for (int i = 0; i < n; i++)
                {
                    m_axisLocal[i] = arm.GetAxisLocal(i);
                    m_linkLocal[i] = arm.GetLinkDirectionLocal(i) * arm.GetLinkLength(i);
                    m_restLocal[i] = arm.GetRestLocalRotation(i);
                }
            }

            public void Evaluate(float[] angles)
            {
                int n = Count;
                Quaternion rot = m_baseRot;
                Vector3 pos = m_basePos;
                for (int i = 0; i < n; i++)
                {
                    Origins[i] = pos;
                    Quaternion jointFrame = rot * m_restLocal[i];
                    AxesWorld[i] = jointFrame * m_axisLocal[i];
                    rot = jointFrame * Quaternion.AngleAxis(
                        m_arm.ClampAngle(i, angles[i]), m_axisLocal[i]);
                    WorldRot[i] = rot;
                    pos += rot * m_linkLocal[i];
                }

                Tip = pos + rot * m_toolOffset;
                TipRot = rot;
            }

            public void UpdateFrom(float[] angles, int jointIndex)
            {
                int n = Count;
                Quaternion rot = jointIndex == 0 ? m_baseRot : WorldRot[jointIndex - 1];
                Vector3 pos = Origins[jointIndex];

                Quaternion jointFrame = rot * m_restLocal[jointIndex];
                AxesWorld[jointIndex] = jointFrame * m_axisLocal[jointIndex];
                rot = jointFrame * Quaternion.AngleAxis(
                    m_arm.ClampAngle(jointIndex, angles[jointIndex]), m_axisLocal[jointIndex]);
                WorldRot[jointIndex] = rot;
                pos += rot * m_linkLocal[jointIndex];

                for (int i = jointIndex + 1; i < n; i++)
                {
                    Origins[i] = pos;
                    jointFrame = rot * m_restLocal[i];
                    AxesWorld[i] = jointFrame * m_axisLocal[i];
                    rot = jointFrame * Quaternion.AngleAxis(
                        m_arm.ClampAngle(i, angles[i]), m_axisLocal[i]);
                    WorldRot[i] = rot;
                    pos += rot * m_linkLocal[i];
                }

                Tip = pos + rot * m_toolOffset;
                TipRot = rot;
            }

            public Vector3 EndPosition(int jointIndex)
            {
                if (jointIndex < 0 || jointIndex >= Count)
                    return Tip;
                return Origins[jointIndex];
            }

            public void CopyOrigins(Vector3[] dst)
            {
                int n = Mathf.Min(Count, dst != null ? dst.Length : 0);
                for (int i = 0; i < n; i++)
                    dst[i] = Origins[i];
            }

            private void Ensure(int n)
            {
                if (Origins.Length >= n)
                    return;
                Origins = new Vector3[n];
                AxesWorld = new Vector3[n];
                WorldRot = new Quaternion[n];
                m_axisLocal = new Vector3[n];
                m_linkLocal = new Vector3[n];
                m_restLocal = new Quaternion[n];
            }
        }
    }
}
