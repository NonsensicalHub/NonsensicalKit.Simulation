using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation
{
    [Serializable]
    public class OpenBoxBehaviour : PlayableBehaviour
    {
        public OpenBoxClipData Data = new OpenBoxClipData();

        [NonSerialized] public OpenBoxClip ClipAsset;

        [NonSerialized] public OpenBoxPose CachedStart;
        [NonSerialized] public OpenBoxPose CachedEnd;
        [NonSerialized] public bool IncomingResolved;
        [NonSerialized] public bool ResolveFailed;

        public override void OnGraphStart(Playable playable)
        {
            IncomingResolved = false;
            ResolveFailed = false;
        }

        public override void OnBehaviourPlay(Playable playable, FrameData info)
        {
            IncomingResolved = false;
            ResolveFailed = false;
        }

        public bool EnsureIncomingResolved(TimelineClip timelineClip, OpenBoxAnim anim)
        {
            if (IncomingResolved)
                return !ResolveFailed;
            if (anim == null || Data == null)
                return false;

            CachedStart = OpenBoxSampler.ResolveStartPose(anim, timelineClip);
            CachedEnd = OpenBoxSampler.Evaluate(CachedStart, Data, 1f);
            ResolveFailed = false;
            IncomingResolved = true;
            return true;
        }
    }
}
