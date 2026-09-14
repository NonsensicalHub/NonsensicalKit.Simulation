using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 摇摆翻转 Clip：驱动绑定的 <see cref="SwingFlipAnim"/>，播放摇摆→翻转→再摇摆→翻回。
    /// </summary>
    [System.Serializable]
    public class SwingFlipClip : PlayableAsset, ITimelineClipAsset, IDurationResolvable
    {
        public SwingFlipClipData Data = new SwingFlipClipData();

        public ClipCaps clipCaps => ClipCaps.ClipIn | ClipCaps.SpeedMultiplier;

        public override double duration => SwingFlipSampler.EstimateDuration(null, Data);

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<SwingFlipBehaviour>.Create(graph);
            SwingFlipBehaviour behaviour = playable.GetBehaviour();
            behaviour.Data = Data;
            behaviour.ClipAsset = this;
            return playable;
        }

        public float ResolveDuration(in DurationResolveContext context)
        {
            if (Data == null || !Data.AutoSyncDuration)
                return -1f;

            if (context.TrackBinding is not SwingFlipAnim anim)
                return -1f;

            return SwingFlipSampler.EstimateDuration(anim, Data);
        }
    }
}
