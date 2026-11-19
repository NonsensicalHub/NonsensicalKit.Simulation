using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace CineShot.Core
{
    [Serializable]
    public class CineShotClip : PlayableAsset, ITimelineClipAsset
    {
        public ShotAsset shot;
        public ShotOverride overrides;
        public ExposedReference<Transform> lookAt;
        public ExposedReference<Transform> follow;
        public ShotBlendMode blendMode = ShotBlendMode.Blend;
        public float blendDuration = 0.5f;
        public float matchHoldDuration;
        public EasingAsset easeIn;
        public EasingAsset easeOut;
        public CineSpline spline;
        public CineSpline cameraSpline;
        public EasingAsset panSpeed;
        public string matchKey;
        public string sourceHash;
        public double lastAppliedDuration;

        public ClipCaps clipCaps
        {
            get { return ClipCaps.Blending | ClipCaps.SpeedMultiplier; }
        }

        public ShotContext BuildContext(float localTime, float duration, in TargetSample lookAt, in TargetSample follow, Vector3 composition)
        {
            return new ShotContext
            {
                LocalTime = localTime,
                Duration = duration,
                LookAt = lookAt,
                Follow = follow,
                CompositionOffset = composition,
                Seed = shot != null ? shot.Seed : 0,
                Spline = spline,
                CameraSpline = cameraSpline,
                Speed = panSpeed
            };
        }

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<CineShotBehaviour>.Create(graph);
            playable.GetBehaviour().Setup(this);
            return playable;
        }

        public override double duration
        {
            get
            {
                if (shot != null && shot.defaultDuration > 0.1f)
                    return shot.defaultDuration;
                return 4.0;
            }
        }
    }
}
