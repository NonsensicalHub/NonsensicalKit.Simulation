using System;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>透明度渐变 Clip 参数：在 Clip 时长内从起始透明度插到结束透明度。</summary>
    [Serializable]
    public class FadeClipData
    {
        [Range(0f, 1f)]
        [Tooltip("Clip 开始时的透明度（1=不透明）")]
        [InspectorLabel("起始透明度")]
        public float FromAlpha = 0f;

        [Range(0f, 1f)]
        [Tooltip("Clip 结束时的透明度（1=不透明）")]
        [InspectorLabel("结束透明度")]
        public float ToAlpha = 1f;

        [Tooltip("归一化时间 → 插值权重；默认线性")]
        [InspectorLabel("缓动曲线")]
        public AnimationCurve Ease = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Tooltip("开启后「按速度刷新全轨时长」会把本 Clip 设为目标时长")]
        [InspectorLabel("自动同步时长")]
        public bool AutoSyncDuration;

        [Min(0.01f)]
        [Tooltip("自动同步时长开启时使用的目标时长（秒）")]
        [InspectorLabel("目标时长（秒）")]
        public float DurationSeconds = 1f;
    }
}
