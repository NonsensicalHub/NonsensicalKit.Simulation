using System;
using UnityEngine;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 姿态改变采样：从前序 PoseChange 终点（无前序则用组件默认状态）按 Clip 归一化时间插值到命名目标姿态。
    /// DurationSeconds 为0 时瞬间到位；Clip 视觉长度取自所属轨 <see cref="ScriptAnimTrackBase.InstantHoldFrames"/>。
    /// </summary>
    public static class PoseChangeSampler
    {
        /// <summary>无轨上下文时的默认占位帧数。</summary>
        public const int InstantHoldFrames = DurationUtility.DefaultInstantHoldFrames;

        public const float DefaultFrameRate = 60f;

        public struct PoseTRS
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 Scale;

            public static PoseTRS FromDefinition(PoseDefinition pose)
            {
                if (pose == null)
                {
                    return new PoseTRS
                    {
                        Position = Vector3.zero,
                        Rotation = Quaternion.identity,
                        Scale = Vector3.one
                    };
                }

                return new PoseTRS
                {
                    Position = pose.Position,
                    Rotation = pose.Rotation,
                    Scale = pose.Scale
                };
            }

            public static PoseTRS Lerp(in PoseTRS a, in PoseTRS b, float t)
            {
                return new PoseTRS
                {
                    Position = Vector3.LerpUnclamped(a.Position, b.Position, t),
                    Rotation = Quaternion.SlerpUnclamped(a.Rotation, b.Rotation, t),
                    Scale = Vector3.LerpUnclamped(a.Scale, b.Scale, t)
                };
            }
        }

        public static bool IsInstant(PoseChangeClipData data)
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

        /// <summary>
        /// AutoSync / 新建 Clip 用的目标时长：用时 &gt; 0 为用时本身；用时为 0 则为占位帧数。
    /// 实际插值时长以 Timeline Clip 长度为准（瞬间模式除外）。
    /// </summary>
        public static float EstimateClipDuration(
            PoseChangeClipData data,
            float frameRate = DefaultFrameRate,
            int holdFrames = -1)
        {
            if (!IsInstant(data))
                return Mathf.Max(DurationUtility.DefaultMinClipDuration, data.DurationSeconds);
            return InstantClipDuration(frameRate, holdFrames);
        }

        public static float EstimateClipDuration(PoseChangeClipData data, TimelineClip clip)
            => EstimateClipDuration(
                data,
                ResolveFrameRate(clip),
                ScriptAnimTrackBase.ResolveInstantHoldFrames(clip));

        public static bool TryResolveEndPose(
            IPoseChangeActor anim,
            string poseName,
            out PoseTRS pose)
        {
            pose = default;
            if (anim == null || !anim.TryGetPose(poseName, out PoseDefinition definition))
                return false;

            pose = PoseTRS.FromDefinition(definition);
            return true;
        }

        public static PoseTRS CaptureCurrent(IPoseChangeActor anim)
        {
            Transform control = anim != null ? anim.ControlTarget : null;
            if (control == null)
            {
                return new PoseTRS
                {
                    Position = Vector3.zero,
                    Rotation = Quaternion.identity,
                    Scale = Vector3.one
                };
            }

            if (anim.Space == PoseSpace.World)
            {
                return new PoseTRS
                {
                    Position = control.position,
                    Rotation = control.rotation,
                    Scale = control.localScale
                };
            }

            return new PoseTRS
            {
                Position = control.localPosition,
                Rotation = control.localRotation,
                Scale = control.localScale
            };
        }

        /// <summary>
        /// 取本 Clip 之前最近一个 Clip：若也是姿态改变，则用其目标姿态作为起点（便于 scrub）。
    /// </summary>
        public static bool TryResolvePreviousEndPose(
            IPoseChangeActor anim,
            TimelineClip timelineClip,
            out PoseTRS pose)
        {
            pose = default;
            if (anim == null || timelineClip == null)
                return false;

            TrackAsset track = timelineClip.GetParentTrack();
            if (track == null)
                return false;

            double bestStart = double.NegativeInfinity;
            TimelineClip previous = null;
            foreach (TimelineClip other in track.GetClips())
            {
                if (other == null || other == timelineClip)
                    continue;
                if (other.start >= timelineClip.start)
                    continue;
                if (other.start > bestStart)
                {
                    bestStart = other.start;
                    previous = other;
                }
            }

            if (previous?.asset is not PoseChangeClip prevClip || prevClip.Data == null)
                return false;

            return TryResolveEndPose(anim, prevClip.Data.PoseName, out pose);
        }

        /// <summary>
        /// 插值进度。瞬间模式恒为 1；否则按 Clip 归一化时间（0~1）与缓动曲线计算。
    /// </summary>
        public static float EvaluateProgress(PoseChangeClipData data, float normalizedTime)
        {
            if (IsInstant(data))
                return 1f;

            float t = Mathf.Clamp01(normalizedTime);
            if (data.Ease != null && data.Ease.length > 0)
                t = Mathf.Clamp01(data.Ease.Evaluate(t));
            return t;
        }

        public static PoseTRS Evaluate(
            in PoseTRS start,
            in PoseTRS end,
            PoseChangeClipData data,
            float normalizedTime)
        {
            float t = EvaluateProgress(data, normalizedTime);
            if (t >= 1f - 1e-6f)
                return end;
            if (t <= 1e-6f)
                return start;
            return PoseTRS.Lerp(start, end, t);
        }

        public static void Sample(
            IPoseChangeActor anim,
            PoseChangeClipData data,
            in PoseTRS start,
            in PoseTRS end,
            float normalizedTime)
        {
            if (anim == null || data == null)
                return;

            anim.ApplyPose(Evaluate(start, end, data, normalizedTime));
        }

        public static bool TryResolveEndPose(
            PoseChangeAnimMax anim,
            string poseName,
            out PoseTRS[] poses)
        {
            poses = null;
            if (anim == null || !anim.TryGetPose(poseName, out MultiTargetPoseDefinition definition))
                return false;

            poses = MultiTargetPoseDefinition.ToPoseTRSArray(definition);
            return poses.Length > 0;
        }

        public static bool TryResolvePreviousEndPose(
            PoseChangeAnimMax anim,
            TimelineClip timelineClip,
            out PoseTRS[] poses)
        {
            poses = null;
            if (anim == null || timelineClip == null)
                return false;

            TrackAsset track = timelineClip.GetParentTrack();
            if (track == null)
                return false;

            double bestStart = double.NegativeInfinity;
            TimelineClip previous = null;
            foreach (TimelineClip other in track.GetClips())
            {
                if (other == null || other == timelineClip)
                    continue;
                if (other.start >= timelineClip.start)
                    continue;
                if (other.start > bestStart)
                {
                    bestStart = other.start;
                    previous = other;
                }
            }

            if (previous?.asset is not PoseChangeClip prevClip || prevClip.Data == null)
                return false;

            return TryResolveEndPose(anim, prevClip.Data.PoseName, out poses);
        }

        public static PoseTRS[] EvaluateMulti(
            PoseTRS[] start,
            PoseTRS[] end,
            PoseChangeClipData data,
            float normalizedTime)
        {
            float t = EvaluateProgress(data, normalizedTime);
            int count = Mathf.Max(start?.Length ?? 0, end?.Length ?? 0);
            if (count == 0)
                return Array.Empty<PoseTRS>();

            var result = new PoseTRS[count];
            for (int i = 0; i < count; i++)
            {
                PoseTRS a = start != null && i < start.Length
                    ? start[i]
                    : new PoseTRS { Position = Vector3.zero, Rotation = Quaternion.identity, Scale = Vector3.one };
                PoseTRS b = end != null && i < end.Length
                    ? end[i]
                    : new PoseTRS { Position = Vector3.zero, Rotation = Quaternion.identity, Scale = Vector3.one };

                if (t >= 1f - 1e-6f)
                    result[i] = b;
                else if (t <= 1e-6f)
                    result[i] = a;
                else
                    result[i] = PoseTRS.Lerp(a, b, t);
            }

            return result;
        }

        public static void Sample(
            PoseChangeAnimMax anim,
            PoseChangeClipData data,
            PoseTRS[] start,
            PoseTRS[] end,
            float normalizedTime)
        {
            if (anim == null || data == null)
                return;

            anim.ApplyPoses(EvaluateMulti(start, end, data, normalizedTime));
        }
    }
}
