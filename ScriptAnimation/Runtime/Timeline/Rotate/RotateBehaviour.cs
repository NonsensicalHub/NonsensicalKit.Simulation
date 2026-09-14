using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation
{
    [Serializable]
    public class RotateBehaviour : PlayableBehaviour
    {
        public RotateClipData Data = new RotateClipData();

        [NonSerialized] public RotateClip ClipAsset;

        /// <summary>
        /// 开场落点（前序结束位置；无前序时为组件 Home 位置）。
    /// 朝向不再缓存：由 Data.StartYawDegrees + 进度直接算出。
    /// </summary>
        [NonSerialized] public Vector3 CachedPosition;
        [NonSerialized] public bool PositionResolved;
        [NonSerialized] public bool ResolveFailed;

        public override void OnGraphStart(Playable playable)
        {
            PositionResolved = false;
            ResolveFailed = false;
        }

        public override void OnBehaviourPlay(Playable playable, FrameData info)
        {
            // 循环再次进入时重新解析落点；朝向始终用 StartYaw + 进度决定，不读 Body。
            PositionResolved = false;
        }

        public bool EnsurePositionResolved(
            TimelineClip timelineClip,
            PathMoveActor actor,
            IExposedPropertyTable resolver)
        {
            if (PositionResolved)
                return !ResolveFailed;
            if (ResolveFailed || actor == null || Data == null)
                return false;

            if (!ScriptAnimHomeResolver.TryResolveRotateIncomingPose(
                    timelineClip, actor, resolver, out CachedPosition, out _))
            {
                ResolveFailed = true;
                PositionResolved = true;
                Debug.LogWarning("[Rotate] 无法解析开场落点（无前序时需绑定 PathMoveActor）", actor);
                return false;
            }

            PositionResolved = true;
            return true;
        }
    }
}
