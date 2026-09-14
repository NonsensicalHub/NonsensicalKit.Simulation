using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 开箱 Clip：驱动绑定的 <see cref="OpenBoxAnim"/>，在 Clip 时长内插值所选阶段。
    /// 默认可拖拽 Clip 控制时长；勾选 AutoSyncDuration 后按目标用时回写。
    /// </summary>
    [System.Serializable]
    public class OpenBoxClip : PlayableAsset, ITimelineClipAsset, IDurationResolvable
    {
        public OpenBoxClipData Data = new OpenBoxClipData();

        public ClipCaps clipCaps => ClipCaps.ClipIn | ClipCaps.SpeedMultiplier;

        public override double duration => OpenBoxSampler.EstimateClipDuration(Data);

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<OpenBoxBehaviour>.Create(graph);
            OpenBoxBehaviour behaviour = playable.GetBehaviour();
            behaviour.Data = Data;
            behaviour.ClipAsset = this;
            return playable;
        }

        public float ResolveDuration(in DurationResolveContext context)
        {
            if (Data == null || !Data.AutoSyncDuration)
                return -1f;

            if (context.TrackBinding is not OpenBoxAnim)
                return -1f;

            return OpenBoxSampler.EstimateClipDuration(Data, context.TimelineClip);
        }
    }
}
