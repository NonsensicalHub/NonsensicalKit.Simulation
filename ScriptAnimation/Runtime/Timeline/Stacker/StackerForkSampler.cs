using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 堆垛机取/放货：停在原地，一级/二级货叉沿各自本地轴运动（无车体前进后退/转向）。
    /// </summary>
    public static class StackerForkSampler
    {
        private struct PhaseTimes
        {
            public float PreFork;
            public float ActionFork;
            public float PostFork;
            public float Total => PreFork + ActionFork + PostFork;
        }

        public static float EstimateDuration(StackerAnim anim, StackerForkClipData data)
        {
            if (anim == null || data == null)
                return -1f;

            float duration = 0f;

            if (anim.PrimaryFork != null)
            {
                ResolvePrimaryForkOffsets(data, out float travel, out float pre, out float action);
                duration = Mathf.Max(
                    duration,
                    BuildPhases(DurationUtility.SafeSpeed(data.ForkSpeed), travel, pre, action).Total);
            }

            if (anim.SecondaryFork != null)
            {
                ResolveSecondaryForkOffsets(data, out float travel, out float pre, out float action);
                duration = Mathf.Max(
                    duration,
                    BuildPhases(DurationUtility.SafeSpeed(data.SecondaryForkSpeed), travel, pre, action).Total);
            }

            return duration > 1e-6f ? duration : 0.01f;
        }

        public static void Sample(StackerAnim anim, StackerForkClipData data, float normalizedTime)
        {
            if (anim == null || data == null)
                return;

            float duration = EstimateDuration(anim, data);
            float elapsed = Mathf.Clamp01(normalizedTime) * Mathf.Max(0.01f, duration);

            if (anim.PrimaryFork != null)
            {
                ResolvePrimaryForkOffsets(data, out float travel, out float pre, out float action);
                PhaseTimes phases = BuildPhases(DurationUtility.SafeSpeed(data.ForkSpeed), travel, pre, action);
                float offset = EvaluateOffsetAtElapsed(phases, travel, pre, action, elapsed);
                SetForkOffset(anim.PrimaryFork, anim.PrimaryForkAxisLocal, offset);
            }

            if (anim.SecondaryFork != null)
            {
                ResolveSecondaryForkOffsets(data, out float travel, out float pre, out float action);
                PhaseTimes phases = BuildPhases(
                    DurationUtility.SafeSpeed(data.SecondaryForkSpeed), travel, pre, action);
                float offset = EvaluateOffsetAtElapsed(phases, travel, pre, action, elapsed);
                SetForkOffset(anim.SecondaryFork, anim.SecondaryForkAxisLocal, offset);
            }
        }

        private static PhaseTimes BuildPhases(float forkSpeed, float travel, float pre, float action)
        {
            return new PhaseTimes
            {
                PreFork = DurationUtility.TimeForDistance(Mathf.Abs(pre - travel), forkSpeed),
                ActionFork = DurationUtility.TimeForDistance(Mathf.Abs(action - pre), forkSpeed),
                PostFork = DurationUtility.TimeForDistance(Mathf.Abs(travel - action), forkSpeed)
            };
        }

        private static float EvaluateOffsetAtElapsed(
            PhaseTimes phases,
            float travel,
            float pre,
            float action,
            float elapsed)
        {
            float cursor = 0f;

            if (elapsed <= cursor + phases.PreFork)
            {
                float u = phases.PreFork > 1e-5f ? (elapsed - cursor) / phases.PreFork : 1f;
                return Mathf.Lerp(travel, pre, u);
            }

            cursor += phases.PreFork;
            if (elapsed <= cursor + phases.ActionFork)
            {
                float u = phases.ActionFork > 1e-5f ? (elapsed - cursor) / phases.ActionFork : 1f;
                return Mathf.Lerp(pre, action, u);
            }

            cursor += phases.ActionFork;
            float postU = phases.PostFork > 1e-5f ? (elapsed - cursor) / phases.PostFork : 1f;
            return Mathf.Lerp(action, travel, Mathf.Clamp01(postU));
        }

        /// <summary>
        /// 取货：先到 Place 再抬到 Lift；放货：先保持 Lift 再落到 Place。
        /// </summary>
        private static void ResolvePrimaryForkOffsets(
            StackerForkClipData data, out float travel, out float pre, out float action)
        {
            ResolveForkOffsets(
                data.Mode,
                data.ForkTravelOffset,
                data.ForkPlaceOffset,
                data.ForkLiftOffset,
                out travel,
                out pre,
                out action);
        }

        private static void ResolveSecondaryForkOffsets(
            StackerForkClipData data, out float travel, out float pre, out float action)
        {
            ResolveForkOffsets(
                data.Mode,
                data.SecondaryForkTravelOffset,
                data.SecondaryForkPlaceOffset,
                data.SecondaryForkLiftOffset,
                out travel,
                out pre,
                out action);
        }

        private static void ResolveForkOffsets(
            ForkliftMode mode,
            float travelOffset,
            float placeOffset,
            float liftOffset,
            out float travel,
            out float pre,
            out float action)
        {
            travel = travelOffset;
            bool pickUp = mode == ForkliftMode.PickUp;
            pre = pickUp ? placeOffset : liftOffset;
            action = pickUp ? liftOffset : placeOffset;
        }

        private static void SetForkOffset(Transform fork, Vector3 axisLocal, float offset)
        {
            if (fork == null)
                return;

            Vector3 axis = axisLocal.sqrMagnitude > 1e-6f ? axisLocal.normalized : Vector3.forward;
            Vector3 local = fork.localPosition;
            local -= axis * Vector3.Dot(local, axis);
            local += axis * offset;
            fork.localPosition = local;
        }
    }
}
