using UnityEngine;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>特种机动 Clip 共用的前序位姿解析。</summary>
    public static class ManeuverHomeUtility
    {
        public static bool TryResolveIncomingPose(
            TimelineClip timelineClip,
            PathMoveActor actor,
            IExposedPropertyTable resolver,
            out Vector3 position,
            out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;

            if (!ScriptAnimHomeResolver.TryFindPreviousClip(timelineClip, out TimelineClip previous))
            {
                if (actor == null)
                    return false;
                actor.ResolveHomePoseOrFallback(
                    out position, out rotation, "特种机动开场");
                return true;
            }

            return ScriptAnimHomeResolver.TryResolveClipExitPose(
                previous, actor, resolver, out position, out rotation);
        }

        public static bool TryResolveBezierCornerExitPose(
            TimelineClip timelineClip,
            BezierCornerClip clipAsset,
            IExposedPropertyTable resolver,
            out Vector3 position,
            out Quaternion rotation,
            PathMoveActor actor = null)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            if (clipAsset?.Data == null || resolver == null)
                return false;

            PathNode corner = clipAsset.CornerNode.Resolve(resolver);
            PathNode prev = clipAsset.PrevNode.Resolve(resolver);
            PathNode next = clipAsset.NextNode.Resolve(resolver);

            if (!TryResolveIncomingPose(timelineClip, actor, resolver, out Vector3 incomingPos, out Quaternion incomingRot))
                return false;

            return BezierCornerSampler.TryGetEndPose(
                actor, clipAsset.Data, corner, prev, next, incomingPos, incomingRot,
                out position, out rotation);
        }

        public static bool TryResolveReverseUTurnExitPose(
            TimelineClip timelineClip,
            ReverseUTurnClip clipAsset,
            IExposedPropertyTable resolver,
            out Vector3 position,
            out Quaternion rotation,
            PathMoveActor actor = null)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            if (clipAsset?.Data == null || resolver == null)
                return false;

            PathNode corner = clipAsset.CornerNode.Resolve(resolver);
            PathNode prev = clipAsset.PrevNode.Resolve(resolver);
            PathNode next = clipAsset.NextNode.Resolve(resolver);

            if (!TryResolveIncomingPose(timelineClip, actor, resolver, out Vector3 incomingPos, out Quaternion incomingRot))
                return false;

            return ReverseUTurnSampler.TryGetEndPose(
                actor, clipAsset.Data, corner, prev, next, incomingPos, incomingRot,
                out position, out rotation);
        }

        /// <summary>解析三点转向 / 贝塞尔弯 / 倒车掉头等 PathMove 系特种机动 Clip 的结束位姿。</summary>
        public static bool TryResolvePathManeuverExitPose(
            TimelineClip timelineClip,
            PathMoveActor actor,
            IExposedPropertyTable resolver,
            out Vector3 position,
            out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            if (timelineClip?.asset == null)
                return false;

            if (timelineClip.asset is ThreePointTurnClip threePoint)
                return ScriptAnimHomeResolver.TryResolveThreePointTurnExitPose(
                    timelineClip, threePoint, resolver, out position, out rotation, actor);

            if (timelineClip.asset is BezierCornerClip bezier)
                return TryResolveBezierCornerExitPose(
                    timelineClip, bezier, resolver, out position, out rotation, actor);

            if (timelineClip.asset is ReverseUTurnClip uTurn)
                return TryResolveReverseUTurnExitPose(
                    timelineClip, uTurn, resolver, out position, out rotation, actor);

            return false;
        }

        public static bool IsPathManeuverClip(TimelineClip clip)
        {
            if (clip?.asset == null)
                return false;

            return clip.asset is ThreePointTurnClip
                   || clip.asset is BezierCornerClip
                   || clip.asset is ReverseUTurnClip;
        }
    }
}
