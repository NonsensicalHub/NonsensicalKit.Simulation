using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation
{
    [Serializable]
    public class ForkliftBehaviour : PlayableBehaviour
    {
        public ForkliftClipData Data = new ForkliftClipData();

        [NonSerialized] public ForkliftClip ClipAsset;
        [NonSerialized] public ScriptAnimPoint Station;

        /// <summary>
        /// 开场 Home 只解析一次并缓存。无前序时 Home 来自当前 Body，
    /// </summary>
        [NonSerialized] public bool HomeResolved;
        [NonSerialized] public Vector3 CachedHomePos;
        [NonSerialized] public Quaternion CachedHomeRot;
        [NonSerialized] public ForkliftRotateMode CachedRotateMode;

        public override void OnGraphStart(Playable playable)
        {
            HomeResolved = false;
        }

        public override void OnBehaviourPlay(Playable playable, FrameData info)
        {
            // 循环 / 再次进入时重解析前序结束位姿（确定性，不读 Body）
            HomeResolved = false;
        }

        public bool EnsureHomeResolved(
            TimelineClip timelineClip,
            ForkliftAnim anim,
            IExposedPropertyTable resolver)
        {
            if (HomeResolved)
                return true;
            if (anim == null || ClipAsset == null)
                return false;

            ScriptAnimHomeResolver.Resolve(
                timelineClip,
                ClipAsset,
                anim,
                resolver,
                out CachedHomePos,
                out CachedHomeRot,
                out CachedRotateMode,
                out _);

            HomeResolved = true;
            return true;
        }
    }
}
