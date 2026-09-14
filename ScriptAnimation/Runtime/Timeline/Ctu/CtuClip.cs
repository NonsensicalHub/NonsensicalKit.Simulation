using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation
{
    [System.Serializable]
    public class CtuClip : PlayableAsset, ITimelineClipAsset, IDurationResolvable
    {
        [HideInInspector]
        [Tooltip("可放货点（ScriptAnimPoint；经 PlayableDirector ExposedReference 绑定）")]
        public ExposedReference<ScriptAnimPoint> Station;

        public CtuClipData Data = new CtuClipData();

        public ClipCaps clipCaps => ClipCaps.ClipIn | ClipCaps.SpeedMultiplier;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<CtuBehaviour>.Create(graph);
            CtuBehaviour behaviour = playable.GetBehaviour();
            behaviour.Data = Data;
            behaviour.ClipAsset = this;

            IExposedPropertyTable resolver = graph.GetResolver();
            behaviour.Station = ScriptAnimPointUtility.Resolve(Station, resolver);
            return playable;
        }

        public float ResolveDuration(in DurationResolveContext context)
        {
            var anim = context.TrackBinding as CtuAnim;
            if (anim == null || Data == null)
                return -1f;

            Transform station = null;
            if (context.Resolver != null)
                station = ScriptAnimPointUtility.AsTransform(
                    ScriptAnimPointUtility.Resolve(Station, context.Resolver));

            ScriptAnimHomeResolver.ResolveCtuBodyHome(
                context.TimelineClip,
                this,
                anim,
                context.Resolver,
                out Vector3 homePos,
                out Quaternion homeRot);

            ScriptAnimHomeResolver.ResolveCtuMechanismStart(
                context.TimelineClip,
                this,
                anim,
                context.Resolver,
                out float startRotate,
                out float startLift,
                out float startClaw,
                out float startPaddle);

            return CtuSampler.EstimateDuration(
                anim, Data, station, homePos, homeRot,
                startRotate, startLift, startClaw, startPaddle);
        }

        /// <summary>
        /// 无前序时：用组件 Home 开场。
    /// 播放路径须经 CtuBehaviour 缓存，勿在 ProcessFrame 每帧调用。
    /// </summary>
        public static void ResolveCurrentPose(
            CtuAnim anim, out Vector3 pos, out Quaternion rot)
        {
            if (anim != null)
            {
                anim.ResolveHomePoseOrFallback(out pos, out rot, "CTU");
                return;
            }

            pos = Vector3.zero;
            rot = Quaternion.identity;
        }
    }
}
