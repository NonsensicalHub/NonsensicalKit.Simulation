using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// ScriptAnimation 共用 Mixer：权重仲裁、scrub 保持、归一化时间、位姿快照。
    /// Clip 分发见 <see cref="ScriptMovementMixerBehaviour"/>、
    /// <see cref="ScriptDedicatedMixerBehaviour"/>。
    /// </summary>
    public abstract class ScriptAnimMixerBase : PlayableBehaviour
    {
        public PlayableDirector Director;
        public TimelineClip[] Clips;
        public ScriptAnimTrackBase.PostPlaybackState PostPlaybackState =
            ScriptAnimTrackBase.PostPlaybackState.Revert;

        protected ScriptAnimActor BoundActor;
        protected ScriptAnimControlledPose.Snapshot InitialPose;
        protected bool HasInitialPose;

        public override void OnPlayableDestroy(Playable playable)
        {
            OnMixerDestroy(playable);
        }

        protected virtual void OnMixerDestroy(Playable playable)
        {
            if (HasInitialPose && BoundActor != null &&
                PostPlaybackState == ScriptAnimTrackBase.PostPlaybackState.Revert)
            {
                InitialPose.Restore();
            }

            OnMixerDestroyAfterPoseRestore(playable);
        }

        protected virtual void OnMixerDestroyAfterPoseRestore(Playable playable) { }

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            var actor = playerData as ScriptAnimActor;
            if (actor == null)
                return;

            if (BoundActor == null)
            {
                BoundActor = actor;
                InitialPose = ScriptAnimControlledPose.Snapshot.Capture(actor);
                HasInitialPose = true;
            }

            BeforeProcessInputs(playable, actor, info);

            if (TryProcessBestInput(playable, actor, info))
                return;

            if (TryHoldLastFinishedClip(playable, actor, info))
                return;

            AfterNoActiveClip(playable, actor, info);
        }

        protected virtual void BeforeProcessInputs(Playable playable, ScriptAnimActor actor, FrameData info) { }

        protected virtual void AfterNoActiveClip(Playable playable, ScriptAnimActor actor, FrameData info) { }

        protected bool TryProcessBestInput(Playable playable, ScriptAnimActor actor, FrameData info)
        {
            int inputCount = playable.GetInputCount();
            int bestIndex = -1;
            float bestWeight = 0.001f;
            for (int i = 0; i < inputCount; i++)
            {
                float weight = playable.GetInputWeight(i);
                if (weight > bestWeight)
                {
                    bestWeight = weight;
                    bestIndex = i;
                }
            }

            if (bestIndex < 0)
                return false;

            return TryProcessInput(
                playable.GetInput(bestIndex), actor, info, holdEnd: false, ClipAt(bestIndex));
        }

        protected bool TryHoldLastFinishedClip(Playable playable, ScriptAnimActor actor, FrameData info)
        {
            if (Director == null || Clips == null)
                return false;

            double time = Director.time;
            int best = -1;
            double bestEnd = double.NegativeInfinity;
            int count = Mathf.Min(Clips.Length, playable.GetInputCount());

            for (int i = 0; i < count; i++)
            {
                TimelineClip clip = Clips[i];
                if (clip == null)
                    continue;

                if (clip.asset is CommentClip)
                    continue;

                if (clip.end > time + 1e-9)
                    continue;

                if (clip.end < bestEnd - 1e-9)
                    continue;

                if (clip.end > bestEnd + 1e-9 ||
                    best < 0 ||
                    clip.start >= Clips[best].start)
                {
                    bestEnd = clip.end;
                    best = i;
                }
            }

            if (best < 0)
                return false;

            return TryProcessInput(
                playable.GetInput(best), actor, info, holdEnd: true, ClipAt(best));
        }

        protected TimelineClip ClipAt(int index)
        {
            if (Clips == null || index < 0 || index >= Clips.Length)
                return null;
            return Clips[index];
        }

        protected TimelineClip FindTimelineClip(PlayableAsset asset)
        {
            if (Clips == null || asset == null)
                return null;

            for (int i = 0; i < Clips.Length; i++)
            {
                if (Clips[i] != null && Clips[i].asset == asset)
                    return Clips[i];
            }

            return null;
        }

        protected abstract bool TryProcessInput(
            Playable input,
            ScriptAnimActor actor,
            FrameData info,
            bool holdEnd,
            TimelineClip timelineClip);

        protected static float NormalizedTime(Playable inputPlayable, bool holdEnd)
        {
            if (holdEnd)
                return 1f;

            double duration = inputPlayable.GetDuration();
            double time = inputPlayable.GetTime();
            if (duration > 1e-6 && !double.IsInfinity(duration) && !double.IsNaN(duration))
                return Mathf.Clamp01((float)(time / duration));
            return 1f;
        }

        protected float NormalizedClipTime(
            TimelineClip clip,
            Playable inputPlayable,
            bool holdEnd)
        {
            if (holdEnd)
                return 1f;

            if (clip != null && Director != null && clip.duration > 1e-6)
            {
                double local = Director.time - clip.start;
                return Mathf.Clamp01((float)(local / clip.duration));
            }

            return NormalizedTime(inputPlayable, holdEnd);
        }
    }
}
