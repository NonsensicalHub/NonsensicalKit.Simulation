using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation
{
    [Serializable]
    public class RobotArmBehaviour : PlayableBehaviour
    {
        public RobotArmClipData Data = new RobotArmClipData();

        [NonSerialized] public RobotArmClip ClipAsset;
        [NonSerialized] public Transform[] WaypointTargets;

        /// <summary>
        /// 前序 Clip 终点 fallback 只在进入本 Clip 时解析一次。
    /// 链式 IK 很重，不可每帧 GetClips 重走整条前序。
    /// </summary>
        [NonSerialized] public bool FallbackResolved;
        [NonSerialized] public RobotArmSampler.TimelineStartFallback CachedFallback;
        [NonSerialized] public int DebugProcessFrameCount;

        public void SetWaypointTargets(Transform[] targets)
        {
            WaypointTargets = targets;
        }

        public override void OnGraphStart(Playable playable)
        {
            FallbackResolved = false;
            ClipAsset?.CachedPlans.Invalidate();
        }

        public override void OnBehaviourPlay(Playable playable, FrameData info)
        {
            FallbackResolved = false;
            DebugProcessFrameCount = 0;
        }

        public RobotArmSampler.TimelineStartFallback EnsureFallbackResolved(
            TimelineClip timelineClip,
            RobotArmAnim anim,
            IExposedPropertyTable resolver,
            bool forceRefresh = false)
        {
            if (!forceRefresh && FallbackResolved)
                return CachedFallback;

            var resolved = RobotArmSampler.ResolvePreviousClipEnd(anim, timelineClip, resolver);
            // 快照关节角，避免引用前序 Clip 的 PlanCache 内部数组并在后续重算时被改写
            if (resolved.HasValue && resolved.HasJointAngles)
                CachedFallback = new RobotArmSampler.TimelineStartFallback(
                    resolved.Position, resolved.Rotation, resolved.JointAngles);
            else
                CachedFallback = resolved;

            FallbackResolved = true;
            return CachedFallback;
        }
    }
}
