using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation
{
    [System.Serializable]
    public class PathMoveClip : PlayableAsset, ITimelineClipAsset, IDurationResolvable
    {
        // HideInInspector：避免 Timeline 右键生成「Add Path Move Clip From PathNode」等选对象菜单。
        [HideInInspector]
        [Tooltip("场景路网（经 PlayableDirector ExposedReference 绑定）")]
        public ExposedReference<PathNetwork> Network;

        [HideInInspector]
        [Tooltip("起点节点（必填）：clip 开头物体一定在此节点")]
        public ExposedReference<PathNode> StartNode;

        [HideInInspector]
        [Tooltip("终点节点（必填）")]
        public ExposedReference<PathNode> EndNode;

        public PathMoveClipData Data = new PathMoveClipData();

        public ClipCaps clipCaps => ClipCaps.ClipIn | ClipCaps.SpeedMultiplier;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<PathMoveBehaviour>.Create(graph);
            PathMoveBehaviour behaviour = playable.GetBehaviour();
            behaviour.Data = Data;
            behaviour.ClipAsset = this;

            IExposedPropertyTable resolver = graph.GetResolver();
            behaviour.Network = Network.Resolve(resolver);
            behaviour.StartNode = StartNode.Resolve(resolver);
            behaviour.EndNode = EndNode.Resolve(resolver);

            return playable;
        }

        public float ResolveDuration(in DurationResolveContext context)
        {
            var actor = context.TrackBinding as PathMoveActor;
            if (actor == null || Data == null)
                return -1f;

            PathNetwork network = null;
            PathNode start = null;
            PathNode end = null;
            if (context.Resolver != null)
            {
                network = Network.Resolve(context.Resolver);
                start = StartNode.Resolve(context.Resolver);
                end = EndNode.Resolve(context.Resolver);
            }

            var points = new List<Vector3>(32);
            var pauses = new List<float>(32);
            if (!PathMoveSampler.TryResolveWorldPoints(
                    network, start, end, Data, points, actor.PathOffset, pauses))
                return -1f;

            Quaternion incoming = ScriptAnimHomeResolver.ResolvePathMoveIncomingRotation(
                context.TimelineClip, actor, context.Resolver, points,
                Data != null && Data.ReverseFacing);
            return PathMoveSampler.EstimateDuration(Data, actor, points, incoming, pauses);
        }
    }
}
