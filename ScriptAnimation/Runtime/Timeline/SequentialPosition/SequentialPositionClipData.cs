using System;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 依次换位 Clip 参数。路点间隔在 <see cref="SequentialPositionAnim"/> 上配置。
    /// </summary>
    [Serializable]
    public class SequentialPositionClipData
    {
        [Tooltip("开启后「按速度刷新全轨时长」会按组件路点间隔总和估算本 Clip 时长")]
        [InspectorLabel("自动同步时长")]
        public bool AutoSyncDuration = true;
    }
}
