using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 堆垛机货位移动 Clip：起/终点为货位坐标（层/列/排/深），
    /// 经绑定物体上的 WarehouseManager 解析世界位置后双轴加减速移动。
    /// </summary>
    [System.Serializable]
    public class StackerClip : PlayableAsset, ITimelineClipAsset, IDurationResolvable
    {
        public StackerClipData Data = new StackerClipData();

        public ClipCaps clipCaps => ClipCaps.ClipIn | ClipCaps.SpeedMultiplier;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<StackerBehaviour>.Create(graph);
            StackerBehaviour behaviour = playable.GetBehaviour();
            behaviour.Data = Data;
            behaviour.ClipAsset = this;
            return playable;
        }

        public float ResolveDuration(in DurationResolveContext context)
        {
            var anim = context.TrackBinding as StackerAnim;
            if (anim == null || Data == null)
                return -1f;

            if (!StackerSampler.TryResolveEndWorld(anim, Data, out Vector3 endPos))
                return -1f;

            var fallback = StackerSampler.ResolvePreviousClipEnd(anim, context.TimelineClip);
            if (!StackerSampler.TryResolveStartWorld(anim, Data, fallback, out Vector3 startPos))
                return -1f;

            return StackerSampler.EstimateDuration(anim, Data, startPos, endPos);
        }
    }
}
