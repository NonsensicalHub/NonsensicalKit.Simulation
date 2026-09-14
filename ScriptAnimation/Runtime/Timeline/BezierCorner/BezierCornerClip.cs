using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 路网拐点贝塞尔直角弯。须 ExposedReference 绑定 PrevNode、CornerNode、NextNode。
    /// </summary>
    [System.Serializable]
    public class BezierCornerClip : PlayableAsset, ITimelineClipAsset, IDurationResolvable
    {
        [HideInInspector]
        public ExposedReference<PathNode> PrevNode;

        [HideInInspector]
        public ExposedReference<PathNode> CornerNode;

        [HideInInspector]
        public ExposedReference<PathNode> NextNode;

        public BezierCornerClipData Data = new BezierCornerClipData();

        public ClipCaps clipCaps => ClipCaps.ClipIn | ClipCaps.SpeedMultiplier;

        public override double duration =>
            Data != null ? BezierCornerSampler.EstimateDuration(Data, null, null, null, null, Vector3.zero, Quaternion.identity) : 1.5;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<BezierCornerBehaviour>.Create(graph);
            BezierCornerBehaviour behaviour = playable.GetBehaviour();
            behaviour.Data = Data;
            behaviour.ClipAsset = this;
            return playable;
        }

        public float ResolveDuration(in DurationResolveContext context)
        {
            var actor = context.TrackBinding as PathMoveActor;
            if (actor == null || Data == null || context.Resolver == null)
                return -1f;

            PathNode corner = CornerNode.Resolve(context.Resolver);
            PathNode prev = PrevNode.Resolve(context.Resolver);
            PathNode next = NextNode.Resolve(context.Resolver);

            if (!ManeuverHomeUtility.TryResolveIncomingPose(
                    context.TimelineClip, actor, context.Resolver,
                    out Vector3 pos, out Quaternion rot))
                return -1f;

            return BezierCornerSampler.EstimateDuration(
                Data, actor, corner, prev, next, pos, rot);
        }
    }
}
