using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation
{
    [Serializable]
    public class BezierCornerBehaviour : PlayableBehaviour
    {
        public BezierCornerClipData Data = new BezierCornerClipData();

        [NonSerialized] public BezierCornerClip ClipAsset;

        [NonSerialized] public PathNode CornerNode;
        [NonSerialized] public PathNode PrevNode;
        [NonSerialized] public PathNode NextNode;

        [NonSerialized] public Vector3 IncomingPosition;
        [NonSerialized] public Quaternion IncomingRotation;
        [NonSerialized] public bool PoseResolved;
        [NonSerialized] public bool ResolveFailed;

        public override void OnGraphStart(Playable playable)
        {
            PoseResolved = false;
            ResolveFailed = false;
        }

        public override void OnBehaviourPlay(Playable playable, FrameData info)
        {
            PoseResolved = false;
        }

        public bool EnsurePoseResolved(
            TimelineClip timelineClip,
            PathMoveActor actor,
            IExposedPropertyTable resolver)
        {
            if (PoseResolved)
                return !ResolveFailed;
            if (ResolveFailed || actor == null || Data == null)
                return false;

            if (ClipAsset != null && resolver != null)
            {
                CornerNode = ClipAsset.CornerNode.Resolve(resolver);
                PrevNode = ClipAsset.PrevNode.Resolve(resolver);
                NextNode = ClipAsset.NextNode.Resolve(resolver);
            }

            if (!ManeuverHomeUtility.TryResolveIncomingPose(
                    timelineClip, actor, resolver, out IncomingPosition, out IncomingRotation))
            {
                ResolveFailed = true;
                PoseResolved = true;
                Debug.LogWarning("[BezierCorner] 无法解析开场位姿", actor);
                return false;
            }

            if (!BezierCornerSampler.TryBuildPlan(
                    actor, Data, CornerNode, PrevNode, NextNode,
                    IncomingPosition, IncomingRotation, out _))
            {
                ResolveFailed = true;
                PoseResolved = true;
                Debug.LogWarning("[BezierCorner] 无法生成贝塞尔弯路径（请配置 Prev/Corner/Next 节点）。", actor);
                return false;
            }

            PoseResolved = true;
            return true;
        }
    }
}
