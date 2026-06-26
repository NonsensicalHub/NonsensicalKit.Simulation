using System;
using System.Collections.Generic;
using UnityEngine;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Runtime
{
    /// <summary>锚点 GameObject 名称与场景中 Transform 的精确互查索引。</summary>
    internal sealed class SimPlaybackNodeAnchorIndex
    {
        private readonly Dictionary<string, Transform> _byKey = new(StringComparer.Ordinal);

        public void Clear() => _byKey.Clear();

        public void Register(string nodeId, Transform transform)
        {
            if (string.IsNullOrWhiteSpace(nodeId) || transform == null)
            {
                return;
            }

            _byKey[nodeId.Trim()] = transform;
        }

        public bool TryGet(string mapNodeId, out Transform transform)
        {
            transform = null;
            if (string.IsNullOrWhiteSpace(mapNodeId))
            {
                return false;
            }

            return _byKey.TryGetValue(mapNodeId.Trim(), out transform);
        }
    }
}
