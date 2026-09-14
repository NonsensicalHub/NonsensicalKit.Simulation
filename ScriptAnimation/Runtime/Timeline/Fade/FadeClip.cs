using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 透明度渐变 Clip：驱动绑定的 <see cref="FadeAnim"/>，批量改子节点材质 alpha。
    /// </summary>
    [System.Serializable]
    public class FadeClip : PlayableAsset, ITimelineClipAsset, IDurationResolvable
    {
        public FadeClipData Data = new FadeClipData();

        public ClipCaps clipCaps => ClipCaps.ClipIn | ClipCaps.SpeedMultiplier;

        public override double duration => FadeSampler.EstimateDuration(Data);

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<FadeBehaviour>.Create(graph);
            FadeBehaviour behaviour = playable.GetBehaviour();
            behaviour.Data = Data;
            behaviour.ClipAsset = this;
            return playable;
        }

        public float ResolveDuration(in DurationResolveContext context)
        {
            if (Data == null || !Data.AutoSyncDuration)
                return -1f;

            if (context.TrackBinding is not FadeAnim)
                return -1f;

            return FadeSampler.EstimateDuration(Data);
        }
    }
}
