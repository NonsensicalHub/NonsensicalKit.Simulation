using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation
{
    [Serializable]
    public class BezierDualCornerBehaviour : PlayableBehaviour
    {
        public BezierDualCornerClipData Data = new BezierDualCornerClipData();

        [NonSerialized] public BezierDualCornerClip ClipAsset;

        [NonSerialized] public PathNode CornerNodeA;
        [NonSerialized] public PathNode CornerNodeB;
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
                CornerNodeA = ClipAsset.CornerNodeA.Resolve(resolver);
                CornerNodeB = ClipAsset.CornerNodeB.Resolve(resolver);
                PrevNode = ClipAsset.PrevNode.Resolve(resolver);
                NextNode = ClipAsset.NextNode.Resolve(resolver);
            }

            if (!ManeuverHomeUtility.TryResolveIncomingPose(
                    timelineClip, actor, resolver, out IncomingPosition, out IncomingRotation))
            {
                ResolveFailed = true;
                PoseResolved = true;
                Debug.LogWarning("[BezierDualCorner] 无法解析开场位姿", actor);
                return false;
            }

            if (!BezierDualCornerSampler.TryBuildPlan(
                    actor, Data, CornerNodeA, CornerNodeB, PrevNode, NextNode,
                    IncomingPosition, IncomingRotation, out _))
            {
                ResolveFailed = true;
                PoseResolved = true;
                Debug.LogWarning(
                    "[BezierDualCorner] 无法生成双拐点贝塞尔路径（请配置 Prev/CornerA/CornerB/Next 节点）。",
                    actor);
                return false;
            }

            PoseResolved = true;
            return true;
        }
    }
}
