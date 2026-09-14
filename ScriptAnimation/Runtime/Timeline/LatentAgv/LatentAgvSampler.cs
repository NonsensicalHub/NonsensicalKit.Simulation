using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 潜伏车取放货：时长估算与进度采样（支持 scrub）。
    /// 车体已在货下方，只升降平台：开始行驶 → 动作高度 → 结束行驶。
    /// </summary>
    public static class LatentAgvSampler
    {
        private struct PhaseTimes
        {
            public float PreLift;
            /// <summary>取货：到达 Place 后、继续抬到 Lift 前的显隐切换停顿。</summary>
            public float HoldBeforeLift;
            public float ActionLift;
            /// <summary>放货：放到 Place 后、继续下降前的显隐切换停顿。</summary>
            public float HoldBeforeLeave;
            public float PostLift;

            public float Total =>
                PreLift + HoldBeforeLift + ActionLift + HoldBeforeLeave + PostLift;
        }

        public static float EstimateDuration(
            LatentAgvAnim anim,
            LatentAgvClipData data)
        {
            if (anim == null || data == null)
                return -1f;

            return BuildPhases(anim, data).Total;
        }

        public static void Sample(
            LatentAgvAnim anim,
            LatentAgvClipData data,
            Vector3 homePos,
            Quaternion homeRot,
            float normalizedTime)
        {
            if (anim == null || data == null)
                return;

            PhaseTimes phases = BuildPhases(anim, data);
            float total = Mathf.Max(0.01f, phases.Total);
            float elapsed = Mathf.Clamp01(normalizedTime) * total;

            ResolvePlatformHeights(
                data, out float startTravelH, out float endTravelH, out float preH, out float actionH);

            float cursor = 0f;
            Transform body = anim.Body;
            Quaternion flatHome = anim.FlattenRotation(homeRot, homeRot);

            body.position = homePos;
            body.rotation = flatHome;

            if (TryConsumePhase(elapsed, ref cursor, phases.PreLift, out float uPre))
            {
                SetPlatformHeight(anim, Mathf.Lerp(startTravelH, preH, uPre));
                return;
            }

            if (phases.HoldBeforeLift > 0f && elapsed <= cursor + phases.HoldBeforeLift)
            {
                SetPlatformHeight(anim, preH);
                return;
            }

            cursor += phases.HoldBeforeLift;

            if (TryConsumePhase(elapsed, ref cursor, phases.ActionLift, out float uAct))
            {
                SetPlatformHeight(anim, Mathf.Lerp(preH, actionH, uAct));
                return;
            }

            if (phases.HoldBeforeLeave > 0f && elapsed <= cursor + phases.HoldBeforeLeave)
            {
                SetPlatformHeight(anim, actionH);
                return;
            }

            cursor += phases.HoldBeforeLeave;

            float uPost = phases.PostLift > 1e-5f
                ? Mathf.Clamp01((elapsed - cursor) / phases.PostLift)
                : 1f;
            SetPlatformHeight(anim, Mathf.Lerp(actionH, endTravelH, uPost));
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

        private static PhaseTimes BuildPhases(LatentAgvAnim anim, LatentAgvClipData data)
        {
            float platformSpeed = DurationUtility.SafeSpeed(data.PlatformSpeed);

            ResolvePlatformHeights(
                data, out float startTravelH, out float endTravelH, out float preH, out float actionH);

            bool pickUp = data.Mode == ForkliftMode.PickUp;
            float hold = DurationUtility.TimeForFrames(data.CargoSwapHoldFrames);

            return new PhaseTimes
            {
                PreLift = DurationUtility.TimeForDistance(Mathf.Abs(preH - startTravelH), platformSpeed),
                HoldBeforeLift = pickUp ? hold : 0f,
                ActionLift = DurationUtility.TimeForDistance(Mathf.Abs(actionH - preH), platformSpeed),
                HoldBeforeLeave = pickUp ? 0f : hold,
                PostLift = DurationUtility.TimeForDistance(Mathf.Abs(endTravelH - actionH), platformSpeed)
            };
        }

        /// <summary>取货 Place→Lift，放货 Lift→Place。开场用开始行驶高度，结束后回到结束行驶高度。</summary>
        private static void ResolvePlatformHeights(
            LatentAgvClipData data,
            out float startTravelH,
            out float endTravelH,
            out float preH,
            out float actionH)
        {
            startTravelH = data.PlatformStartTravelHeight;
            endTravelH = data.PlatformEndTravelHeight;
            bool pickUp = data.Mode == ForkliftMode.PickUp;
            preH = pickUp ? data.PlatformPlaceHeight : data.PlatformLiftHeight;
            actionH = pickUp ? data.PlatformLiftHeight : data.PlatformPlaceHeight;
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
