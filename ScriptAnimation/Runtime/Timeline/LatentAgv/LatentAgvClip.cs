using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation
{
    [System.Serializable]
    public class LatentAgvClip : PlayableAsset, ITimelineClipAsset, IDurationResolvable
    {
        [HideInInspector]
        [Tooltip("移动点（ScriptAnimPoint；经 PlayableDirector ExposedReference 绑定）")]
        public ExposedReference<ScriptAnimPoint> MovePoint;

        public LatentAgvClipData Data = new LatentAgvClipData();

        public ClipCaps clipCaps => ClipCaps.ClipIn | ClipCaps.SpeedMultiplier;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<LatentAgvBehaviour>.Create(graph);
            LatentAgvBehaviour behaviour = playable.GetBehaviour();
            behaviour.Data = Data;
            behaviour.ClipAsset = this;

            IExposedPropertyTable resolver = graph.GetResolver();
            behaviour.MovePoint = ScriptAnimPointUtility.Resolve(MovePoint, resolver);
            return playable;
        }

        public float ResolveDuration(in DurationResolveContext context)
        {
            var anim = context.TrackBinding as LatentAgvAnim;
            if (anim == null || Data == null)
                return -1f;

            Transform movePoint = null;
            if (context.Resolver != null)
                movePoint = ScriptAnimPointUtility.AsTransform(
                    ScriptAnimPointUtility.Resolve(MovePoint, context.Resolver));

            ScriptAnimHomeResolver.Resolve(
                context.TimelineClip,
                this,
                anim,
                context.Resolver,
                out Vector3 homePos,
                out Quaternion homeRot,
                out ForkliftRotateMode rotateMode,
                out _);

            return LatentAgvSampler.EstimateDuration(
                anim, Data, movePoint, homePos, homeRot, rotateMode);
        }

        /// <summary>
        /// 无前序时：用组件 Home 开场。
        /// 播放路径须经 LatentAgvBehaviour 缓存，勿在 ProcessFrame 每帧调用。
        /// </summary>
        public static void ResolveCurrentPose(
            LatentAgvAnim anim, out Vector3 pos, out Quaternion rot)
        {
            if (anim != null)
            {
                anim.ResolveHomePoseOrFallback(out pos, out rot, "LatentAgv");
                return;
            }

            pos = Vector3.zero;
            rot = Quaternion.identity;
        }
    }
}
