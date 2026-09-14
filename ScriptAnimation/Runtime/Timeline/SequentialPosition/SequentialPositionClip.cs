using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 依次换位 Clip：驱动绑定的 <see cref="SequentialPositionAnim"/>（路点间隔/控制对象均在组件上）。
    /// </summary>
    [System.Serializable]
    public class SequentialPositionClip : PlayableAsset, ITimelineClipAsset, IDurationResolvable
    {
        public SequentialPositionClipData Data = new SequentialPositionClipData();

        public ClipCaps clipCaps => ClipCaps.ClipIn | ClipCaps.SpeedMultiplier;

        /// <summary>
        /// 仅作 Timeline 创建占位；真实时长由添加菜单 / OnCreate 按组件配置写入。
    /// </summary>
        public override double duration => 1.0;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<SequentialPositionBehaviour>.Create(graph);
            SequentialPositionBehaviour behaviour = playable.GetBehaviour();
            behaviour.Data = Data;
            behaviour.ClipAsset = this;
            return playable;
        }

        public float ResolveDuration(in DurationResolveContext context)
        {
            if (Data == null || !Data.AutoSyncDuration)
                return -1f;

            if (context.TrackBinding is not SequentialPositionAnim anim)
                return -1f;

            float estimated = SequentialPositionSampler.EstimateDuration(anim);
            return estimated > 1e-6f ? estimated : -1f;
        }
    }
}
