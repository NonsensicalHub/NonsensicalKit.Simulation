using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// ScriptAnimation 轨道基类：播放结束处理 / 最小时长 / 瞬间动作占位帧 / GatherProperties。
    /// Clip 分发在 Mixer 中：<see cref="ScriptMovementTrack"/>（可混排）与
    /// <see cref="ScriptDedicatedTrack"/>（业务上一条轨实例通常只放一种 Clip）。
    /// </summary>
    public abstract class ScriptAnimTrackBase : TrackAsset
    {
        /// <summary>
        /// Timeline 结束后的位姿处理（语义同 ActivationTrack 的 Post-playback state）。
        /// </summary>
        public enum PostPlaybackState
        {
            /// <summary>还原 Timeline 开始时捕获的位姿。</summary>
            Revert,
            /// <summary>保持 Timeline 结束时的位姿。</summary>
            LeaveAsIs,
        }

        [SerializeField]
        [Tooltip("Timeline 结束后：Revert = 还原开始位姿；LeaveAsIs = 保持结束位姿")]
        protected PostPlaybackState m_PostPlaybackState = PostPlaybackState.Revert;

        [SerializeField]
        [Min(0f)]
        [Tooltip("写入时长时 TimelineClip.duration 的最小值（秒），可为 0。")]
        float m_MinClipDuration = DurationUtility.DefaultMinClipDuration;

        [SerializeField]
        [Min(1)]
        [Tooltip("瞬间动作 Clip 在 Timeline 上的占位帧数（如 Teleport、Duration=0 的 PoseChange / OpenBox）。")]
        int m_InstantHoldFrames = DurationUtility.DefaultInstantHoldFrames;

        public PostPlaybackState postPlaybackState
        {
            get => m_PostPlaybackState;
            set => m_PostPlaybackState = value;
        }

        /// <summary>本轨 Clip 最小时长（秒）。</summary>
        public float MinClipDuration
        {
            get => Mathf.Max(0f, m_MinClipDuration);
            set => m_MinClipDuration = Mathf.Max(0f, value);
        }

        /// <summary>瞬间动作 Clip 在 Timeline 上的占位帧数。</summary>
        public int InstantHoldFrames
        {
            get => Mathf.Max(1, m_InstantHoldFrames);
            set => m_InstantHoldFrames = Mathf.Max(1, value);
        }

        /// <summary>从所属 ScriptAnim 轨解析最小时长；无则用默认值。</summary>
        public static float ResolveMinClipDuration(TimelineClip clip)
        {
            if (clip != null && clip.GetParentTrack() is ScriptAnimTrackBase track)
                return track.MinClipDuration;
            return DurationUtility.DefaultMinClipDuration;
        }

        /// <summary>从所属 ScriptAnim 轨解析瞬间占位帧；无则用默认值。</summary>
        public static int ResolveInstantHoldFrames(TimelineClip clip)
        {
            if (clip != null && clip.GetParentTrack() is ScriptAnimTrackBase track)
                return track.InstantHoldFrames;
            return DurationUtility.DefaultInstantHoldFrames;
        }

        /// <summary>将时长钳到轨最小时长；负时长原样返回。</summary>
        public static float ClampDuration(TimelineClip clip, float duration)
        {
            if (duration < 0f)
                return duration;
            return Mathf.Max(ResolveMinClipDuration(clip), duration);
        }

#if UNITY_EDITOR
        public override void GatherProperties(PlayableDirector director, IPropertyCollector driver)
        {
            var binding = director.GetGenericBinding(this) as ScriptAnimActor;
            if (binding == null)
                return;

            var entries = new List<ScriptAnimControlledPose.Entry>(8);
            ScriptAnimControlledPose.Collect(binding, entries);
            for (int i = 0; i < entries.Count; i++)
            {
                ScriptAnimControlledPose.Entry e = entries[i];
                if (e.Transform == null)
                    continue;

                GameObject go = e.Transform.gameObject;
                if ((e.Channels & ScriptAnimPoseChannels.LocalPosition) != 0)
                    driver.AddFromName<Transform>(go, "m_LocalPosition");
                if ((e.Channels & ScriptAnimPoseChannels.LocalRotation) != 0)
                    driver.AddFromName<Transform>(go, "m_LocalRotation");
                if ((e.Channels & ScriptAnimPoseChannels.LocalScale) != 0)
                    driver.AddFromName<Transform>(go, "m_LocalScale");
            }

            // 非 Transform 通道：登记组件序列化字段，便于 Timeline 预览还原
            if (binding is FadeAnim)
                driver.AddFromName<FadeAnim>(binding.gameObject, "m_currentAlpha");
            if (binding is BlinkAnim blink)
            {
                driver.AddFromName<BlinkAnim>(binding.gameObject, "m_visible");
                GameObject blinkTarget = blink.Target;
                if (blinkTarget != null && blinkTarget != blink.gameObject)
                    driver.AddFromName(blinkTarget, "m_IsActive");
            }

            // ObjectSwitchAnim：刻意不登记候选物体的 m_IsActive。
            // Timeline 预览会每帧还原激活状态，导致 SetActive(false) 立刻被盖掉。

            if (binding is FilmWrapAnim)
                driver.AddFromName<FilmWrapAnim>(binding.gameObject, "m_currentProgress");
        }
#endif
    }
}
