using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 堆垛机取/放货 Clip：机身停在当前货位，仅货叉沿本地轴运动。
    /// 通常接在 <see cref="StackerClip"/> 到位之后。
    /// </summary>
    [System.Serializable]
    public class StackerForkClip : PlayableAsset, ITimelineClipAsset, IDurationResolvable
    {
        public StackerForkClipData Data = new StackerForkClipData();

        public ClipCaps clipCaps => ClipCaps.ClipIn | ClipCaps.SpeedMultiplier;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<StackerForkBehaviour>.Create(graph);
            StackerForkBehaviour behaviour = playable.GetBehaviour();
            behaviour.Data = Data;
            behaviour.ClipAsset = this;
            return playable;
        }

        public float ResolveDuration(in DurationResolveContext context)
        {
            var anim = context.TrackBinding as StackerAnim;
            if (anim == null || Data == null)
                return -1f;

            return StackerForkSampler.EstimateDuration(anim, Data);
        }
    }
}
