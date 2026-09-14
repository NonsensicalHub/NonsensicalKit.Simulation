using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation
{
    [Serializable]
    public class DirectMoveBehaviour : PlayableBehaviour
    {
        public DirectMoveClipData Data = new DirectMoveClipData();

        [NonSerialized] public DirectMoveClip ClipAsset;
        [NonSerialized] public Transform StartTarget;
        [NonSerialized] public Transform EndTarget;

        [NonSerialized] public Vector3 CachedStart;
        [NonSerialized] public Vector3 CachedEnd;
        /// <summary>
        /// 开场朝向（前序结束朝向；无前序时为组件 Home）。
    /// 只解析一次并缓存；全程不改朝向。
    /// </summary>
        [NonSerialized] public Quaternion IncomingRotation;
        [NonSerialized] public bool Resolved;
        [NonSerialized] public bool ResolveFailed;
        [NonSerialized] public bool IncomingResolved;

        public override void OnGraphStart(Playable playable)
        {
            Resolved = false;
            ResolveFailed = false;
            IncomingResolved = false;
        }

        public override void OnBehaviourPlay(Playable playable, FrameData info)
        {
            IncomingResolved = false;
        }

        public bool EnsureResolved(ScriptAnimActor actor)
        {
            if (Resolved)
                return !ResolveFailed;
            if (ResolveFailed || actor == null || Data == null)
                return false;

            var rail = actor as PathMoveActor;
            Vector3 pathOffset = rail != null ? rail.PathOffset : default;
            if (!DirectMoveSampler.TryResolveEndpoints(
                    StartTarget, EndTarget, Data, out CachedStart, out CachedEnd, pathOffset))
            {
                ResolveFailed = true;
                Resolved = true;
                Debug.LogWarning(
                    $"[DirectMove] 端点解析失败（需 Transform 或世界坐标） start={StartTarget} end={EndTarget} " +
                    $"useStartWorld={Data.UseStartWorldPosition} useEndWorld={Data.UseEndWorldPosition}",
                    actor);
                return false;
            }

            IncomingRotation = Quaternion.identity;
            Resolved = true;
            return true;
        }

        public bool EnsureIncomingResolved(
            TimelineClip timelineClip,
            PathMoveActor actor,
            IExposedPropertyTable resolver)
        {
            if (IncomingResolved)
                return EnsureResolved(actor);
            if (!EnsureResolved(actor))
                return false;

            IncomingRotation = ScriptAnimHomeResolver.ResolveDirectMoveIncomingRotation(
                timelineClip, actor, resolver);
            IncomingResolved = true;
            return true;
        }
    }
}
