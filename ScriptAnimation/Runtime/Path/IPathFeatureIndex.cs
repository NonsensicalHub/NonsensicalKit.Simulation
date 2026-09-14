using System.Collections.Generic;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 路网级二级索引（点号、工位码等）。在 Bind 时 Rebuild，热路径只做字典查找。
    /// 实现类放在业务程序集，通过 <see cref="PathFeatureIndexRegistry"/> 自注册，路网核心不引用业务类型。
    /// </summary>
    public interface IPathFeatureIndex
    {
        void Rebuild(IReadOnlyList<PathNode> nodes);
    }
}
