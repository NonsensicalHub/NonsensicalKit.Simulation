using NonsensicalKit.Core;
using UnityEngine;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 堆垛机货位移动：双轴同时出发，各轴沿单一配置方向梯形加减速，
    /// 总时长 = max(行走, 升降)。世界位置由货位坐标经 WarehouseManager 或 CellPositionTable 解析。
    /// </summary>
    public static class StackerSampler
    {
        public readonly struct MotionPlans
        {
            public readonly AxisMotionProfile.Plan Travel;
            public readonly AxisMotionProfile.Plan Lift;
            public readonly float Duration;

            public MotionPlans(AxisMotionProfile.Plan travel, AxisMotionProfile.Plan lift)
            {
                Travel = travel;
                Lift = lift;
                Duration = Mathf.Max(0.01f, Mathf.Max(travel.Duration, lift.Duration));
            }
        }

        public static float EstimateDuration(
            StackerAnim anim,
            StackerClipData data,
            Vector3 startWorld,
            Vector3 endWorld)
        {
            if (anim == null || data == null)
                return -1f;
            return BuildPlans(anim, data, startWorld, endWorld).Duration;
        }

        public static void Sample(
            StackerAnim anim,
            StackerClipData data,
            Vector3 startWorld,
            Vector3 endWorld,
            float normalizedTime)
        {
            if (anim == null || data == null)
                return;

            MotionPlans plans = BuildPlans(anim, data, startWorld, endWorld);
            float t = Mathf.Clamp01(normalizedTime) * plans.Duration;

            float uTravel = AxisMotionProfile.ProgressAt(plans.Travel, t);
            float uLift = AxisMotionProfile.ProgressAt(plans.Lift, t);

            Vector3 slot = startWorld;
            float travelStart = SignedAxisUtil.GetComponent(startWorld, anim.TravelDirection);
            float travelEnd = SignedAxisUtil.GetComponent(endWorld, anim.TravelDirection);
            float liftStart = SignedAxisUtil.GetComponent(startWorld, anim.LiftDirection);
            float liftEnd = SignedAxisUtil.GetComponent(endWorld, anim.LiftDirection);

            slot = SignedAxisUtil.WithComponent(
                slot, anim.TravelDirection, Mathf.Lerp(travelStart, travelEnd, uTravel));
            slot = SignedAxisUtil.WithComponent(
                slot, anim.LiftDirection, Mathf.Lerp(liftStart, liftEnd, uLift));

            anim.ApplySlotPosition(slot);
        }

        public static MotionPlans BuildPlans(
            StackerAnim anim,
            StackerClipData data,
            Vector3 startWorld,
            Vector3 endWorld)
        {
            Vector3 delta = endWorld - startWorld;
            float travelDist = Mathf.Abs(SignedAxisUtil.GetComponent(delta, anim.TravelDirection));
            float liftDist = Mathf.Abs(SignedAxisUtil.GetComponent(delta, anim.LiftDirection));

            float travelSpeed = DurationUtility.SafeSpeed(data.TravelSpeed);
            float travelAccel = Mathf.Max(0.01f, data.TravelAcceleration);

            float liftSpeed = DurationUtility.SafeSpeed(data.LiftSpeed);
            float liftAccel = Mathf.Max(0.01f, data.LiftAcceleration);

            var travel = AxisMotionProfile.Build(travelDist, travelSpeed, travelAccel);
            var lift = AxisMotionProfile.Build(liftDist, liftSpeed, liftAccel);
            return new MotionPlans(travel, lift);
        }

        public static bool TryResolveEndWorld(
            StackerAnim anim,
            StackerClipData data,
            out Vector3 worldPos)
        {
            worldPos = default;
            if (anim == null || data == null)
                return false;

            if (!anim.TryGetCellWorldPosition(data.EndCell, out worldPos))
                return false;

            worldPos += data.DestinationOffset;
            return true;
        }

        public static bool TryResolveStartWorld(
            StackerAnim anim,
            StackerClipData data,
            TimelineStartFallback fallback,
            out Vector3 worldPos)
        {
            worldPos = default;
            if (anim == null || data == null)
                return false;

            if (data.HasStartCell)
            {
                if (!anim.TryGetCellWorldPosition(data.StartCell, out worldPos))
                    return false;
                return true;
            }

            if (fallback.HasValue)
            {
                worldPos = fallback.Value;
                return true;
            }

            if (anim.HasHome)
            {
                worldPos = anim.HomePosition;
                return true;
            }

            Debug.LogWarning(
                "[Stacker] 时 StartCell / 前序 Stacker / Home，无法确定性解析开场位置。" +
                "请配置 StartCell、前序 StackerClip，或在 StackerAnim 上捕获 Home。",
                anim);
            return false;
        }

        /// <summary>前序 StackerClip 结束世界位置（可选）。</summary>
        public static TimelineStartFallback ResolvePreviousClipEnd(
            StackerAnim anim,
            TimelineClip timelineClip)
        {
            if (timelineClip == null || anim == null)
                return TimelineStartFallback.None;

            if (!TryFindPreviousStackerClip(timelineClip, out TimelineClip previous))
                return TimelineStartFallback.None;

            if (previous.asset is not StackerClip prevAsset || prevAsset.Data == null)
                return TimelineStartFallback.None;

            if (!TryResolveEndWorld(anim, prevAsset.Data, out Vector3 endPos))
                return TimelineStartFallback.None;

            return new TimelineStartFallback(endPos);
        }

        static bool TryFindPreviousStackerClip(TimelineClip clip, out TimelineClip previous)
        {
            previous = null;
            TrackAsset track = clip?.GetParentTrack();
            if (track == null)
                return false;

            double bestStart = double.NegativeInfinity;
            foreach (TimelineClip other in track.GetClips())
            {
                if (other == null || other == clip || other.asset is not StackerClip)
                    continue;
                if (other.start >= clip.start)
                    continue;
                if (other.start > bestStart)
                {
                    bestStart = other.start;
                    previous = other;
                }
            }

            return previous != null;
        }

        /// <summary>前序 Clip 结束世界位置（可选）。</summary>
        public readonly struct TimelineStartFallback
        {
            public readonly bool HasValue;
            public readonly Vector3 Value;

            public TimelineStartFallback(Vector3 value)
            {
                HasValue = true;
                Value = value;
            }

            public static TimelineStartFallback None => default;
        }

        public static string FormatCell(Int4 cell) =>
            $"层{cell.X} 列{cell.Y} 排{cell.Z} 深{cell.W}";
    }
}
