using UnityEngine;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 瞬移：解析目标位姿并立即落到该点。
    /// Clip 默认占位帧数取自所层 <see cref="ScriptAnimTrackBase.InstantHoldFrames"/>（默认 <see cref="HoldFrames"/>），
    /// 可在 Timeline 自由调整；写入只在首帧发生。
    /// </summary>
    public static class TeleportSampler
    {
        /// <summary>无轨上下文时的默认占位帧数（为 <see cref="DurationUtility.DefaultInstantHoldFrames"/> 一致）。</summary>
        public const int HoldFrames = DurationUtility.DefaultInstantHoldFrames;

        public const float DefaultFrameRate = 60f;

        public static bool TryResolvePose(
            PathNode targetNode,
            TeleportClipData data,
            Quaternion fallbackRotation,
            out Vector3 position,
            out Quaternion rotation,
            PathMoveActor actor = null)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            if (data == null)
                return false;

            if (data.UseWorldPosition)
            {
                position = data.WorldPosition + data.DestinationOffset;
                // 世界坐标无节点朝向，「对齐目标节点」回退为前序当前朝向
                rotation = ResolveFacing(null, data, fallbackRotation, actor);
                return true;
            }

            if (targetNode == null)
                return false;

            position = targetNode.Position + data.DestinationOffset;
            rotation = ResolveFacing(targetNode, data, fallbackRotation, actor);
            return true;
        }

        public static Quaternion ResolveFacing(
            PathNode targetNode,
            TeleportClipData data,
            Quaternion fallbackRotation,
            PathMoveActor actor = null)
        {
            if (data == null)
                return FlattenYaw(fallbackRotation, actor);

            switch (data.FacingMode)
            {
                case TeleportFacingMode.KeepPrevious:
                    return FlattenYaw(fallbackRotation, actor);
                case TeleportFacingMode.CustomYaw:
                    if (actor != null)
                        return actor.RotationFromYawDegrees(data.CustomYawDegrees, fallbackRotation);
                    return Quaternion.Euler(0f, data.CustomYawDegrees, 0f);
                case TeleportFacingMode.FaceNode:
                default:
                    return targetNode != null
                        ? FlattenYaw(targetNode.transform.rotation, actor)
                        : FlattenYaw(fallbackRotation, actor);
            }
        }

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

        public static float OneFrameDuration(float frameRate = DefaultFrameRate)
            => 1f / Mathf.Max(1f, frameRate);

        public static int ResolveHoldFrames(TimelineClip clip)
            => ScriptAnimTrackBase.ResolveInstantHoldFrames(clip);

        /// <summary>占位时长（秒）；无轨时用默认 <see cref="HoldFrames"/>。</summary>
        public static float EstimateDuration(float frameRate = DefaultFrameRate, int holdFrames = -1)
        {
            int frames = holdFrames > 0 ? holdFrames : HoldFrames;
            return frames * OneFrameDuration(frameRate);
        }

        public static float EstimateDuration(TimelineClip clip)
            => EstimateDuration(ResolveFrameRate(clip), ResolveHoldFrames(clip));

        public static float EstimateDuration(TimelineAsset timeline)
            => EstimateDuration(ResolveFrameRate(timeline));

        /// <summary>
        /// 是否应在本评估帧写入瞬移位姿。
        /// 历史：仅首帧写入；现 Mixer 在 Clip 覆盖区间每帧 Sample，本方法保留供编辑器/旧调用兼容。
        /// seek 或 hold 时恒为 true。
        /// </summary>
        public static bool ShouldApplyPose(double localTime, float frameRate, bool seekOccurred)
        {
            if (seekOccurred)
                return true;
            return localTime <= OneFrameDuration(frameRate) + 1e-6;
        }

        public static void Sample(
            PathMoveActor actor,
            TeleportClipData data,
            Vector3 position,
            Quaternion rotation)
        {
            if (actor == null || data == null)
                return;

            Transform tr = actor.MoverTransform;
            tr.position = position;
            tr.rotation = rotation;
        }

        private static Quaternion FlattenYaw(Quaternion rotation, PathMoveActor actor)
        {
            if (actor != null)
                return actor.FlattenRotation(rotation, Quaternion.identity);

            Vector3 forward = rotation * Vector3.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-8f)
                return Quaternion.identity;
            return Quaternion.LookRotation(forward.normalized, Vector3.up);
        }
    }
}
