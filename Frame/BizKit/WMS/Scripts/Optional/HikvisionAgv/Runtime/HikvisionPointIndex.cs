using System.Collections.Generic;
using NonsensicalKit.ScriptAnimation;

namespace HikvisionAgv
{
    /// <summary>MapId + PointId → PathNode。Bind 后 O(1) 查找。</summary>
    public sealed class HikvisionPointIndex : IPathFeatureIndex
    {
        readonly Dictionary<string, PathNode> _byMapPoint = new Dictionary<string, PathNode>(64);
        readonly Dictionary<int, PathNode> _byPointId = new Dictionary<int, PathNode>(64);
        readonly Dictionary<int, HikvisionPathPointFeature> _featureByNode = new Dictionary<int, HikvisionPathPointFeature>(64);

        public void Rebuild(IReadOnlyList<PathNode> nodes)
        {
            _byMapPoint.Clear();
            _byPointId.Clear();
            _featureByNode.Clear();
            if (nodes == null)
                return;

            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                if (node == null)
                    continue;
                var feature = node.GetFeature<HikvisionPathPointFeature>();
                if (feature == null)
                    continue;

                _byMapPoint[MakeKey(feature.MapId, feature.PointId)] = node;
                _byPointId[feature.PointId] = node;
                _featureByNode[node.GetInstanceID()] = feature;
            }
        }

        public bool TryGetNode(string mapId, int pointId, out PathNode node)
        {
            if (!string.IsNullOrEmpty(mapId) &&
                _byMapPoint.TryGetValue(MakeKey(mapId, pointId), out node) &&
                node != null)
                return true;
            return _byPointId.TryGetValue(pointId, out node) && node != null;
        }

        public bool TryGetPointId(PathNode node, out int pointId)
        {
            pointId = 0;
            if (node == null)
                return false;
            if (!_featureByNode.TryGetValue(node.GetInstanceID(), out var feature) || feature == null)
                return false;
            pointId = feature.PointId;
            return true;
        }

        public bool TryGetFeature(PathNode node, out HikvisionPathPointFeature feature)
        {
            feature = null;
            if (node == null)
                return false;
            return _featureByNode.TryGetValue(node.GetInstanceID(), out feature) && feature != null;
        }

        static string MakeKey(string mapId, int pointId)
        {
            return (mapId ?? string.Empty) + "\n" + pointId.ToString();
        }
    }
}
