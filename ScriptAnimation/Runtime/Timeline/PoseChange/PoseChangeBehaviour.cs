using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation
{
    [Serializable]
    public class PoseChangeBehaviour : PlayableBehaviour
    {
        public PoseChangeClipData Data = new PoseChangeClipData();

        [NonSerialized] public PoseChangeClip ClipAsset;

        [NonSerialized] public PoseChangeSampler.PoseTRS CachedStart;
        [NonSerialized] public PoseChangeSampler.PoseTRS CachedEnd;
        [NonSerialized] public PoseChangeSampler.PoseTRS[] CachedStartMulti;
        [NonSerialized] public PoseChangeSampler.PoseTRS[] CachedEndMulti;
        [NonSerialized] public bool IsMultiTarget;
        [NonSerialized] public bool IncomingResolved;
        [NonSerialized] public bool ResolveFailed;

        public override void OnGraphStart(Playable playable)
        {
            ResetResolveState();
        }

        public override void OnBehaviourPlay(Playable playable, FrameData info)
        {
            ResetResolveState();
        }

        void ResetResolveState()
        {
            IncomingResolved = false;
            ResolveFailed = false;
            IsMultiTarget = false;
            CachedStartMulti = null;
            CachedEndMulti = null;
        }

        public bool EnsureIncomingResolved(TimelineClip timelineClip, ScriptAnimActor actor)
        {
            if (IncomingResolved)
                return !ResolveFailed;
            if (actor == null || Data == null)
                return false;

            if (actor is PoseChangeAnimMax maxAnim)
                return EnsureIncomingResolvedMax(timelineClip, maxAnim, actor);

            return EnsureIncomingResolvedSingle(timelineClip, actor as IPoseChangeActor, actor);
        }

        bool EnsureIncomingResolvedSingle(
            TimelineClip timelineClip,
            IPoseChangeActor anim,
            ScriptAnimActor actor)
        {
            if (anim == null)
                return false;

            IsMultiTarget = false;

            if (!PoseChangeSampler.TryResolveEndPose(anim, Data.PoseName, out CachedEnd))
            {
                if (!ResolveFailed)
                {
                    Debug.LogWarning($"[PoseChange] 无法解析目标姿态「{Data.PoseName}」", actor);
                }

                ResolveFailed = true;
                IncomingResolved = true;
                return false;
            }

            if (!PoseChangeSampler.TryResolvePreviousEndPose(anim, timelineClip, out CachedStart))
                CachedStart = anim.ResolveDefaultStartPose();

            ResolveFailed = false;
            IncomingResolved = true;
            return true;
        }

        bool EnsureIncomingResolvedMax(
            TimelineClip timelineClip,
            PoseChangeAnimMax anim,
            ScriptAnimActor actor)
        {
            IsMultiTarget = true;

            if (!PoseChangeSampler.TryResolveEndPose(anim, Data.PoseName, out CachedEndMulti))
            {
                if (!ResolveFailed)
                {
                    Debug.LogWarning($"[PoseChange] 无法解析目标姿态「{Data.PoseName}」", actor);
                }

                ResolveFailed = true;
                IncomingResolved = true;
                return false;
            }

            if (!PoseChangeSampler.TryResolvePreviousEndPose(anim, timelineClip, out CachedStartMulti))
                CachedStartMulti = anim.ResolveDefaultStartPoses();

            ResolveFailed = false;
            IncomingResolved = true;
            return true;
        }
    }
}
