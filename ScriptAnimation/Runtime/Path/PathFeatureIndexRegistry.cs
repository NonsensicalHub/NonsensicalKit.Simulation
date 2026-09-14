using System;
using System.Collections.Generic;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 业务侧在加载时注册索引工厂。路网 Rebuild 时只调用工厂，不 new 具体业务类型。
    /// </summary>
    public static class PathFeatureIndexRegistry
    {
        static readonly Dictionary<Type, Func<IPathFeatureIndex>> s_factories =
            new Dictionary<Type, Func<IPathFeatureIndex>>();

        public static void Register<T>() where T : class, IPathFeatureIndex, new()
        {
            s_factories[typeof(T)] = () => new T();
        }

        public static void Register(Type indexType, Func<IPathFeatureIndex> factory)
        {
            if (indexType == null || factory == null)
                return;
            s_factories[indexType] = factory;
        }

        internal static void CreateAll(List<IPathFeatureIndex> results)
        {
            results.Clear();
            foreach (var pair in s_factories)
            {
                var index = pair.Value != null ? pair.Value() : null;
                if (index != null)
                    results.Add(index);
            }
        }
    }
}
