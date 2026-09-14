using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>按归一化时间采样透明度并写入 <see cref="FadeAnim"/>。</summary>
    public static class FadeSampler
    {
        public const float DefaultDuration = 1f;

        public static float EstimateDuration(FadeClipData data)
        {
            if (data == null)
                return DefaultDuration;
            return Mathf.Max(0.01f, data.DurationSeconds);
        }

        public static float EvaluateAlpha(FadeClipData data, float normalizedTime)
        {
            if (data == null)
                return 1f;

            float t = Mathf.Clamp01(normalizedTime);
            if (data.Ease != null && data.Ease.length > 0)
                t = Mathf.Clamp01(data.Ease.Evaluate(t));

            return Mathf.Lerp(data.FromAlpha, data.ToAlpha, t);
        }

        public static void Sample(FadeAnim anim, FadeClipData data, float normalizedTime)
        {
            if (anim == null || data == null)
                return;

            anim.SetAlpha(EvaluateAlpha(data, normalizedTime));
        }
    }
}
