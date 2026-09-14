using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 窄道三点转向 Clip：只配左/右转，开场朝向取前序路径结束朝向，
    /// 自动走出「前进 → 对侧倒车转 90° → 再前进回到开场点」。
    /// </summary>
    [System.Serializable]
    public class ThreePointTurnClip : PlayableAsset, ITimelineClipAsset, IDurationResolvable
    {
        public ThreePointTurnClipData Data = new ThreePointTurnClipData();

        public ClipCaps clipCaps => ClipCaps.ClipIn | ClipCaps.SpeedMultiplier;

        public override double duration =>
            Data != null ? ThreePointTurnSampler.EstimateDuration(Data, null) : 2.0;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<ThreePointTurnBehaviour>.Create(graph);
            ThreePointTurnBehaviour behaviour = playable.GetBehaviour();
            behaviour.Data = Data;
            behaviour.ClipAsset = this;
            return playable;
        }

        public float ResolveDuration(in DurationResolveContext context)
        {
            var actor = context.TrackBinding as PathMoveActor;
            if (actor == null || Data == null)
                return -1f;

            return ThreePointTurnSampler.EstimateDuration(Data, actor);
        }
    }
}
