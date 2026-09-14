using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>按归一化时间在 Clip 时长内采样显隐并写入 <see cref="BlinkAnim"/>。</summary>
    public static class BlinkSampler
    {
        public const float DefaultDuration = 0.3f;

        public static float EstimateDuration(BlinkClipData data)
        {
            if (data == null)
                return DefaultDuration;

            return Mathf.Max(0.01f, data.ToggleCount * data.IntervalSeconds);
        }

        public static bool EvaluateVisible(BlinkClipData data, float normalizedTime, bool holdEnd)
        {
            if (data == null)
                return true;

            if (data.ToggleCount <= 0)
                return data.StartVisible;

            int togglesDone = holdEnd
                ? data.ToggleCount
                : Mathf.Min(data.ToggleCount, Mathf.FloorToInt(normalizedTime * data.ToggleCount));

            return data.StartVisible ^ (togglesDone % 2 == 1);
        }

        public static void Sample(BlinkAnim anim, BlinkClipData data, float normalizedTime, bool holdEnd)
        {
            if (anim == null || data == null)
                return;

            anim.SetVisible(EvaluateVisible(data, normalizedTime, holdEnd));
        }
    }
}
