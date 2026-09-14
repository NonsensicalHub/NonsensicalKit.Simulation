using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation
{
    [Serializable]
    public class StackerBehaviour : PlayableBehaviour
    {
        public StackerClipData Data = new StackerClipData();

        [NonSerialized] public StackerClip ClipAsset;

        /// <summary>
        /// 起终点世界坐标只解析一次并缓存。货位在播放中不会变）
        /// 避免每帧 GetClips + WarehouseManager 查询。
    /// </summary>
        [NonSerialized] public bool EndpointsResolved;
        [NonSerialized] public bool ResolveFailed;
        [NonSerialized] public Vector3 CachedStart;
        [NonSerialized] public Vector3 CachedEnd;

        public override void OnGraphStart(Playable playable)
        {
            EndpointsResolved = false;
            ResolveFailed = false;
        }

        public override void OnBehaviourPlay(Playable playable, FrameData info)
        {
            EndpointsResolved = false;
            ResolveFailed = false;
        }

        public bool EnsureEndpointsResolved(StackerAnim anim, TimelineClip timelineClip)
        {
            if (EndpointsResolved)
                return !ResolveFailed;
            if (anim == null || Data == null)
                return false;

            var fallback = StackerSampler.ResolvePreviousClipEnd(anim, timelineClip);
            if (!StackerSampler.TryResolveEndWorld(anim, Data, out CachedEnd))
            {
                ResolveFailed = true;
                EndpointsResolved = true;
                Debug.LogWarning(
                    $"[Stacker] 无法解析终点货位 {StackerSampler.FormatCell(Data.EndCell)}（检查 WarehouseManager / CellPositionTable）",
                    anim);
                return false;
            }

            if (!StackerSampler.TryResolveStartWorld(anim, Data, fallback, out CachedStart))
            {
                ResolveFailed = true;
                EndpointsResolved = true;
                Debug.LogWarning(
                    $"[Stacker] 无法解析起点货位（HasStartCell={Data.HasStartCell} " +
                    $"{StackerSampler.FormatCell(Data.StartCell)}；需前序 Stacker、StartCell 或组件 Home）。",
                    anim);
                return false;
            }

            EndpointsResolved = true;
            return true;
        }
    }
}
