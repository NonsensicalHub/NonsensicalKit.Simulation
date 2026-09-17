using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 潜伏车取放货：时长估算与进度采样（支持 scrub）。
    /// 取货：空载→放置(停顿)→抬起→驶到移动点→载货行驶高度。
    /// 放货：载货行驶→转向移动点→抬起→驶到移动点→放置(停顿)→空载。
    /// </summary>
    public static class LatentAgvSampler
    {
        private struct PhaseTimes
        {
            public float Rotate;
            public float PreLift;
            /// <summary>取货：到达 Place 后、继续抬到 Lift 前的显隐切换停顿。</summary>
            public float HoldBeforeLift;
            public float ActionLift;
            public float Move;
            /// <summary>放货：放到 Place 后、继续下降前的显隐切换停顿。</summary>
            public float HoldBeforeLeave;
            public float PostLift;

            public float Total =>
                Rotate + PreLift + HoldBeforeLift + ActionLift + Move + HoldBeforeLeave + PostLift;
        }

        public static float EstimateDuration(
            LatentAgvAnim anim,
            LatentAgvClipData data,
            Transform movePoint,
            Vector3 homePos,
            Quaternion homeRot,
            ForkliftRotateMode rotateMode = ForkliftRotateMode.Timed)
        {
            if (anim == null || data == null || movePoint == null)
                return -1f;

            return BuildPhases(anim, data, movePoint, homePos, homeRot, rotateMode).Total;
        }

        public static void Sample(
            LatentAgvAnim anim,
            LatentAgvClipData data,
            Transform movePoint,
            Vector3 homePos,
            Quaternion homeRot,
            ForkliftRotateMode rotateMode,
            float normalizedTime)
        {
            if (anim == null || data == null || movePoint == null)
                return;

            bool pickUp = data.Mode == ForkliftMode.PickUp;
            if (pickUp)
                SamplePickUp(anim, data, movePoint, homePos, homeRot, normalizedTime);
            else
                SamplePutDown(anim, data, movePoint, homePos, homeRot, rotateMode, normalizedTime);
        }

        private static void SamplePickUp(
            LatentAgvAnim anim,
            LatentAgvClipData data,
            Transform movePoint,
            Vector3 homePos,
            Quaternion homeRot,
            float normalizedTime)
        {
            PhaseTimes phases = BuildPhases(
                anim, data, movePoint, homePos, homeRot, ForkliftRotateMode.Skip);
            float total = Mathf.Max(0.01f, phases.Total);
            float elapsed = Mathf.Clamp01(normalizedTime) * total;

            ResolvePlatformHeights(
                data, out float startH, out float endH, out float placeH, out float liftH);
            Vector3 targetPos = ResolveMoveTarget(anim, homePos, movePoint.position);

            float cursor = 0f;
            Transform body = anim.Body;
            Quaternion flatHome = anim.FlattenRotation(homeRot, homeRot);

            body.rotation = flatHome;

            // 空载 → 放置高度
            if (TryConsumePhase(elapsed, ref cursor, phases.PreLift, out float uPre))
            {
                body.position = homePos;
                SetPlatformHeight(anim, Mathf.Lerp(startH, placeH, uPre));
                return;
            }

            // 放置高度停顿（货物显隐）
            if (phases.HoldBeforeLift > 0f && elapsed <= cursor + phases.HoldBeforeLift)
            {
                body.position = homePos;
                SetPlatformHeight(anim, placeH);
                return;
            }

            cursor += phases.HoldBeforeLift;

            // 放置 → 抬起高度
            if (TryConsumePhase(elapsed, ref cursor, phases.ActionLift, out float uAct))
            {
                body.position = homePos;
                SetPlatformHeight(anim, Mathf.Lerp(placeH, liftH, uAct));
                return;
            }

            // 驶到移动点（保持抬起高度）
            if (TryConsumePhase(elapsed, ref cursor, phases.Move, out float uMove))
            {
                body.position = Vector3.Lerp(homePos, targetPos, uMove);
                SetPlatformHeight(anim, liftH);
                return;
            }

            cursor += phases.HoldBeforeLeave;

            // 抬起 → 载货行驶高度
            body.position = targetPos;
            float uPost = phases.PostLift > 1e-5f
                ? Mathf.Clamp01((elapsed - cursor) / phases.PostLift)
                : 1f;
            SetPlatformHeight(anim, Mathf.Lerp(liftH, endH, uPost));
        }

        private static void SamplePutDown(
            LatentAgvAnim anim,
            LatentAgvClipData data,
            Transform movePoint,
            Vector3 homePos,
            Quaternion homeRot,
            ForkliftRotateMode rotateMode,
            float normalizedTime)
        {
            PhaseTimes phases = BuildPhases(anim, data, movePoint, homePos, homeRot, rotateMode);
            float total = Mathf.Max(0.01f, phases.Total);
            float elapsed = Mathf.Clamp01(normalizedTime) * total;

            ResolvePlatformHeights(
                data, out float startH, out float endH, out float placeH, out float liftH);
            Vector3 targetPos = ResolveMoveTarget(anim, homePos, movePoint.position);

            float cursor = 0f;
            Transform body = anim.Body;
            Quaternion flatHome = anim.FlattenRotation(homeRot, homeRot);
            Quaternion faceRot = GetFlatFacing(anim, homePos, targetPos, flatHome);
            Quaternion heldRot = rotateMode == ForkliftRotateMode.Skip ? flatHome : faceRot;

            // 旋转朝向移动点（仅 Timed 有时长；Instant/Skip 的 Rotate=0）
            if (rotateMode == ForkliftRotateMode.Timed &&
                TryConsumePhase(elapsed, ref cursor, phases.Rotate, out float uRot))
            {
                body.position = homePos;
                body.rotation = Quaternion.Slerp(flatHome, faceRot, uRot);
                SetPlatformHeight(anim, startH);
                return;
            }

            body.rotation = heldRot;

            // 载货行驶 → 抬起高度
            if (TryConsumePhase(elapsed, ref cursor, phases.PreLift, out float uPre))
            {
                body.position = homePos;
                SetPlatformHeight(anim, Mathf.Lerp(startH, liftH, uPre));
                return;
            }

            cursor += phases.HoldBeforeLift;

            // 驶到移动点（保持抬起高度）
            if (TryConsumePhase(elapsed, ref cursor, phases.Move, out float uMove))
            {
                body.position = Vector3.Lerp(homePos, targetPos, uMove);
                SetPlatformHeight(anim, liftH);
                return;
            }

            // 抬起 → 放置高度
            if (TryConsumePhase(elapsed, ref cursor, phases.ActionLift, out float uAct))
            {
                body.position = targetPos;
                SetPlatformHeight(anim, Mathf.Lerp(liftH, placeH, uAct));
                return;
            }

            // 放置高度停顿（货物显隐）
            if (phases.HoldBeforeLeave > 0f && elapsed <= cursor + phases.HoldBeforeLeave)
            {
                body.position = targetPos;
                SetPlatformHeight(anim, placeH);
                return;
            }

            cursor += phases.HoldBeforeLeave;

            // 放置 → 空载高度
            body.position = targetPos;
            float uPost = phases.PostLift > 1e-5f
                ? Mathf.Clamp01((elapsed - cursor) / phases.PostLift)
                : 1f;
            SetPlatformHeight(anim, Mathf.Lerp(placeH, endH, uPost));
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
            LatentAgvAnim anim,
            LatentAgvClipData data,
            Transform movePoint,
            Vector3 homePos,
            Quaternion homeRot,
            ForkliftRotateMode rotateMode)
        {
            float platformSpeed = DurationUtility.SafeSpeed(data.PlatformSpeed);
            float moveSpeed = DurationUtility.SafeSpeed(data.MoveSpeed);
            float rotSpeed = DurationUtility.SafeSpeed(data.RotateSpeed);

            ResolvePlatformHeights(
                data, out float startH, out float endH, out float placeH, out float liftH);
            Vector3 targetPos = ResolveMoveTarget(anim, homePos, movePoint.position);

            bool pickUp = data.Mode == ForkliftMode.PickUp;
            float hold = DurationUtility.TimeForFrames(data.CargoSwapHoldFrames);
            float moveDist = Vector3.Distance(homePos, targetPos);

            float rotateTime = 0f;
            if (!pickUp && rotateMode == ForkliftRotateMode.Timed)
            {
                Quaternion flatHome = anim.FlattenRotation(homeRot, homeRot);
                Quaternion faceRot = GetFlatFacing(anim, homePos, targetPos, flatHome);
                float angle = Quaternion.Angle(flatHome, faceRot);
                rotateTime = DurationUtility.TimeForAngle(angle, rotSpeed);
            }

            if (pickUp)
            {
                // 空载→放置 → 停顿 → 放置→抬起 → 移动 → 抬起→载货行驶
                return new PhaseTimes
                {
                    Rotate = 0f,
                    PreLift = DurationUtility.TimeForDistance(Mathf.Abs(placeH - startH), platformSpeed),
                    HoldBeforeLift = hold,
                    ActionLift = DurationUtility.TimeForDistance(Mathf.Abs(liftH - placeH), platformSpeed),
                    Move = DurationUtility.TimeForDistance(moveDist, moveSpeed),
                    HoldBeforeLeave = 0f,
                    PostLift = DurationUtility.TimeForDistance(Mathf.Abs(endH - liftH), platformSpeed)
                };
            }

            // 转向 → 载货行驶→抬起 → 移动 → 抬起→放置 → 停顿 → 放置→空载
            return new PhaseTimes
            {
                Rotate = rotateTime,
                PreLift = DurationUtility.TimeForDistance(Mathf.Abs(liftH - startH), platformSpeed),
                HoldBeforeLift = 0f,
                ActionLift = DurationUtility.TimeForDistance(Mathf.Abs(placeH - liftH), platformSpeed),
                Move = DurationUtility.TimeForDistance(moveDist, moveSpeed),
                HoldBeforeLeave = hold,
                PostLift = DurationUtility.TimeForDistance(Mathf.Abs(endH - placeH), platformSpeed)
            };
        }

        /// <summary>
        /// 取货：空载开场、载货行驶结束；放货相反。放置 / 抬起高度按模式共用。
        /// </summary>
        private static void ResolvePlatformHeights(
            LatentAgvClipData data,
            out float startH,
            out float endH,
            out float placeH,
            out float liftH)
        {
            bool pickUp = data.Mode == ForkliftMode.PickUp;
            startH = pickUp ? data.PlatformEmptyHeight : data.PlatformLoadedTravelHeight;
            endH = pickUp ? data.PlatformLoadedTravelHeight : data.PlatformEmptyHeight;
            placeH = data.PlatformPlaceHeight;
            liftH = data.PlatformLiftHeight;
        }

        private static Vector3 ResolveMoveTarget(
            PathMoveActor actor, Vector3 homePos, Vector3 movePointPos)
        {
            return actor.WithUpHeight(movePointPos, homePos);
        }

        private static Quaternion GetFlatFacing(
            PathMoveActor actor, Vector3 from, Vector3 to, Quaternion fallback)
        {
            return actor.LookRotation(
                PathMoveSampler.FacingDirection(to - from, reverseFacing: false), fallback);
        }

        private static void SetPlatformHeight(LatentAgvAnim anim, float height)
        {
            if (anim.Platform == null)
                return;

            Vector3 axis = anim.LiftAxisInParent;
            Vector3 local = anim.Platform.localPosition;
            local -= axis * Vector3.Dot(local, axis);
            local += axis * height;
            anim.Platform.localPosition = local;
        }
    }
}
