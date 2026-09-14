using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation
{
    [Serializable]
    public class LatentAgvBehaviour : PlayableBehaviour
    {
        public LatentAgvClipData Data = new LatentAgvClipData();

        [NonSerialized] public LatentAgvClip ClipAsset;

        /// <summary>
        /// 开场 Home 只解析一次并缓存（前序结束位姿或组件 Home，不读 Body）。
    /// </summary>
        [NonSerialized] public bool HomeResolved;
        [NonSerialized] public Vector3 CachedHomePos;
        [NonSerialized] public Quaternion CachedHomeRot;

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
            LatentAgvAnim anim,
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
                out _,
                out _);

            HomeResolved = true;
            return true;
        }
    }
}
