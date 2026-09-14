using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation
{
    [System.Serializable]
    public class ForkliftClip : PlayableAsset, ITimelineClipAsset, IDurationResolvable
    {
        [HideInInspector]
        [Tooltip("可放货点（ScriptAnimPoint；经 PlayableDirector ExposedReference 绑定）")]
        public ExposedReference<ScriptAnimPoint> Station;

        public ForkliftClipData Data = new ForkliftClipData();

        public ClipCaps clipCaps => ClipCaps.ClipIn | ClipCaps.SpeedMultiplier;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<ForkliftBehaviour>.Create(graph);
            ForkliftBehaviour behaviour = playable.GetBehaviour();
            behaviour.Data = Data;
            behaviour.ClipAsset = this;

            IExposedPropertyTable resolver = graph.GetResolver();
            behaviour.Station = ScriptAnimPointUtility.Resolve(Station, resolver);
            return playable;
        }

        public float ResolveDuration(in DurationResolveContext context)
        {
            var anim = context.TrackBinding as ForkliftAnim;
            if (anim == null || Data == null)
                return -1f;

            Transform station = null;
            if (context.Resolver != null)
                station = ScriptAnimPointUtility.AsTransform(
                    ScriptAnimPointUtility.Resolve(Station, context.Resolver));

            ScriptAnimHomeResolver.Resolve(
                context.TimelineClip,
                this,
                anim,
                context.Resolver,
                out Vector3 homePos,
                out Quaternion homeRot,
                out ForkliftRotateMode rotateMode,
                out _);

            return ForkliftSampler.EstimateDuration(
                anim, Data, station, homePos, homeRot, rotateMode);
        }

        /// <summary>
        /// 无前序时：用组件 Home 开场。
    /// 播放路径须经 ForkliftBehaviour 缓存，勿在 ProcessFrame 每帧调用。
    /// </summary>
        public static void ResolveCurrentPose(
            ForkliftAnim anim, out Vector3 pos, out Quaternion rot)
        {
            if (anim != null)
            {
                anim.ResolveHomePoseOrFallback(out pos, out rot, "Forklift");
                return;
            }

            pos = Vector3.zero;
            rot = Quaternion.identity;
        }
    }
}
