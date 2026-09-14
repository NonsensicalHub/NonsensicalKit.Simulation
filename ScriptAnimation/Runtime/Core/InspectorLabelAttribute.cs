using System;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// Inspector 字段显示名（中文标签）。由 Editor 中的 PropertyDrawer 生效。
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class InspectorLabelAttribute : PropertyAttribute
    {
        public string Label { get; }

        public InspectorLabelAttribute(string label)
        {
            Label = label ?? string.Empty;
        }
    }
}
