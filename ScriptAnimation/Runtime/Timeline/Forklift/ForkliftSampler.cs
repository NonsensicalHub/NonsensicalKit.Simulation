using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>叉车取放货：时长估算与进度采样（支持 scrub）。开场用开始行驶高度，后退后再调到结束行驶高度。</summary>
    public static class ForkliftSampler
    {
        private struct PhaseTimes
        {
            public float Rotate;
            public float PreFork;
            public float Forward;
            /// <summary>取货：到达 Place 后、继续抬到 Lift 前的显隐切换停顿。</summary>
            public float HoldBeforeLift;
            public float ActionFork;
            /// <summary>放货：放到 Place 后、离开或继续下降前的显隐切换停顿。</summary>
            public float HoldBeforeLeave;
            public float Back;
            public float PostFork;

            public float Total =>
                Rotate + PreFork + Forward + HoldBeforeLift + ActionFork + HoldBeforeLeave + Back + PostFork;
        }

        public static float EstimateDuration(
            ForkliftAnim anim,
            ForkliftClipData data,
            Transform station,
            Vector3 homePos,
            Quaternion homeRot,
            ForkliftRotateMode rotateMode = ForkliftRotateMode.Timed)
        {
            if (anim == null || data == null || station == null)
                return -1f;

            return BuildPhases(anim, data, station, homePos, homeRot, rotateMode).Total;
        }

        public static void Sample(
            ForkliftAnim anim,
            ForkliftClipData data,
            Transform station,
            Vector3 homePos,
            Quaternion homeRot,
            ForkliftRotateMode rotateMode,
            float normalizedTime)
        {
            if (anim == null || data == null || station == null)
                return;

            PhaseTimes phases = BuildPhases(anim, data, station, homePos, homeRot, rotateMode);
            float total = Mathf.Max(0.01f, phases.Total);
            float elapsed = Mathf.Clamp01(normalizedTime) * total;

            Vector3 stationPos = station.position + data.DestinationOffset;
            Vector3 approach = ResolveApproach(anim, homePos, stationPos, GetApproach(anim, data));
            ResolveForkHeights(data, out float startTravelH, out float endTravelH, out float preH, out float actionH);

            float cursor = 0f;
            Transform body = anim.Body;

            // 开场朝向压到基类上方前方轴，避免脚pitch/roll 在 Slerp 中把车“放倒—
            Quaternion flatHome = anim.FlattenRotation(homeRot, homeRot);
            // Instant：开场即面朝货点；Skip：保持上一动作朝向；Timed：插值转向
            // ReverseFacing：车头背对货点（倒车驶入），为 PathMove 反向行驶一自
            Quaternion faceRot = GetFlatFacing(
                anim, homePos, stationPos, flatHome, data.ReverseFacing);
            Quaternion heldRot = rotateMode == ForkliftRotateMode.Skip ? flatHome : faceRot;

            // 0 rotate（仅 Timed 有时长；Instant/Skip 的 Rotate=0，直接落到 heldRot）
            if (rotateMode == ForkliftRotateMode.Timed &&
                TryConsumePhase(elapsed, ref cursor, phases.Rotate, out float uRot))
            {
                body.position = homePos;
                body.rotation = Quaternion.Slerp(flatHome, faceRot, uRot);
                SetForkHeight(anim, startTravelH);
                return;
            }

            body.rotation = heldRot;

            // 旋转后先调到插入/载货高度，再前进（取货 Place；放货 Lift）
            if (TryConsumePhase(elapsed, ref cursor, phases.PreFork, out float uPreHome))
            {
                body.position = homePos;
                SetForkHeight(anim, Mathf.Lerp(startTravelH, preH, uPreHome));
                return;
            }

            if (TryConsumePhase(elapsed, ref cursor, phases.Forward, out float uFwd))
            {
                body.position = Vector3.Lerp(homePos, approach, uFwd);
                SetForkHeight(anim, preH);
                return;
            }

            // 取货：到达 Place 尚未继续抬到 Lift
            if (phases.HoldBeforeLift > 0f && elapsed <= cursor + phases.HoldBeforeLift)
            {
                body.position = approach;
                SetForkHeight(anim, preH);
                return;
            }

            cursor += phases.HoldBeforeLift;

            if (TryConsumePhase(elapsed, ref cursor, phases.ActionFork, out float uAct))
            {
                body.position = approach;
                SetForkHeight(anim, Mathf.Lerp(preH, actionH, uAct));
                return;
            }

            // 放货：放到 Place 尚未离开
            if (phases.HoldBeforeLeave > 0f && elapsed <= cursor + phases.HoldBeforeLeave)
            {
                body.position = approach;
                SetForkHeight(anim, actionH);
                return;
            }

            cursor += phases.HoldBeforeLeave;

            if (TryConsumePhase(elapsed, ref cursor, phases.Back, out float uBack))
            {
                body.position = Vector3.Lerp(approach, homePos, uBack);
                SetForkHeight(anim, actionH);
                return;
            }

            body.position = homePos;
            float u = phases.PostFork > 1e-5f
                ? Mathf.Clamp01((elapsed - cursor) / phases.PostFork)
                : 1f;
            SetForkHeight(anim, Mathf.Lerp(actionH, endTravelH, u));
        }

        private static bool TryConsumePhase(float elapsed, ref float cursor, float duration, out float u)
        {
            if (elapsed <= cursor + duration)
            {
                u = duration > 1e-5f ? Mathf.Clamp01((elapsed - cursor) / duration) : 1f;
                return true;
            }

            cursor += duration;
            u = 1f;
            return false;
        }

        private static PhaseTimes BuildPhases(
            ForkliftAnim anim,
            ForkliftClipData data,
            Transform station,
            Vector3 homePos,
            Quaternion homeRot,
            ForkliftRotateMode rotateMode)
        {
            float moveSpeed = DurationUtility.SafeSpeed(data.MoveSpeed);
            float forkSpeed = DurationUtility.SafeSpeed(data.ForkSpeed);
            float rotSpeed = DurationUtility.SafeSpeed(data.RotateSpeed);

            Vector3 stationPos = station.position + data.DestinationOffset;
            Vector3 approach = ResolveApproach(anim, homePos, stationPos, GetApproach(anim, data));
            Quaternion flatHome = anim.FlattenRotation(homeRot, homeRot);
            Quaternion faceRot = GetFlatFacing(
                anim, homePos, stationPos, flatHome, data.ReverseFacing);

            float rotateTime = 0f;
            if (rotateMode == ForkliftRotateMode.Timed)
            {
                float angle = Quaternion.Angle(flatHome, faceRot);
                rotateTime = DurationUtility.TimeForAngle(angle, rotSpeed);
            }
            // Instant / Skip：旋转时长为 0（Instant 在采样时瞬间面朝货点）

            ResolveForkHeights(data, out float startTravelH, out float endTravelH, out float preH, out float actionH);

            bool pickUp = data.Mode == ForkliftMode.PickUp;
            float hold = DurationUtility.TimeForFrames(data.CargoSwapHoldFrames);

            return new PhaseTimes
            {
                Rotate = rotateTime,
                PreFork = DurationUtility.TimeForDistance(Mathf.Abs(preH - startTravelH), forkSpeed),
                Forward = DurationUtility.TimeForDistance(Vector3.Distance(homePos, approach), moveSpeed),
                HoldBeforeLift = pickUp ? hold : 0f,
                ActionFork = DurationUtility.TimeForDistance(Mathf.Abs(actionH - preH), forkSpeed),
                HoldBeforeLeave = pickUp ? 0f : hold,
                Back = DurationUtility.TimeForDistance(Vector3.Distance(approach, homePos), moveSpeed),
                PostFork = DurationUtility.TimeForDistance(Mathf.Abs(endTravelH - actionH), forkSpeed)
            };
        }

        /// <summary>取货 Place→Lift，放货 Lift→Place。开场用开始行驶高度，后退后再调到结束行驶高度。</summary>
        private static void ResolveForkHeights(
            ForkliftClipData data,
            out float startTravelH,
            out float endTravelH,
            out float preH,
            out float actionH)
        {
            startTravelH = data.ForkStartTravelHeight;
            endTravelH = data.ForkEndTravelHeight;
            bool pickUp = data.Mode == ForkliftMode.PickUp;
            preH = pickUp ? data.ForkPlaceHeight : data.ForkLiftHeight;
            actionH = pickUp ? data.ForkLiftHeight : data.ForkPlaceHeight;
        }

        private static float GetApproach(ForkliftAnim anim, ForkliftClipData data)
        {
            _ = anim;
            return Mathf.Max(0f, data.ApproachDistance);
        }

        private static Vector3 ResolveApproach(
            PathMoveActor actor, Vector3 startPos, Vector3 stationPos, float approachDistance)
        {
            Vector3 flat = actor.Flatten(stationPos - startPos);
            float dist = flat.magnitude;
            Vector3 approach = stationPos;
            if (dist > 1e-4f && approachDistance > 0f)
            {
                float stopDist = Mathf.Min(approachDistance, Mathf.Max(0f, dist - 0.01f));
                approach = stationPos - flat.normalized * stopDist;
            }

            return actor.WithUpHeight(approach, startPos);
        }

        private static Quaternion GetFlatFacing(
            PathMoveActor actor, Vector3 from, Vector3 to, Quaternion fallback,
            bool reverseFacing = false)
        {
            Vector3 moveDir = to - from;
            return actor.LookRotation(
                PathMoveSampler.FacingDirection(moveDir, reverseFacing), fallback);
        }

        private static void SetForkHeight(ForkliftAnim anim, float height)
        {
            if (anim.Fork == null)
                return;

            // LiftAxisLocal 是货叉节点本地轴，需变到父空间再写 localPosition
            Vector3 axis = anim.LiftAxisInParent;
            Vector3 local = anim.Fork.localPosition;
            local -= axis * Vector3.Dot(local, axis);
            local += axis * height;
            anim.Fork.localPosition = local;
        }
    }
}
