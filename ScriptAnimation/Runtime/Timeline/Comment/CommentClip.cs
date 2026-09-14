using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 注释 Clip：在 ScriptAnim 轨上添加文字说明，不驱动 <see cref="ScriptAnimActor"/>。
    /// </summary>
    [System.Serializable]
    public class CommentClip : PlayableAsset, ITimelineClipAsset
    {
        public const double DefaultDuration = 2.0;

        public CommentClipData Data = new CommentClipData();

        public ClipCaps clipCaps => ClipCaps.None;

        public override double duration => DefaultDuration;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<CommentBehaviour>.Create(graph);
            CommentBehaviour behaviour = playable.GetBehaviour();
            behaviour.Data = Data;
            return playable;
        }
    }
}
