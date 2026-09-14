using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 瞬移 Clip：瞬间将 <see cref="PathMoveActor"/> 放到目标 PathNode 或世界坐标。
    /// 用于同一对象循环复用（动画结束后瞬移回起点再播）。
    /// </summary>
    [System.Serializable]
    public class TeleportClip : PlayableAsset, ITimelineClipAsset, IDurationResolvable
    {
        [HideInInspector]
        [Tooltip("瞬移目标节点；也可在 Data 中改用世界坐标")]
        public ExposedReference<PathNode> TargetNode;

        public TeleportClipData Data = new TeleportClipData();

        public ClipCaps clipCaps => ClipCaps.ClipIn | ClipCaps.SpeedMultiplier;

        /// <summary>新建 Clip 时的默认占位时长（HoldFrames）；之后可在 Timeline 上自由拖拽。</summary>
        public override double duration => TeleportSampler.EstimateDuration();

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<TeleportBehaviour>.Create(graph);
            TeleportBehaviour behaviour = playable.GetBehaviour();
            behaviour.Data = Data;
            behaviour.ClipAsset = this;

            IExposedPropertyTable resolver = graph.GetResolver();
            behaviour.TargetNode = TargetNode.Resolve(resolver);

            return playable;
        }

        public float ResolveDuration(in DurationResolveContext context)
        {
            // 默认不自动改时长，便于 Timeline 上自由调整占位长度
            if (Data == null || !Data.AutoSyncDuration)
                return -1f;

            if (context.TrackBinding is not PathMoveActor)
                return -1f;

            PathNode target = null;
            if (context.Resolver != null)
                target = TargetNode.Resolve(context.Resolver);

            // 时长估算只需落点可解析；朝向在采样时再定
            if (!TeleportSampler.TryResolvePose(
                    target, Data, Quaternion.identity, out _, out _))
                return -1f;

            return TeleportSampler.EstimateDuration(context.TimelineClip);
        }
    }
}
