using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 姿态改变 Clip：驱动绑定的 <see cref="PoseChangeAnim"/> / <see cref="PoseChangeAnimMax"/>，在 Clip 时长内变到命名姿态。
    /// 默认可拖拽 Clip 控制时长；勾选 AutoSyncDuration 后按目标用时回写。
    /// </summary>
    [System.Serializable]
    public class PoseChangeClip : PlayableAsset, ITimelineClipAsset, IDurationResolvable
    {
        public PoseChangeClipData Data = new PoseChangeClipData();

        public ClipCaps clipCaps => ClipCaps.ClipIn | ClipCaps.SpeedMultiplier;

        /// <summary>新建 Clip 时的占位时长；真实时长由 OnCreate / 自动同步写入。</summary>
        public override double duration => PoseChangeSampler.EstimateClipDuration(Data);

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<PoseChangeBehaviour>.Create(graph);
            PoseChangeBehaviour behaviour = playable.GetBehaviour();
            behaviour.Data = Data;
            behaviour.ClipAsset = this;
            return playable;
        }

        public float ResolveDuration(in DurationResolveContext context)
        {
            if (Data == null || !Data.AutoSyncDuration)
                return -1f;

            if (context.TrackBinding is not IPoseChangeActor &&
                context.TrackBinding is not PoseChangeAnimMax)
                return -1f;

            return PoseChangeSampler.EstimateClipDuration(Data, context.TimelineClip);
        }
    }
}
