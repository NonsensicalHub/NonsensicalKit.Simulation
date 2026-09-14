using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 专用轨：Fade / Blink / ObjectSwitch / RobotArm / WorldRotationLock 等「绑特定组件、业务上同轨只用一种」的 Clip。
    /// 技术上允许多种 <see cref="TrackClipType"/>（共用 Mixer），业务上一条轨实例只服务一种 Anim。
    /// 与 <see cref="ScriptMovementTrack"/> 分离：无前序位姿链。WorldRotationLock 须另建本类型轨实例，
    /// 绑定 <see cref="WorldRotationLockAnim"/>，与移动轨时间重叠。
    /// </summary>
    [TrackColor(0.75f, 0.55f, 0.95f)]
    [TrackClipType(typeof(FadeClip), false)]
    [TrackClipType(typeof(BlinkClip), false)]
    [TrackClipType(typeof(ObjectSwitchClip), false)]
    [TrackClipType(typeof(SwingFlipClip), false)]
    [TrackClipType(typeof(SequentialPositionClip), false)]
    [TrackClipType(typeof(PoseChangeClip), false)]
    [TrackClipType(typeof(OpenBoxClip), false)]
    [TrackClipType(typeof(FilmWrapClip), false)]
    [TrackClipType(typeof(RobotArmClip), false)]
    [TrackClipType(typeof(RobotArm5Clip), false)]
    [TrackClipType(typeof(WorldRotationLockClip), false)]
    [TrackClipType(typeof(CommentClip), false)]
    [TrackBindingType(typeof(ScriptAnimActor))]
    public class ScriptDedicatedTrack : ScriptAnimTrackBase
    {
        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
        {
            var playable = ScriptPlayable<ScriptDedicatedMixerBehaviour>.Create(graph, inputCount);
            ScriptDedicatedMixerBehaviour mixer = playable.GetBehaviour();
            mixer.Director = go != null ? go.GetComponent<PlayableDirector>() : null;
            mixer.PostPlaybackState = m_PostPlaybackState;
            mixer.Clips = GetClips().ToArray();
            return playable;
        }

        /// <summary>
        /// 同轨出现多种非 <see cref="CommentClip"/> 资产类型时返回警告文案；否则 null。
    /// 仅提示，不阻止创建或播放。
    /// </summary>
        public string GetMixedClipTypesWarning()
        {
            Type firstType = null;
            var extras = new List<string>(4);

            foreach (TimelineClip clip in GetClips())
            {
                if (clip?.asset == null || clip.asset is CommentClip)
                    continue;

                Type assetType = clip.asset.GetType();
                if (firstType == null)
                {
                    firstType = assetType;
                    continue;
                }

                if (assetType == firstType)
                    continue;

                string name = assetType.Name;
                if (!extras.Contains(name))
                    extras.Add(name);
            }

            if (firstType == null || extras.Count == 0)
                return null;

            return
                $"本轨混用了多种专用 Clip（{firstType.Name} 与 {string.Join("、", extras)}）。" +
                "业务上建议一条专用轨只使用一种 Clip；混用不会被禁止，但通常无意义。";
        }
    }
}
