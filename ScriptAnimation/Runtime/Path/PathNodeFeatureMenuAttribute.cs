using System;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>给 Inspector「添加扩展模块」菜单用的显示名。业务程序集里的 Feature 也可使用。</summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class PathNodeFeatureMenuAttribute : Attribute
    {
        public PathNodeFeatureMenuAttribute(string menuPath)
        {
            MenuPath = menuPath ?? string.Empty;
        }

        public string MenuPath { get; }
    }
}
