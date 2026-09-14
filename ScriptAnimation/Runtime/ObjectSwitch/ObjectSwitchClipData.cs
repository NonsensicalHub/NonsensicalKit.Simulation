using System;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 本 Clip 生效期间，显示 <see cref="VisibleIndex"/> 对应对象，其余全部隐藏。
    /// </summary>
    [Serializable]
    public class ObjectSwitchClipData
    {
        [Tooltip("要显示的对象索引（从 0 开始）。越界或为负数时全部隐藏。")]
        [InspectorLabel("显示索引")]
        public int VisibleIndex;
    }
}
