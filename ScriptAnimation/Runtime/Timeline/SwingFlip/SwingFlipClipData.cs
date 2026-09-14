using System;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 摇摆翻转 Clip 参数。运行时一律使用本数据；新增 Clip 时从 <see cref="SwingFlipAnim"/> 写入默认值。
    /// </summary>
    [Serializable]
    public class SwingFlipClipData
    {
        [Header("启动延时")]
        [Min(0f)]
        [InspectorLabel("启动延时")]
        public float StartDelay;

        [Header("摇摆")]
        [InspectorLabel("摇摆轴")]
        public SwingFlipAxis SwayAxis = SwingFlipAxis.X;
        [InspectorLabel("摇摆角度")]
        public float SwayAngle = 25f;
        [Min(0.01f)]
        [InspectorLabel("摇摆时长")]
        public float SwayDuration = 0.35f;

        [Header("翻转")]
        [InspectorLabel("翻转轴")]
        public SwingFlipAxis FlipAxis = SwingFlipAxis.Y;
        [InspectorLabel("翻转角度")]
        public float FlipAngle = 180f;
        [Min(0.01f)]
        [InspectorLabel("翻转时长")]
        public float FlipDuration = 0.5f;

        [Header("曲线")]
        [InspectorLabel("缓动曲线")]
        public AnimationCurve Ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("时长")]
        [Tooltip("开启后「按速度刷新全轨时长」会按序列估算本 Clip 时长")]
        [InspectorLabel("自动同步时长")]
        public bool AutoSyncDuration = true;
    }
}
