using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation
{
    [Serializable]
    public class PathMoveBehaviour : PlayableBehaviour
    {
        public PathMoveClipData Data = new PathMoveClipData();

        [NonSerialized] public PathMoveClip ClipAsset;
        [NonSerialized] public PathNetwork Network;
        [NonSerialized] public PathNode StartNode;
        [NonSerialized] public PathNode EndNode;

        [NonSerialized] public readonly List<Vector3> CachedPoints = new List<Vector3>(32);
        [NonSerialized] public readonly List<float> CachedAccum = new List<float>(32);
        [NonSerialized] public readonly List<float> CachedPauses = new List<float>(32);
        [NonSerialized] public float CachedTotalLen;
        /// <summary>路径第一段朝向（几何）。</summary>
        [NonSerialized] public Quaternion PathStartRotation;
        /// <summary>
        /// 开场朝向（前序 Clip 结束朝向；无前序时为组件 Home）。
    /// 只在进入 Clip / seek 时解析一次并缓存。
    /// </summary>
        [NonSerialized] public Quaternion IncomingRotation;
        [NonSerialized] public bool Resolved;
        [NonSerialized] public bool ResolveFailed;
        [NonSerialized] public bool IncomingResolved;

        public override void OnGraphStart(Playable playable)
        {
            // 图重建时清空，保证重新按 Clip 数据解析
            Resolved = false;
            ResolveFailed = false;
            IncomingResolved = false;
            CachedPoints.Clear();
            CachedAccum.Clear();
            CachedPauses.Clear();
        }

        public override void OnBehaviourPlay(Playable playable, FrameData info)
        {
            // 循环 / 再次进入时重解析前序结束朝向（确定性，不读 Body）
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
            if (!PathMoveSampler.TryResolveWorldPoints(
                    Network, StartNode, EndNode, Data, CachedPoints, pathOffset, CachedPauses) ||
                CachedPoints.Count < 2)
            {
                ResolveFailed = true;
                Resolved = true;
                Debug.LogWarning(
                    $"[PathMove] 路径解析失败（需要有效 Start/End 节点） net={Network} start={StartNode} end={EndNode}",
                    actor);
                return false;
            }

            PathQuery.BuildAccum(CachedPoints, CachedAccum, out CachedTotalLen);
            PathStartRotation = PathMoveSampler.GetPathStartRotation(
                CachedPoints, rail, Data != null && Data.ReverseFacing);
            IncomingRotation = PathStartRotation;
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

            IncomingRotation = ScriptAnimHomeResolver.ResolvePathMoveIncomingRotation(
                timelineClip, actor, resolver, CachedPoints,
                Data != null && Data.ReverseFacing);
            IncomingResolved = true;
            return true;
        }
    }
}
