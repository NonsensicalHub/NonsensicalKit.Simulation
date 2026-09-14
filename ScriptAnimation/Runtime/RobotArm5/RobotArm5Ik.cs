using System.Text;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 五轴专用 FK / IK。构型：J1 绕竖直轴，J2/J3/J4 互相平行且始终水平，J5 扭转。
    /// 朝下：保持 J5 在目标正上方（J5→TCP 对齐世界下方）；俯仰由 J4 一次转到（水平轴 ⊥ 下方）。
    /// </summary>
    public static class RobotArm5Ik
    {
        private const float MaxCcdStepDegrees = 22f;
        private const float J1SweepStep = 30f;
        private const int J1SweepCount = 6;

        public const float TipDownAcceptDot = 0.9999f;
        private const float TipDownDoneDot = 0.999995f;
        private const float ElbowDropPenaltyPerMeter = 900f;

        private static readonly FkChain s_fk = new FkChain();
        private static float[] s_seed;
        private static float[] s_trialSeed;
        private static float[] s_trialOut;
        private static float[] s_best;

        private static bool IsReady(RobotArm5Anim arm, float[] angles)
        {
            return arm != null &&
                   arm.JointCount >= RobotArm5Anim.MinJointCount &&
                   angles != null &&
                   angles.Length >= arm.JointCount;
        }

        public static void Forward(
            RobotArm5Anim arm,
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

        public static void ForwardPositions(
            RobotArm5Anim arm,
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

        public static bool SolvePosition(
            RobotArm5Anim arm,
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
                float cost = JointTravelCost(s_seed, s_trialOut, n) +
                             ElbowDropPenalty(arm);
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

            CopyAngles(s_best, outAngles, n);
            AlignAnglesToSeed(s_seed, outAngles, arm);
            s_fk.Evaluate(outAngles);
            return (targetWorld - s_fk.Tip).sqrMagnitude <= acceptSqr;
        }

        public static bool SolveToTarget(
            RobotArm5Anim arm,
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
                // 保留最近解
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

        public static Vector3 TipDownDirectionWorld(RobotArm5Anim arm, float[] angles)
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

        public static float TipAxisDot(RobotArm5Anim arm, float[] angles, Vector3 worldAxis)
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
            RobotArm5Anim arm,
            float[] angles,
            Vector3 worldAxis,
            float minDot = TipDownAcceptDot)
        {
            return TipAxisDot(arm, angles, worldAxis) >= minDot;
        }

        /// <summary>
        /// 硬锁朝下：保持 J5→TCP 对齐世界下方（J5 在目标正上方）。
    /// 俯仰只动 J4（水平轴一次转到）；J5 扭转角保持不变。
    /// </summary>
        public static void ForceTipDown(
            RobotArm5Anim arm,
            float[] angles,
            Vector3 downWorldAxis = default)
        {
            if (!IsReady(arm, angles))
                return;

            if (downWorldAxis.sqrMagnitude < 1e-8f)
                downWorldAxis = arm.DownWorldAxis;
            else
                downWorldAxis.Normalize();

            int frame = Mathf.Clamp(arm.TipDownJointIndex, 0, arm.JointCount - 1);
            int pitch = Mathf.Clamp(arm.WristStartIndex, 0, frame);
            float savedJ5 = angles[RobotArm5Anim.J5Index];
            Vector3 axisLocal = TipDownConstraintLocal(arm, angles);
            SnapWristPitchTipDown(arm, axisLocal, downWorldAxis, angles, frame, pitch);

            if (!IsTipAxisAligned(arm, angles, downWorldAxis, TipDownAcceptDot))
                PolishTipAxis(arm, axisLocal, downWorldAxis, angles, frame, 1);

            // J5 仅扭转，朝下求解后写回，避免被 Refine 改写
            angles[RobotArm5Anim.J5Index] = arm.ClampAngle(RobotArm5Anim.J5Index, savedJ5);
        }

        private static bool SolveKeepTipDown(
            RobotArm5Anim arm,
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
            int pitch = Mathf.Clamp(arm.WristStartIndex, 0, frame);
            int armMax = Mathf.Clamp(arm.ArmJointCount - 1, 0, Mathf.Max(0, pitch - 1));
            float acceptSqr = arm.IkPositionTolerance * arm.IkPositionTolerance * 4f;

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

            Vector3 tipDownLocal = TipDownConstraintLocal(arm, s_seed);
            s_fk.Bind(arm);
            s_fk.Evaluate(s_seed);
            Vector3 localDistal =
                Quaternion.Inverse(s_fk.WorldRot[frame]) * (s_fk.Tip - s_fk.Origins[frame]);
            // 腕心 = 目标正上方：J5 原点 = target − down × (J5→TCP 长度)
            Vector3 wristTarget =
                targetWorld - AlignedDistalWorld(localDistal, tipDownLocal, downWorldAxis);

            CopyAngles(s_seed, outAngles, n);
            TryReachWrist(
                arm, wristTarget, targetWorld, s_seed, outAngles,
                tipDownLocal, downWorldAxis, frame, pitch, armMax, acceptSqr);

            for (int round = 0; round < 4; round++)
            {
                s_fk.Bind(arm);
                s_fk.Evaluate(outAngles);
                Vector3 distal = s_fk.Tip - s_fk.Origins[frame];
                SolvePositionToJointOrigin(
                    arm, targetWorld - distal, outAngles, outAngles, frame, armMax);
                ForceTipDown(arm, outAngles, downWorldAxis);
                PreserveJointsAfter(outAngles, s_seed, frame, n);

                s_fk.Evaluate(outAngles);
                bool posOk = (targetWorld - s_fk.Tip).sqrMagnitude <= acceptSqr;
                bool oriOk = IsTipAxisAligned(arm, outAngles, downWorldAxis);
                if (posOk && oriOk)
                    break;
            }

            AlignAnglesToSeed(s_seed, outAngles, arm);
            return IsTipAxisAligned(arm, outAngles, downWorldAxis);
        }

        private static bool TryReachWrist(
            RobotArm5Anim arm,
            Vector3 wristTarget,
            Vector3 tipTarget,
            float[] seedAngles,
            float[] outAngles,
            Vector3 tipDownLocal,
            Vector3 downWorldAxis,
            int frame,
            int pitch,
            int armMax,
            float acceptSqr)
        {
            int n = arm.JointCount;
            EnsureAngles(ref s_trialSeed, n);
            EnsureAngles(ref s_trialOut, n);
            EnsureAngles(ref s_best, n);

            float bestErrSqr = float.PositiveInfinity;
            float bestDot = -2f;
            bool found = false;

            void Consider(float[] startSeed)
            {
                CopyAngles(startSeed, s_trialOut, n);
                TryAnalyticalArmToWrist(arm, wristTarget, s_trialOut);
                SolvePositionToJointOrigin(
                    arm, wristTarget, s_trialOut, s_trialOut, frame, armMax);
                SnapWristPitchTipDown(arm, tipDownLocal, downWorldAxis, s_trialOut, frame, pitch);
                PreserveJointsAfter(s_trialOut, startSeed, frame, n);

                s_fk.Bind(arm);
                s_fk.Evaluate(s_trialOut);
                float errSqr = (tipTarget - s_fk.Tip).sqrMagnitude;
                float dot = Vector3.Dot(s_fk.WorldRot[frame] * tipDownLocal, downWorldAxis);
                bool ok = errSqr <= acceptSqr && dot >= TipDownAcceptDot;

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

            CopyAngles(seedAngles, s_trialSeed, n);
            s_trialSeed[1] = arm.ClampAngle(1, 70f);
            s_trialSeed[2] = arm.ClampAngle(2, 80f);
            Consider(s_trialSeed);
            // J4 俯仰种子：从 ±90° 起步，避免从 0° 翻到限位
            s_trialSeed[pitch] = arm.ClampAngle(pitch, 90f);
            Consider(s_trialSeed);
            s_trialSeed[pitch] = arm.ClampAngle(pitch, -90f);
            Consider(s_trialSeed);

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

            CopyAngles(s_best, outAngles, n);
            return found;
        }

        /// <summary>
        /// J1 把臂平面对准腕心水平方位；J2/J3 平面二连杆把 J5 原点送到目标正上方。
    /// 失败时保留当前角，交给后续 CCD。
    /// </summary>
        private static void TryAnalyticalArmToWrist(
            RobotArm5Anim arm, Vector3 wristTarget, float[] angles)
        {
            int n = arm.JointCount;
            if (n < 4)
                return;

            s_fk.Bind(arm);
            s_fk.Evaluate(angles);

            Vector3 j2Origin = s_fk.Origins[1];
            Vector3 radial = wristTarget - j2Origin;
            radial.y = 0f;
            if (radial.sqrMagnitude < 1e-8f)
                return;

            Vector3 desiredAxis = Vector3.Cross(Vector3.up, radial);
            if (desiredAxis.sqrMagnitude < 1e-8f)
                return;
            desiredAxis.Normalize();

            Vector3 currentAxis = s_fk.AxesWorld[1];
            currentAxis.y = 0f;
            if (currentAxis.sqrMagnitude < 1e-8f)
                return;
            currentAxis.Normalize();

            float j1Delta = Vector3.SignedAngle(currentAxis, desiredAxis, Vector3.up);
            angles[0] = arm.ClampAngle(0, RobotArm5Anim.NormalizeSignedAngle(angles[0] + j1Delta));
            s_fk.Evaluate(angles);

            float l2 = arm.GetLinkLength(1);
            float l3 = arm.GetLinkLength(2);
            if (l2 < 1e-5f || l3 < 1e-5f)
                return;

            float savedJ2 = angles[1];
            float savedJ3 = angles[2];
            angles[1] = 0f;
            angles[2] = 0f;
            s_fk.Evaluate(angles);

            Vector3 axis = s_fk.AxesWorld[1];
            Vector3 origin = s_fk.Origins[1];
            Vector3 toWrist = Vector3.ProjectOnPlane(wristTarget - origin, axis);
            float d = toWrist.magnitude;
            if (d < 1e-5f)
            {
                angles[1] = savedJ2;
                angles[2] = savedJ3;
                return;
            }

            float maxReach = l2 + l3;
            float minReach = Mathf.Abs(l2 - l3);
            if (d > maxReach - 1e-5f)
                toWrist *= (maxReach - 1e-4f) / d;
            else if (d < minReach + 1e-5f && minReach > 1e-5f)
                toWrist *= (minReach + 1e-4f) / d;
            d = toWrist.magnitude;

            Vector3 restDir = s_fk.WorldRot[1] * arm.GetLinkDirectionLocal(1);
            restDir = Vector3.ProjectOnPlane(restDir, axis);
            if (restDir.sqrMagnitude < 1e-8f)
                restDir = Vector3.ProjectOnPlane(Vector3.up, axis);
            if (restDir.sqrMagnitude < 1e-8f)
            {
                angles[1] = savedJ2;
                angles[2] = savedJ3;
                return;
            }

            restDir.Normalize();
            Vector3 perp = Vector3.Cross(axis, restDir);
            if (perp.sqrMagnitude < 1e-8f)
            {
                angles[1] = savedJ2;
                angles[2] = savedJ3;
                return;
            }

            perp.Normalize();
            float x = Vector3.Dot(toWrist, restDir);
            float y = Vector3.Dot(toWrist, perp);

            float cosE = (d * d - l2 * l2 - l3 * l3) / (2f * l2 * l3);
            cosE = Mathf.Clamp(cosE, -1f, 1f);
            float elbow = Mathf.Acos(cosE) * Mathf.Rad2Deg;
            float seedElbow = RobotArm5Anim.NormalizeSignedAngle(savedJ3);
            float j3 = arm.ClampAngle(
                2,
                Mathf.Abs(seedElbow + elbow) <= Mathf.Abs(seedElbow - elbow) ? -elbow : elbow);
            float j3Rad = j3 * Mathf.Deg2Rad;
            float j2Abs = Mathf.Atan2(y, x) * Mathf.Rad2Deg -
                          Mathf.Atan2(l3 * Mathf.Sin(j3Rad), l2 + l3 * Mathf.Cos(j3Rad)) *
                          Mathf.Rad2Deg;

            angles[1] = arm.ClampAngle(1, RobotArm5Anim.NormalizeSignedAngle(j2Abs));
            angles[2] = j3;
        }

        private static Vector3 TipDownConstraintLocal(RobotArm5Anim arm, float[] angles)
        {
            if (!IsReady(arm, angles))
                return FallbackTipDownLocal(arm);

            s_fk.Bind(arm);
            s_fk.Evaluate(angles);
            int frame = Mathf.Clamp(arm.TipDownJointIndex, 0, arm.JointCount - 1);
            Vector3 world = s_fk.Tip - s_fk.Origins[frame];
            if (world.sqrMagnitude < 1e-12f)
                return FallbackTipDownLocal(arm);
            return (Quaternion.Inverse(s_fk.WorldRot[frame]) * world).normalized;
        }

        private static Vector3 FallbackTipDownLocal(RobotArm5Anim arm)
        {
            Vector3 local = arm.TipDownAxisLocal;
            return local.sqrMagnitude > 1e-8f ? local.normalized : Vector3.up;
        }

        private static void SnapWristPitchTipDown(
            RobotArm5Anim arm,
            Vector3 axisLocal,
            Vector3 downWorldAxis,
            float[] angles,
            int frame,
            int pitch)
        {
            // 约束方向在 J5 帧上读；俯仰只动 J4（水平轴一次转到），不碰 J5 扭转。
            RefineTipAxis(arm, axisLocal, downWorldAxis, angles, 4, pitch, 180f, frame, pitch);
        }

        private static void PolishTipAxis(
            RobotArm5Anim arm,
            Vector3 axisLocal,
            Vector3 downWorldAxis,
            float[] angles,
            int frame,
            int minJointIndex)
        {
            int pitch = Mathf.Clamp(arm.WristStartIndex, 0, frame);
            RefineTipAxis(
                arm, axisLocal, downWorldAxis, angles, 8, minJointIndex, 180f, frame, pitch);
        }

        public static void RefineTipAxis(
            RobotArm5Anim arm,
            Vector3 axisLocal,
            Vector3 worldTargetAxis,
            float[] angles,
            int passCount = 8,
            int minJointIndex = -1,
            float maxStepDegrees = -1f,
            int frameJointIndex = -1,
            int maxJointIndex = -1)
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
            // 朝下俯仰最多动到 J4；J5 扭转不参与
            int maxJoint = maxJointIndex >= 0
                ? Mathf.Clamp(maxJointIndex, 0, frame)
                : Mathf.Min(frame, arm.WristStartIndex);
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
                        j, RobotArm5Anim.NormalizeSignedAngle(angles[j] + delta));
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

        public static void RefineOrientation(
            RobotArm5Anim arm,
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
                        j, RobotArm5Anim.NormalizeSignedAngle(angles[j] + delta));
                    s_fk.UpdateFrom(angles, j);
                }
            }
        }

        private static bool SolvePositionToJointOrigin(
            RobotArm5Anim arm,
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

        private static void PreserveJointsAfter(float[] angles, float[] seed, int startIndex, int count)
        {
            if (angles == null || seed == null)
                return;
            for (int i = startIndex; i < count && i < angles.Length && i < seed.Length; i++)
                angles[i] = seed[i];
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

        private static float ElbowDropPenalty(RobotArm5Anim arm)
        {
            if (s_fk.Count < 3)
                return 0f;
            float elbowDrop = s_fk.Origins[1].y - s_fk.Origins[2].y;
            if (elbowDrop > 0.02f)
                return elbowDrop * ElbowDropPenaltyPerMeter;
            return 0f;
        }

        private static void RunCcd(
            RobotArm5Anim arm,
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
                        j, RobotArm5Anim.NormalizeSignedAngle(outAngles[j] + delta));
                    s_fk.UpdateFrom(outAngles, j);

                    if ((targetWorld - s_fk.EndPosition(endJointIndex)).sqrMagnitude <= tolSqr)
                        return;
                }
            }
        }

        private static void AlignAnglesToSeed(float[] seed, float[] angles, RobotArm5Anim arm)
        {
            int n = arm.JointCount;
            for (int i = 0; i < n; i++)
            {
                float aligned = seed[i] + Mathf.DeltaAngle(seed[i], angles[i]);
                angles[i] = arm.ClampAngle(i, RobotArm5Anim.NormalizeSignedAngle(aligned));
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

        public static string BuildPoseDebugReport(RobotArm5Anim arm)
        {
            var sb = new StringBuilder(2048);
            sb.AppendLine("======== RobotArm5 姿态诊断 ========");
            if (arm == null)
            {
                sb.AppendLine("arm = null");
                return sb.ToString();
            }

            sb.AppendLine($"对象: {arm.name}");
            sb.AppendLine($"KeepTipDown={arm.KeepTipDown}  DownWorld={arm.DownWorldAxis}");
            int n = arm.JointCount;
            if (n <= 0)
            {
                sb.AppendLine("JointCount=0");
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
            float fkTipAng = Mathf.Acos(Mathf.Clamp(fkTipDot, -1f, 1f)) * Mathf.Rad2Deg;

            sb.AppendLine($"朝下约束 = J5 原点 → TCP（轴5在目标正上方）");
            sb.AppendLine(
                $"  FK J5→TCP 点积={fkTipDot:F6}  夹角={fkTipAng:F3}°  " +
                $"合格={(fkTipDot >= TipDownAcceptDot ? "YES" : "NO")}");
            sb.AppendLine($"  FK Tip={s_fk.Tip}");
            if (arm.IkTarget != null)
                sb.AppendLine($"  到目标={Vector3.Distance(s_fk.Tip, arm.IkTarget.position):F4} m");

            for (int i = 0; i < n; i++)
            {
                arm.GetJointLimits(i, out float jMin, out float jMax);
                sb.AppendLine(
                    $"  J{i + 1} 角={angles[i]:F3}°  限位=[{jMin:F1},{jMax:F1}]  " +
                    $"轴 local={arm.GetAxisLocal(i)}  L={arm.GetLinkLength(i):F4}");
            }

            int pitch = Mathf.Clamp(arm.WristStartIndex, 0, n - 1);
            Vector3 j4Axis = s_fk.AxesWorld[pitch];
            sb.AppendLine(
                $"  J4 世界轴={j4Axis}  与水平夹角={90f - Vector3.Angle(j4Axis, Vector3.up):F2}°");
            sb.AppendLine($"  J5 原点={s_fk.Origins[frame]}（应在目标正上方）");
            sb.Append("======== 结束 ========");
            return sb.ToString();
        }

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
            private RobotArm5Anim m_arm;

            public void Bind(RobotArm5Anim arm)
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
