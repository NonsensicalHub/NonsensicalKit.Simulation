using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 原地旋转 Clip：默认位置保持前序终点；可选仅旋转不写位置。
    /// 朝向由起始偏航角 + 方向×角度 × 进度直接算出。
    /// 默认一圈（360°）；时长按角速度自动同步。
    /// </summary>
    [System.Serializable]
    public class RotateClip : PlayableAsset, ITimelineClipAsset, IDurationResolvable
    {
        public RotateClipData Data = new RotateClipData();

        public ClipCaps clipCaps => ClipCaps.ClipIn | ClipCaps.SpeedMultiplier;

        /// <summary>新建 Clip 时的默认时长（按 360° / 90°/s ≈ 4s）。</summary>
        public override double duration =>
            Data != null ? RotateSampler.EstimateDuration(Data, null) : 4.0;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<RotateBehaviour>.Create(graph);
            RotateBehaviour behaviour = playable.GetBehaviour();
            behaviour.Data = Data;
            behaviour.ClipAsset = this;
            return playable;
        }

        public float ResolveDuration(in DurationResolveContext context)
        {
            var actor = context.TrackBinding as PathMoveActor;
            if (actor == null || Data == null)
                return -1f;

            return RotateSampler.EstimateDuration(Data, actor);
        }
    }
}
