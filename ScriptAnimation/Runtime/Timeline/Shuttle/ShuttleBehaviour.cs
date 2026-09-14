using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation
{
    [Serializable]
    public class ShuttleBehaviour : PlayableBehaviour
    {
        public ShuttleClipData Data = new ShuttleClipData();

        [NonSerialized] public ShuttleClip ClipAsset;

        /// <summary>
        /// 开场车体位姿与机构状态只解析一次并缓存。
    /// 无前序时车体来自组件 Home；机构默认按本 Clip 模式（取=松开、放=夹持）。
    /// </summary>
        [NonSerialized] public bool HomeResolved;
        [NonSerialized] public Vector3 CachedHomePos;
        [NonSerialized] public Quaternion CachedHomeRot;
        [NonSerialized] public float CachedStartClawExtend;
        [NonSerialized] public float CachedStartClampOffset;
        [NonSerialized] public float CachedStartPaddleAngle;

        public override void OnGraphStart(Playable playable)
        {
            HomeResolved = false;
        }

        public override void OnBehaviourPlay(Playable playable, FrameData info)
        {
            HomeResolved = false;
        }

        public bool EnsureHomeResolved(
            TimelineClip timelineClip,
            ShuttleAnim anim,
            IExposedPropertyTable resolver)
        {
            if (HomeResolved)
                return true;
            if (anim == null || ClipAsset == null || Data == null)
                return false;

            ScriptAnimHomeResolver.ResolveShuttleBodyHome(
                timelineClip,
                ClipAsset,
                anim,
                resolver,
                out CachedHomePos,
                out CachedHomeRot);

            ScriptAnimHomeResolver.ResolveShuttleMechanismStart(
                timelineClip,
                ClipAsset,
                anim,
                out CachedStartClawExtend,
                out CachedStartClampOffset,
                out CachedStartPaddleAngle);

            HomeResolved = true;
            return true;
        }
    }
}
