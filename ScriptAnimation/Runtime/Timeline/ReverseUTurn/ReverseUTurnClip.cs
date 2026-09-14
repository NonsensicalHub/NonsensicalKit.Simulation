using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 路网拐点倒车掉头。须 ExposedReference 绑定 PrevNode、CornerNode、NextNode。
    /// </summary>
    [System.Serializable]
    public class ReverseUTurnClip : PlayableAsset, ITimelineClipAsset, IDurationResolvable
    {
        [HideInInspector]
        public ExposedReference<PathNode> PrevNode;

        [HideInInspector]
        public ExposedReference<PathNode> CornerNode;

        [HideInInspector]
        public ExposedReference<PathNode> NextNode;

        public ReverseUTurnClipData Data = new ReverseUTurnClipData();

        public ClipCaps clipCaps => ClipCaps.ClipIn | ClipCaps.SpeedMultiplier;

        public override double duration =>
            Data != null ? ReverseUTurnSampler.EstimateDuration(Data, null, null, null, null, Vector3.zero, Quaternion.identity) : 2.5;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<ReverseUTurnBehaviour>.Create(graph);
            ReverseUTurnBehaviour behaviour = playable.GetBehaviour();
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

            return ReverseUTurnSampler.EstimateDuration(
                Data, actor, corner, prev, next, pos, rot);
        }
    }
}
