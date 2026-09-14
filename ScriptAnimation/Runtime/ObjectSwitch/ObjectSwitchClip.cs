using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 对象切换 Clip：在生效区间内只显示指定索引的对象。
    /// </summary>
    [System.Serializable]
    public class ObjectSwitchClip : PlayableAsset, ITimelineClipAsset
    {
        public const double DefaultDuration = 1.0;

        public ObjectSwitchClipData Data = new ObjectSwitchClipData();

        public ClipCaps clipCaps => ClipCaps.None;

        public override double duration => DefaultDuration;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<ObjectSwitchBehaviour>.Create(graph);
            playable.GetBehaviour().Data = Data;
            return playable;
        }
    }
}
