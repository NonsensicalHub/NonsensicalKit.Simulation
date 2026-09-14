using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 相对 ScriptAnim Clip 查询 <see cref="WorldRotationLockClip"/> 的时间关系。
    /// Lock 与车体无组件耦合：<see cref="WorldRotationLockAnim"/> 可挂在任意物体上，
    /// 专用轨绑定它；移动 Mixer 仅当其 <see cref="WorldRotationLockAnim.Target"/> 属于
    /// 当前移动 Actor 层级时，在位移之后再写一次旋转（防止父节点转动带偏）。
    /// </summary>
    public static class WorldRotationLockUtility
    {
        /// <summary>
        /// 是否存在已启用、且 Target 属于车体轨 Actor 层级的 Lock，
        /// 与该车体 Clip 的时间范围重叠。
        /// </summary>
        public static bool IsActiveDuringClip(TimelineClip bodyClip, PlayableDirector director)
        {
            if (bodyClip == null || director == null)
                return false;

            TrackAsset bodyTrack = bodyClip.GetParentTrack();
            TimelineAsset timeline = bodyTrack != null ? bodyTrack.timelineAsset : null;
            if (timeline == null)
                return false;

            var actor = director.GetGenericBinding(bodyTrack) as ScriptAnimActor;
            if (actor == null)
                return false;

            return IsActiveDuringRange(timeline, bodyClip.start, bodyClip.end, director, actor);
        }

        /// <summary>
        /// 在指定时刻取 Target 属于 <paramref name="actor"/> 层级、且正在生效的 Lock。
        /// 返回绑定的 LockAnim 与 Clip 数据，供位移后再写一次旋转。
        /// </summary>
        public static bool TryGetActiveLockForActor(
            TimelineAsset timeline,
            double time,
            PlayableDirector director,
            ScriptAnimActor actor,
            out WorldRotationLockAnim lockAnim,
            out WorldRotationLockClipData data)
        {
            lockAnim = null;
            data = null;
            if (timeline == null || director == null || actor == null)
                return false;

            Transform root = actor.transform;
            foreach (TrackAsset track in timeline.GetOutputTracks())
            {
                var bound = director.GetGenericBinding(track) as WorldRotationLockAnim;
                if (bound == null || bound.Target == null)
                    continue;
                if (!bound.Target.IsChildOf(root))
                    continue;

                foreach (TimelineClip clip in track.GetClips())
                {
                    if (clip == null || clip.asset is not WorldRotationLockClip lockAsset)
                        continue;
                    if (lockAsset.Data == null || !lockAsset.Data.Enabled)
                        continue;
                    if (time + 1e-9 < clip.start || time >= clip.end - 1e-9)
                        continue;

                    lockAnim = bound;
                    data = lockAsset.Data;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// [start, end) 是否与任一已启用、且 Target 属于
        /// <paramref name="actor"/> 层级的 Lock 时间重叠。
        /// </summary>
        public static bool IsActiveDuringRange(
            TimelineAsset timeline,
            double start,
            double end,
            PlayableDirector director,
            ScriptAnimActor actor)
        {
            if (timeline == null || director == null || actor == null || end <= start + 1e-12)
                return false;

            Transform root = actor.transform;
            foreach (TrackAsset track in timeline.GetOutputTracks())
            {
                var bound = director.GetGenericBinding(track) as WorldRotationLockAnim;
                if (bound == null || bound.Target == null)
                    continue;
                if (!bound.Target.IsChildOf(root))
                    continue;

                foreach (TimelineClip clip in track.GetClips())
                {
                    if (clip == null || clip.asset is not WorldRotationLockClip lockAsset)
                        continue;
                    if (lockAsset.Data == null || !lockAsset.Data.Enabled)
                        continue;
                    if (clip.start >= end - 1e-9 || clip.end <= start + 1e-9)
                        continue;
                    return true;
                }
            }

            return false;
        }
    }
}
