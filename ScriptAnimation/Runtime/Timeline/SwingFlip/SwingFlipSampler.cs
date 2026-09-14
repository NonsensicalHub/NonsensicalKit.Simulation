using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 按绝对时间采样摇摆→翻转→再摇摆→翻回序列，写入 <see cref="SwingFlipAnim"/>。
    /// </summary>
    public static class SwingFlipSampler
    {
        public readonly struct SequenceParams
        {
            public readonly float StartDelay;
            public readonly SwingFlipAxis SwayAxis;
            public readonly float SwayAngle;
            public readonly float SwayDuration;
            public readonly SwingFlipAxis FlipAxis;
            public readonly float FlipAngle;
            public readonly float FlipDuration;
            public readonly AnimationCurve Ease;

            public SequenceParams(
                float startDelay,
                SwingFlipAxis swayAxis,
                float swayAngle,
                float swayDuration,
                SwingFlipAxis flipAxis,
                float flipAngle,
                float flipDuration,
                AnimationCurve ease)
            {
                StartDelay = Mathf.Max(0f, startDelay);
                SwayAxis = swayAxis;
                SwayAngle = swayAngle;
                SwayDuration = Mathf.Max(0.01f, swayDuration);
                FlipAxis = flipAxis;
                FlipAngle = flipAngle;
                FlipDuration = Mathf.Max(0.01f, flipDuration);
                Ease = ease;
            }

            public float TotalDuration =>
                StartDelay + SwayDuration + FlipDuration + SwayDuration + FlipDuration;
        }

        public static SequenceParams Resolve(SwingFlipAnim anim, SwingFlipClipData data)
        {
            _ = anim;
            if (data != null)
            {
                return new SequenceParams(
                    data.StartDelay,
                    data.SwayAxis,
                    data.SwayAngle,
                    data.SwayDuration,
                    data.FlipAxis,
                    data.FlipAngle,
                    data.FlipDuration,
                    data.Ease);
            }

            return new SequenceParams(
                0f, SwingFlipAxis.X, 25f, 0.35f, SwingFlipAxis.Y, 180f, 0.5f, null);
        }

        public static float EstimateDuration(SwingFlipAnim anim, SwingFlipClipData data)
        {
            return Mathf.Max(0.01f, Resolve(anim, data).TotalDuration);
        }

        public static void Sample(SwingFlipAnim anim, SwingFlipClipData data, float normalizedTime)
        {
            if (anim == null)
                return;

            SequenceParams p = Resolve(anim, data);
            float total = p.TotalDuration;
            float time = Mathf.Clamp01(normalizedTime) * total;
            EvaluateAt(p, time, out float sway, out float flip);
            anim.ApplySampledPose(sway, flip, p.SwayAxis, p.FlipAxis);
        }

        /// <summary>在序列绝对时间下求摇摆角与翻转角。</summary>
        public static void EvaluateAt(
            in SequenceParams p,
            float time,
            out float swayAngle,
            out float flipAngle)
        {
            swayAngle = 0f;
            flipAngle = 0f;

            float t = Mathf.Max(0f, time);
            if (t <= p.StartDelay)
                return;

            t -= p.StartDelay;

            // 1) 第一次摇摆（forward）
            if (t < p.SwayDuration)
            {
                swayAngle = EvaluateSway(p, t, forward: true);
                return;
            }

            t -= p.SwayDuration;

            // 2) 翻转至 +FlipAngle
            if (t < p.FlipDuration)
            {
                float u = Ease01(p.Ease, t / p.FlipDuration);
                flipAngle = Mathf.LerpUnclamped(0f, p.FlipAngle, u);
                return;
            }

            t -= p.FlipDuration;
            flipAngle = p.FlipAngle;

            if (t < p.SwayDuration)
            {
                swayAngle = EvaluateSway(p, t, forward: false);
                return;
            }

            t -= p.SwayDuration;

            // 4) 翻回至 0
            if (t < p.FlipDuration)
            {
                float u = Ease01(p.Ease, t / p.FlipDuration);
                flipAngle = Mathf.LerpUnclamped(p.FlipAngle, 0f, u);
                return;
            }

            flipAngle = 0f;
        }

        /// <summary>
        /// 钟摆： →first →second →0，三段时长1:2:1。
    /// forward 时 first=+angle；否则 first=-angle。
    /// </summary>
        private static float EvaluateSway(in SequenceParams p, float localTime, bool forward)
        {
            float d = p.SwayDuration;
            float t1 = d * 0.25f;
            float t2 = d * 0.5f;
            float first = forward ? p.SwayAngle : -p.SwayAngle;
            float second = -first;
            float t = Mathf.Clamp(localTime, 0f, d);

            if (t <= t1)
            {
                float u = Ease01(p.Ease, t1 > 1e-6f ? t / t1 : 1f);
                return Mathf.LerpUnclamped(0f, first, u);
            }

            t -= t1;
            if (t <= t2)
            {
                float u = Ease01(p.Ease, t2 > 1e-6f ? t / t2 : 1f);
                return Mathf.LerpUnclamped(first, second, u);
            }

            t -= t2;
            float t3 = d * 0.25f;
            {
                float u = Ease01(p.Ease, t3 > 1e-6f ? t / t3 : 1f);
                return Mathf.LerpUnclamped(second, 0f, u);
            }
        }

        private static float Ease01(AnimationCurve ease, float raw)
        {
            float r = Mathf.Clamp01(raw);
            if (ease == null || ease.length == 0)
                return r;
            return Mathf.Clamp01(ease.Evaluate(r));
        }
    }
}
