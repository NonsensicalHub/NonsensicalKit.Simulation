using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation
{
    [Serializable]
    public class ThreePointTurnBehaviour : PlayableBehaviour
    {
        public ThreePointTurnClipData Data = new ThreePointTurnClipData();

        [NonSerialized] public ThreePointTurnClip ClipAsset;

        /// <summary>
        /// 开场位姿（前序结束位姿；无前序时为组件 Home）。
    /// 只解析一次并缓存；不可每帧重读 Body。
    /// </summary>
        [NonSerialized] public Vector3 IncomingPosition;
        [NonSerialized] public Quaternion IncomingRotation;
        [NonSerialized] public ThreePointTurnSampler.Plan CachedPlan;
        [NonSerialized] public bool PoseResolved;
        [NonSerialized] public bool ResolveFailed;

        public override void OnGraphStart(Playable playable)
        {
            PoseResolved = false;
            ResolveFailed = false;
            CachedPlan = default;
        }

        public override void OnBehaviourPlay(Playable playable, FrameData info)
        {
            PoseResolved = false;
        }

        public bool EnsurePoseResolved(
            TimelineClip timelineClip,
            PathMoveActor actor,
            IExposedPropertyTable resolver)
        {
            if (PoseResolved)
                return !ResolveFailed;
            if (ResolveFailed || actor == null || Data == null)
                return false;

            if (!ScriptAnimHomeResolver.TryResolveThreePointTurnIncomingPose(
                    timelineClip, actor, resolver, out IncomingPosition, out IncomingRotation))
            {
                ResolveFailed = true;
                PoseResolved = true;
                Debug.LogWarning("[ThreePointTurn] 无法解析开场位姿（无前序时需绑定 PathMoveActor）", actor);
                return false;
            }

            if (!ThreePointTurnSampler.TryBuildPlan(
                    actor, Data, IncomingPosition, IncomingRotation, out CachedPlan))
            {
                ResolveFailed = true;
                PoseResolved = true;
                Debug.LogWarning("[ThreePointTurn] 无法生成三点转向路径", actor);
                return false;
            }

            PoseResolved = true;
            return true;
        }
    }
}
