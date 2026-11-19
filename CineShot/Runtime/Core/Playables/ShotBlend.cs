using UnityEngine;

namespace CineShot.Core
{
    /// <summary>片段进入时的位姿混合：重叠优先时长，曲线控快慢。</summary>
    public static class ShotBlend
    {
        const double k_SameStart = 1e-4d;

        /// <summary>
        /// 选当前镜：先比开拍时间（后开的是进入镜，保叙事叠化）；
        /// 开拍几乎相同（同刻叠片）再用 priority，大者胜。
        /// 返回 &gt;0 表示 a 更适合做当前镜。
        /// </summary>
        public static int CompareActive(double startA, int priorityA, double startB, int priorityB)
        {
            double d = startA - startB;
            if (d > k_SameStart)
                return 1;
            if (d < -k_SameStart)
                return -1;
            return priorityA.CompareTo(priorityB);
        }

        public static float ResolveDuration(float blendDuration)
        {
            return blendDuration > 0.01f ? blendDuration : 0.5f;
        }

        public static float Window(double overlapSeconds, float configuredDuration)
        {
            if (overlapSeconds > 0.01d)
                return (float)overlapSeconds;
            return ResolveDuration(configuredDuration);
        }

        public static float Factor(float window, double secondsIntoClip, EasingAsset curve)
        {
            if (window <= 0.01f)
                window = 0.5f;
            if (secondsIntoClip <= 0d)
                return 0f;
            if (secondsIntoClip >= window)
                return 1f;
            float u = (float)(secondsIntoClip / window);
            if (curve != null)
                return Mathf.Clamp01(curve.Evaluate(u));
            return u * u * (3f - 2f * u);
        }

        public static EasingAsset Resolve(CineShotClip clip, bool fadeIn)
        {
            if (clip == null)
                return null;
            EasingAsset direct = fadeIn ? clip.easeIn : clip.easeOut;
            if (direct != null)
                return direct;
            if (clip.shot == null)
                return null;
            bool masked = (clip.overrides.mask & ShotOverrideMask.Easing) != 0;
            EasingAsset over = fadeIn ? clip.overrides.easeIn : clip.overrides.easeOut;
            if (masked && over != null)
                return over;
            return fadeIn ? clip.shot.easeIn : clip.shot.easeOut;
        }

        public static EasingAsset BlendCurve(CineShotClip incoming, CineShotClip previous)
        {
            EasingAsset curve = Resolve(incoming, true);
            if (curve != null)
                return curve;
            return Resolve(previous, false);
        }

        public static CameraPose Apply(ShotBlendMode mode, float blendDuration, double overlapSeconds, double secondsIntoClip, bool hasPrevious, in CameraPose previous, in CameraPose current, EasingAsset curve)
        {
            if (mode != ShotBlendMode.Blend || !hasPrevious)
                return current;
            float u = Factor(Window(overlapSeconds, blendDuration), secondsIntoClip, curve);
            if (u <= 0f)
                return previous;
            if (u >= 1f)
                return current;
            return CameraPose.Lerp(previous, current, u);
        }

        /// <summary>叠化时插值景深外观。模式在中点切换，避免两套体积参数各写一半。</summary>
        public static FocusSettings LerpSettings(in FocusSettings a, in FocusSettings b, float u)
        {
            if (u <= 0f)
                return a;
            if (u >= 1f)
                return b;
            var s = b;
            s.dofMode = u < 0.5f ? a.dofMode : b.dofMode;
            s.damping = Mathf.Lerp(a.damping, b.damping, u);
            s.maxFocusSpeed = Mathf.Lerp(a.maxFocusSpeed, b.maxFocusSpeed, u);
            s.pullDuration = Mathf.Lerp(a.pullDuration, b.pullDuration, u);
            s.aperture = Mathf.Lerp(a.aperture, b.aperture, u);
            s.focalLength = Mathf.Lerp(a.focalLength, b.focalLength, u);
            s.bladeCount = Mathf.RoundToInt(Mathf.Lerp(a.bladeCount, b.bladeCount, u));
            s.hyperfocalCircleOfConfusion = Mathf.Lerp(a.hyperfocalCircleOfConfusion, b.hyperfocalCircleOfConfusion, u);
            return s;
        }

        /// <summary>
        /// 匹配帧：先在 H 秒内贴住锚点，再按剩余窗口 W-H 从锚点混到本镜。
        /// W 与混合模式相同（重叠优先，否则 blendDuration）。H &gt;= W 时硬切本镜。
        /// </summary>
        public static CameraPose ApplyMatch(
            ShotBlendMode mode,
            in MatchPoint point,
            float matchHoldDuration,
            float blendDuration,
            double overlapSeconds,
            double secondsIntoClip,
            EasingAsset curve,
            in CameraPose current)
        {
            if (mode != ShotBlendMode.MatchFrame || !point.valid)
                return current;

            float w = Window(overlapSeconds, blendDuration);
            float h = Mathf.Max(0f, matchHoldDuration);
            if (h >= w)
                return current;

            CameraPose match = ToPose(in point, in current);
            if (secondsIntoClip < h)
                return match;

            float remain = w - h;
            float u = Factor(remain, secondsIntoClip - h, curve);
            if (u <= 0f)
                return match;
            if (u >= 1f)
                return current;
            return CameraPose.Lerp(match, current, u);
        }

        public static bool MatchHoldExceedsWindow(float matchHoldDuration, double overlapSeconds, float blendDuration)
        {
            float w = Window(overlapSeconds, blendDuration);
            return Mathf.Max(0f, matchHoldDuration) >= w;
        }

        public static CameraPose ToPose(in MatchPoint point, in CameraPose fallback)
        {
            var next = fallback;
            next.Position = point.position;
            next.Rotation = point.rotation;
            if (point.fov > 1f)
                next.Fov = point.fov;
            return next;
        }
    }
}
