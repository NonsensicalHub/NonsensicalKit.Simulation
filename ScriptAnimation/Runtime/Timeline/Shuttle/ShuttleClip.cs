using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation
{
    [System.Serializable]
    public class ShuttleClip : PlayableAsset, ITimelineClipAsset, IDurationResolvable
    {
        public ShuttleClipData Data = new ShuttleClipData();

        public ClipCaps clipCaps => ClipCaps.ClipIn | ClipCaps.SpeedMultiplier;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<ShuttleBehaviour>.Create(graph);
            ShuttleBehaviour behaviour = playable.GetBehaviour();
            behaviour.Data = Data;
            behaviour.ClipAsset = this;
            return playable;
        }

        public float ResolveDuration(in DurationResolveContext context)
        {
            var anim = context.TrackBinding as ShuttleAnim;
            if (anim == null || Data == null)
                return -1f;

            ScriptAnimHomeResolver.ResolveShuttleMechanismStart(
                context.TimelineClip,
                this,
                anim,
                out float startClaw,
                out float startClamp,
                out float startPaddle);

            return ShuttleSampler.EstimateDuration(
                anim, Data, startClaw, startClamp, startPaddle);
        }

        /// <summary>
        /// 无前序时：用组件 Home 开场。
    /// 播放路径须经 ShuttleBehaviour 缓存，勿在 ProcessFrame 每帧调用。
    /// </summary>
        public static void ResolveCurrentPose(
            ShuttleAnim anim, out Vector3 pos, out Quaternion rot)
        {
            if (anim != null)
            {
                anim.ResolveHomePoseOrFallback(out pos, out rot, "Shuttle");
                return;
            }

            pos = Vector3.zero;
            rot = Quaternion.identity;
        }
    }
}
