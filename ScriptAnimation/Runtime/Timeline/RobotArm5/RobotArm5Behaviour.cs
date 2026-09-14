using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation
{
    [Serializable]
    public class RobotArm5Behaviour : PlayableBehaviour
    {
        public RobotArm5ClipData Data = new RobotArm5ClipData();

        [NonSerialized] public RobotArm5Clip ClipAsset;
        [NonSerialized] public Transform[] WaypointTargets;

        [NonSerialized] public bool FallbackResolved;
        [NonSerialized] public RobotArm5Sampler.TimelineStartFallback CachedFallback;
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

        public RobotArm5Sampler.TimelineStartFallback EnsureFallbackResolved(
            TimelineClip timelineClip,
            RobotArm5Anim anim,
            IExposedPropertyTable resolver,
            bool forceRefresh = false)
        {
            if (!forceRefresh && FallbackResolved)
                return CachedFallback;

            var resolved = RobotArm5Sampler.ResolvePreviousClipEnd(anim, timelineClip, resolver);
            if (resolved.HasValue && resolved.HasJointAngles)
                CachedFallback = new RobotArm5Sampler.TimelineStartFallback(
                    resolved.Position, resolved.Rotation, resolved.JointAngles);
            else
                CachedFallback = resolved;

            FallbackResolved = true;
            return CachedFallback;
        }
    }
}
