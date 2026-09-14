using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 开箱采样：从未勾选阶段保持前序终点，勾选阶段按 Clip 归一化时间插值到目标。
    /// DurationSeconds 为0 时瞬间到位；Clip 视觉长度取自所属轨 <see cref="ScriptAnimTrackBase.InstantHoldFrames"/>。
    /// </summary>
    public static class OpenBoxSampler
    {
        /// <summary>无轨上下文时的默认占位帧数。</summary>
        public const int InstantHoldFrames = DurationUtility.DefaultInstantHoldFrames;
        public const float DefaultFrameRate = 60f;
        public const float DefaultDuration = 1f;

        public static bool IsInstant(OpenBoxClipData data)
            => data == null || data.DurationSeconds <= 1e-6f;

        public static float ResolveFrameRate(TimelineAsset timeline)
        {
            if (timeline != null)
            {
                double fps = timeline.editorSettings.frameRate;
                if (fps > 1e-3)
                    return (float)fps;
            }

            return DefaultFrameRate;
        }

        public static float ResolveFrameRate(TimelineClip clip)
            => ResolveFrameRate(clip?.GetParentTrack()?.timelineAsset);

        public static float InstantClipDuration(float frameRate = DefaultFrameRate, int holdFrames = -1)
        {
            int frames = holdFrames > 0 ? holdFrames : InstantHoldFrames;
            return DurationUtility.TimeForFrames(frames, frameRate);
        }

        public static float EstimateClipDuration(
            OpenBoxClipData data,
            float frameRate = DefaultFrameRate,
            int holdFrames = -1)
        {
            if (!IsInstant(data))
                return Mathf.Max(DurationUtility.DefaultMinClipDuration, data.DurationSeconds);
            return InstantClipDuration(frameRate, holdFrames);
        }

        public static float EstimateClipDuration(OpenBoxClipData data, TimelineClip clip)
            => EstimateClipDuration(
                data,
                ResolveFrameRate(clip),
                ScriptAnimTrackBase.ResolveInstantHoldFrames(clip));

        public static float EvaluateProgress(OpenBoxClipData data, float normalizedTime)
        {
            if (IsInstant(data))
                return 1f;

            float t = Mathf.Clamp01(normalizedTime);
            if (data.Ease != null && data.Ease.length > 0)
                t = Mathf.Clamp01(data.Ease.Evaluate(t));
            return t;
        }

        public static OpenBoxPose Evaluate(in OpenBoxPose start, OpenBoxClipData data, float normalizedTime)
        {
            if (data == null)
                return start;

            float t = EvaluateProgress(data, normalizedTime);
            var pose = start;
            if (data.AnimateWall)
                pose.Wall = Mathf.Lerp(FromValue(data.FromCurrent, start.Wall, data.FromWall), data.Wall, t);
            if (data.AnimateBottomShort)
                pose.BottomShort = Mathf.Lerp(
                    FromValue(data.FromCurrent, start.BottomShort, data.FromBottomShort), data.BottomShort, t);
            if (data.AnimateBottomLong)
                pose.BottomLong = Mathf.Lerp(
                    FromValue(data.FromCurrent, start.BottomLong, data.FromBottomLong), data.BottomLong, t);
            if (data.AnimateTopShort)
                pose.TopShort = Mathf.Lerp(
                    FromValue(data.FromCurrent, start.TopShort, data.FromTopShort), data.TopShort, t);
            if (data.AnimateTopLong)
                pose.TopLong = Mathf.Lerp(
                    FromValue(data.FromCurrent, start.TopLong, data.FromTopLong), data.TopLong, t);
            return pose;
        }

        static float FromValue(bool fromCurrent, float start, float from) => fromCurrent ? start : from;

        public static OpenBoxPose ResolveStartPose(OpenBoxAnim anim, TimelineClip timelineClip)
        {
            // 无前序链时用组件默认状态（确定性）；不再用 Rest / 当前折叠值
            OpenBoxPose pose = anim != null
                ? anim.ResolveDefaultStartPose()
                : OpenBoxPose.Flat;

            if (timelineClip == null)
                return pose;

            TrackAsset track = timelineClip.GetParentTrack();
            if (track == null)
                return pose;

            var previous = new List<TimelineClip>(8);
            foreach (TimelineClip other in track.GetClips())
            {
                if (other == null || other == timelineClip)
                    continue;
                if (other.asset is not OpenBoxClip)
                    continue;
                if (other.start >= timelineClip.start)
                    continue;
                previous.Add(other);
            }

            previous.Sort((a, b) => a.start.CompareTo(b.start));
            for (int i = 0; i < previous.Count; i++)
            {
                if (previous[i].asset is OpenBoxClip prevClip && prevClip.Data != null)
                    pose = Evaluate(pose, prevClip.Data, 1f);
            }

            return pose;
        }

        public static void Sample(OpenBoxAnim anim, in OpenBoxPose pose)
        {
            if (anim == null)
                return;
            anim.ApplyTimelinePose(pose);
        }

        public static string FormatDisplayName(OpenBoxClipData data)
        {
            if (data == null || !data.AnimatesAny)
                return "开箱(无阶段";

            var parts = new List<string>(5);
            AppendStage(parts, data.AnimateWall, "侧壁", data.FromCurrent, data.FromWall, data.Wall);
            AppendStage(parts, data.AnimateBottomShort, "底短", data.FromCurrent, data.FromBottomShort, data.BottomShort);
            AppendStage(parts, data.AnimateBottomLong, "底长", data.FromCurrent, data.FromBottomLong, data.BottomLong);
            AppendStage(parts, data.AnimateTopShort, "顶短", data.FromCurrent, data.FromTopShort, data.TopShort);
            AppendStage(parts, data.AnimateTopLong, "顶长", data.FromCurrent, data.FromTopLong, data.TopLong);

            if (parts.Count == 1)
                return "开箱 " + parts[0];
            return "开箱 " + string.Join(",", parts);
        }

        static void AppendStage(List<string> parts, bool animate, string label, bool fromCurrent, float from, float to)
        {
            if (!animate)
                return;
            if (fromCurrent)
                parts.Add($"{label}→{to:0.##}");
            else
                parts.Add($"{label} {from:0.##}→{to:0.##}");
        }
    }
}
