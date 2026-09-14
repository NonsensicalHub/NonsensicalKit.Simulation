using System;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>间隔显隐 Clip 参数：在 Clip 时长内均匀切换指定次数。</summary>
    [Serializable]
    public class BlinkClipData
    {
        [Min(1)]
        [Tooltip("Clip 时长内切换显隐的次数")]
        [InspectorLabel("切换次数")]
        public int ToggleCount = 6;

        [Tooltip("Clip 开始时是否可见")]
        [InspectorLabel("起始可见")]
        public bool StartVisible = true;

        [Min(0.01f)]
        [Tooltip("每次切换的间隔秒数；勾选自动同步时长时用于估算 Clip 时长")]
        [InspectorLabel("间隔（秒）")]
        public float IntervalSeconds = 0.05f;

        [Tooltip("开启后「按速度刷新全轨时长」会把本 Clip 设为 切换次数 × 间隔")]
        [InspectorLabel("自动同步时长")]
        public bool AutoSyncDuration = true;
    }
}
