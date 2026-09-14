using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 标记需锁定世界旋转的时间段（放在绑定 <see cref="WorldRotationLockAnim"/> 的 <see cref="ScriptAnimTrackBase"/> 上，可与移动轨重叠）。
    /// 开启且播放时间落在本 Clip 内时：将目标世界旋转设为 <see cref="WorldRotationLockClipData.LockEulerAngles"/>；关闭或未覆盖时不控制、不还原。
    /// </summary>
    [System.Serializable]
    public class WorldRotationLockClip : PlayableAsset, ITimelineClipAsset
    {
        public WorldRotationLockClipData Data = new WorldRotationLockClipData();

        public ClipCaps clipCaps => ClipCaps.None;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<WorldRotationLockBehaviour>.Create(graph);
            playable.GetBehaviour().Data = Data;
            return playable;
        }
    }
}
