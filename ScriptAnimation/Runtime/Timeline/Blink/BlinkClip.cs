using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 间隔显隐 Clip：在 Clip 时长内快速切换绑定 <see cref="BlinkAnim"/> 目标的显隐。
    /// </summary>
    [System.Serializable]
    public class BlinkClip : PlayableAsset, ITimelineClipAsset, IDurationResolvable
    {
        public BlinkClipData Data = new BlinkClipData();

        public ClipCaps clipCaps => ClipCaps.ClipIn | ClipCaps.SpeedMultiplier;

        public override double duration => BlinkSampler.EstimateDuration(Data);

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<BlinkBehaviour>.Create(graph);
            BlinkBehaviour behaviour = playable.GetBehaviour();
            behaviour.Data = Data;
            behaviour.ClipAsset = this;
            return playable;
        }

        public float ResolveDuration(in DurationResolveContext context)
        {
            if (Data == null || !Data.AutoSyncDuration)
                return -1f;

            if (context.TrackBinding is not BlinkAnim)
                return -1f;

            return BlinkSampler.EstimateDuration(Data);
        }
    }
}
