using System;
using System.Collections.Generic;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 与 PathNetwork 节点列表下标对齐的 Feature 缓存。
    /// Rebuild 只在 Bind / 导入后调用；热路径用 Get / TryGetIndex。
    /// </summary>
    public sealed class PathFeatureStore
    {
        readonly Dictionary<Type, Array> _dense = new Dictionary<Type, Array>();
        readonly Dictionary<Type, IPathFeatureIndex> _indexes = new Dictionary<Type, IPathFeatureIndex>();
        readonly List<IPathFeatureIndex> _indexBuffer = new List<IPathFeatureIndex>(8);
        readonly HashSet<Type> _typesScratch = new HashSet<Type>();
        int _nodeCount;

        public int NodeCount => _nodeCount;

        public void Rebuild(IReadOnlyList<PathNode> nodes)
        {
            _dense.Clear();
            _indexes.Clear();
            _typesScratch.Clear();
            _nodeCount = nodes != null ? nodes.Count : 0;
            if (_nodeCount <= 0)
                return;

            for (int i = 0; i < _nodeCount; i++)
            {
                var node = nodes[i];
                if (node == null)
                    continue;
                var features = node.Features;
                if (features == null)
                    continue;
                for (int f = 0; f < features.Count; f++)
                {
                    var feature = features[f];
                    if (feature != null)
                        _typesScratch.Add(feature.GetType());
                }
            }

            foreach (var type in _typesScratch)
            {
                var array = Array.CreateInstance(type, _nodeCount);
                for (int i = 0; i < _nodeCount; i++)
                {
                    var node = nodes[i];
                    if (node == null)
                        continue;
                    array.SetValue(node.GetFeature(type), i);
                }

                _dense[type] = array;
            }

            PathFeatureIndexRegistry.CreateAll(_indexBuffer);
            for (int i = 0; i < _indexBuffer.Count; i++)
            {
                var index = _indexBuffer[i];
                index.Rebuild(nodes);
                _indexes[index.GetType()] = index;
            }
        }

        /// <summary>按下标取模块；该节点没有此类型时为 null。</summary>
        public T Get<T>(int nodeIndex) where T : class, IPathNodeFeature
        {
            if (nodeIndex < 0 || nodeIndex >= _nodeCount)
                return null;
            if (!_dense.TryGetValue(typeof(T), out var array) || array == null)
                return null;
            return array.GetValue(nodeIndex) as T;
        }

        /// <summary>一次解析路径时先取整表，再按下标读，避免反复按 Type 查字典。</summary>
        public T[] GetArray<T>() where T : class, IPathNodeFeature
        {
            if (_dense.TryGetValue(typeof(T), out var array) && array is T[] typed)
                return typed;
            return Array.Empty<T>();
        }

        public bool TryGetIndex<T>(out T index) where T : class, IPathFeatureIndex
        {
            if (_indexes.TryGetValue(typeof(T), out var boxed))
            {
                index = boxed as T;
                return index != null;
            }

            index = null;
            return false;
        }
    }
}
