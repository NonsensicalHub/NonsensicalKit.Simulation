using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation
{
    [Serializable]
    public class CtuBehaviour : PlayableBehaviour
    {
        public CtuClipData Data = new CtuClipData();

        [NonSerialized] public CtuClip ClipAsset;
        [NonSerialized] public ScriptAnimPoint Station;

        /// <summary>
        /// 开场车体位姿与机构状态只解析一次并缓存。
    /// 无前序时车体来自组件 Home；机构默认行驶态，
        /// 若同轨前序 CTU 结束未回正则从其结束转台/升降起。
    /// </summary>
        [NonSerialized] public bool HomeResolved;
        [NonSerialized] public Vector3 CachedHomePos;
        [NonSerialized] public Quaternion CachedHomeRot;
        [NonSerialized] public float CachedStartRotateAngle;
        [NonSerialized] public float CachedStartLiftHeight;
        [NonSerialized] public float CachedStartClawExtend;
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
            CtuAnim anim,
            IExposedPropertyTable resolver)
        {
            if (HomeResolved)
                return true;
            if (anim == null || ClipAsset == null || Data == null)
                return false;

            ScriptAnimHomeResolver.ResolveCtuBodyHome(
                timelineClip,
                ClipAsset,
                anim,
                resolver,
                out CachedHomePos,
                out CachedHomeRot);

            ScriptAnimHomeResolver.ResolveCtuMechanismStart(
                timelineClip,
                ClipAsset,
                anim,
                resolver,
                out CachedStartRotateAngle,
                out CachedStartLiftHeight,
                out CachedStartClawExtend,
                out CachedStartPaddleAngle);

            HomeResolved = true;
            return true;
        }
    }
}
