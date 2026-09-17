using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 路网双拐点贝塞尔弯。须 ExposedReference 绑定 PrevNode、CornerNodeA、CornerNodeB、NextNode；结束于 NextNode。
    /// </summary>
    [System.Serializable]
    public class BezierDualCornerClip : PlayableAsset, ITimelineClipAsset, IDurationResolvable
    {
        [HideInInspector]
        public ExposedReference<PathNode> PrevNode;

        [HideInInspector]
        public ExposedReference<PathNode> CornerNodeA;

        [HideInInspector]
        public ExposedReference<PathNode> CornerNodeB;

        [HideInInspector]
        public ExposedReference<PathNode> NextNode;

        public BezierDualCornerClipData Data = new BezierDualCornerClipData();

        public ClipCaps clipCaps => ClipCaps.ClipIn | ClipCaps.SpeedMultiplier;

        public override double duration =>
            Data != null
                ? BezierDualCornerSampler.EstimateDuration(
                    Data, null, null, null, null, null, Vector3.zero, Quaternion.identity)
                : 2.0;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<BezierDualCornerBehaviour>.Create(graph);
            BezierDualCornerBehaviour behaviour = playable.GetBehaviour();
            behaviour.Data = Data;
            behaviour.ClipAsset = this;
            return playable;
        }

        public float ResolveDuration(in DurationResolveContext context)
        {
            var actor = context.TrackBinding as PathMoveActor;
            if (actor == null || Data == null || context.Resolver == null)
                return -1f;

            PathNode cornerA = CornerNodeA.Resolve(context.Resolver);
            PathNode cornerB = CornerNodeB.Resolve(context.Resolver);
            PathNode prev = PrevNode.Resolve(context.Resolver);
            PathNode next = NextNode.Resolve(context.Resolver);

            if (!ManeuverHomeUtility.TryResolveIncomingPose(
                    context.TimelineClip, actor, context.Resolver,
                    out Vector3 pos, out Quaternion rot))
                return -1f;

            return BezierDualCornerSampler.EstimateDuration(
                Data, actor, cornerA, cornerB, prev, next, pos, rot);
        }
    }
}
