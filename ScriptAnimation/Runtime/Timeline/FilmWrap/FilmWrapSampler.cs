using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>按归一化时间采样缠膜进度并写入 <see cref="FilmWrapAnim"/>。</summary>
    public static class FilmWrapSampler
    {
        public const float DefaultDuration = 3f;

        public static float EstimateDuration(FilmWrapClipData data)
        {
            if (data == null)
                return DefaultDuration;
            return Mathf.Max(0.01f, data.DurationSeconds);
        }

        public static float EvaluateProgress(FilmWrapClipData data, float normalizedTime)
        {
            if (data == null)
                return 1f;

            float t = Mathf.Clamp01(normalizedTime);
            if (data.Ease != null && data.Ease.length > 0)
                t = Mathf.Clamp01(data.Ease.Evaluate(t));

            return Mathf.Lerp(data.FromProgress, data.ToProgress, t);
        }

        public static void Sample(FilmWrapAnim anim, FilmWrapClipData data, float normalizedTime)
        {
            if (anim == null || data == null)
                return;

            anim.SetProgress(EvaluateProgress(data, normalizedTime));
        }
    }
}
