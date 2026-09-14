using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 缠膜 Clip：驱动绑定的 <see cref="FilmWrapAnim"/>，在 Clip 时长内插值螺旋上升进度。
    /// </summary>
    [System.Serializable]
    public class FilmWrapClip : PlayableAsset, ITimelineClipAsset, IDurationResolvable
    {
        public FilmWrapClipData Data = new FilmWrapClipData();

        public ClipCaps clipCaps => ClipCaps.ClipIn | ClipCaps.SpeedMultiplier;

        public override double duration => FilmWrapSampler.EstimateDuration(Data);

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<FilmWrapBehaviour>.Create(graph);
            FilmWrapBehaviour behaviour = playable.GetBehaviour();
            behaviour.Data = Data;
            behaviour.ClipAsset = this;
            return playable;
        }

        public float ResolveDuration(in DurationResolveContext context)
        {
            if (Data == null || !Data.AutoSyncDuration)
                return -1f;

            if (context.TrackBinding is not FilmWrapAnim)
                return -1f;

            return FilmWrapSampler.EstimateDuration(Data);
        }
    }
}
