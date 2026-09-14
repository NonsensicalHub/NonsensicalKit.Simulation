using System;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>注释 Clip 参数：仅用于 Timeline 标注，不影响绑定对象位姿。</summary>
    [Serializable]
    public class CommentClipData
    {
        [TextArea(3, 12)]
        [Tooltip("在 Timeline 上显示的说明文字")]
        [InspectorLabel("注释")]
        public string Text = string.Empty;
    }
}
