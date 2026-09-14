using UnityEngine;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation
{
    public struct DurationResolveContext
    {
        /// <summary>Track 绑定对象（如 PathMoveActor / ForkliftAnim / StackerAnim）。</summary>
        public Object TrackBinding;

        /// <summary>用于解析 ExposedReference，通常是 PlayableDirector。</summary>
        public IExposedPropertyTable Resolver;

        /// <summary>当前正在估算时长的 TimelineClip（整轨刷新时提供，供查前序 PathMove）。</summary>
        public TimelineClip TimelineClip;
    }
}
