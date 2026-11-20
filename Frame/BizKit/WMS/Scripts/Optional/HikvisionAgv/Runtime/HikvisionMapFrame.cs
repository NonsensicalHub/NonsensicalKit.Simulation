using NonsensicalKit.ScriptAnimation;
using UnityEngine;

namespace HikvisionAgv
{
    /// <summary>
    /// 海康地图平面 ↔ Unity 世界坐标。
    /// 导入后以当前 PathNode 世界坐标为准：已知点用节点位置，中间点沿邻接边插值。
    /// 平面 XZ 按路网换算；Y 取匹配节点（或边上两端节点插值）的场景高度，便于多层路网。
    /// 挪过路网节点后，映射跟着新位置走。坐标框只用于还没有海康节点时的初始换算（导入不写高度）。
    /// </summary>
    [AddComponentMenu("海康AGV/地图坐标框 (HikvisionMapFrame)")]
    public sealed class HikvisionMapFrame : MonoBehaviour
    {
        const float VertexSnapMeters = 0.05f;

        [SerializeField] PathNetwork m_network;
        [SerializeField] string m_mapId;
        [Tooltip("RCS queryAgvStatus 的 mapShortName，如 AA / BB。不是 XML 里的 MapId。")]
        [SerializeField] string m_mapShortName;
        [Tooltip("仅初始导入：海康 xpos 乘到本地 X，ypos 乘到本地 Z。")]
        [SerializeField] Vector2 m_scale = Vector2.one;
        [Tooltip("仅初始导入：勾选后海康 ypos 映射到本地 -Z。")]
        [SerializeField] bool m_flipZ;

        public PathNetwork Network
        {
            get
            {
                if (m_network == null)
                    m_network = GetComponent<PathNetwork>() ?? GetComponentInParent<PathNetwork>();
                return m_network;
            }
            set => m_network = value;
        }

        public string MapId
        {
            get => m_mapId;
            set => m_mapId = value;
        }

        public string MapShortName
        {
            get => m_mapShortName;
            set => m_mapShortName = value;
        }

        public Vector2 Scale
        {
            get => m_scale;
            set => m_scale = value;
        }

        public bool FlipZ
        {
            get => m_flipZ;
            set => m_flipZ = value;
        }

        /// <summary>按 RCS 地图简称找场景里的地图框。只挂了一份时，简称未填也返回它。</summary>
        public static HikvisionMapFrame FindByMapShortName(string mapShortName)
        {
            var frames = FindObjectsByType<HikvisionMapFrame>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            if (frames == null || frames.Length == 0)
                return null;

            if (!string.IsNullOrEmpty(mapShortName))
            {
                for (int i = 0; i < frames.Length; i++)
                {
                    var frame = frames[i];
                    if (frame != null &&
                        string.Equals(frame.m_mapShortName, mapShortName, System.StringComparison.OrdinalIgnoreCase))
                        return frame;
                }
            }

            return frames.Length == 1 ? frames[0] : null;
        }

        /// <summary>初始导入用：只看坐标框 Transform，本地 Y=0，不读路网高度。</summary>
        public Vector3 MapToWorldByFrame(float mapX, float mapY)
        {
            float x = mapX * m_scale.x;
            float z = mapY * m_scale.y;
            if (m_flipZ)
                z = -z;
            return transform.TransformPoint(new Vector3(x, 0f, z));
        }

        /// <summary>初始导入用：坐标框反算。</summary>
        public Vector2 WorldToMapByFrame(Vector3 world)
        {
            Vector3 local = transform.InverseTransformPoint(world);
            float mapX = m_scale.x != 0f ? local.x / m_scale.x : 0f;
            float mapY = m_scale.y != 0f ? local.z / m_scale.y : 0f;
            if (m_flipZ)
                mapY = -mapY;
            return new Vector2(mapX, mapY);
        }

        /// <summary>海康平面坐标 → Unity 世界坐标。优先用当前路网节点位置（含节点高度）。</summary>
        public Vector3 MapToWorld(float mapX, float mapY)
        {
            if (TryMapToWorldFromNetwork(mapX, mapY, out Vector3 world))
                return world;
            return MapToWorldByFrame(mapX, mapY);
        }

        /// <summary>Unity 世界坐标 → 海康平面坐标。优先用当前路网节点位置。</summary>
        public Vector2 WorldToMap(Vector3 world)
        {
            if (TryWorldToMapFromNetwork(world, out Vector2 map))
                return map;
            return WorldToMapByFrame(world);
        }

        public bool TryGetNode(int pointId, out PathNode node)
        {
            node = null;
            var network = Network;
            if (network == null)
                return false;
            if (!network.FeatureStore.TryGetIndex<HikvisionPointIndex>(out var index) || index == null)
                return false;
            return index.TryGetNode(m_mapId, pointId, out node);
        }

        /// <summary>已导入的路径点号 → 场景中该 PathNode 的当前世界坐标。</summary>
        public bool TryGetWorldPosition(int pointId, out Vector3 world)
        {
            if (!TryGetNode(pointId, out var node) || node == null)
            {
                world = default;
                return false;
            }

            world = node.Position;
            return true;
        }

        public bool TryGetPointId(PathNode node, out int pointId)
        {
            pointId = 0;
            var network = Network;
            if (network == null)
                return false;
            if (!network.FeatureStore.TryGetIndex<HikvisionPointIndex>(out var index) || index == null)
                return false;
            return index.TryGetPointId(node, out pointId);
        }

        bool TryMapToWorldFromNetwork(float mapX, float mapY, out Vector3 world)
        {
            world = default;
            var network = Network;
            if (network == null)
                return false;

            var nodes = network.Nodes;
            if (nodes == null || nodes.Count == 0)
                return false;

            Vector2 mapP = new Vector2(mapX, mapY);
            float snapSq = VertexSnapMeters * VertexSnapMeters;
            float bestDistSq = float.PositiveInfinity;
            Vector3 bestWorld = default;
            PathNode bestVertex = null;
            HikvisionPathPointFeature bestVertexFeat = null;
            bool bestIsEdge = false;
            bool any = false;

            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                if (!TryGetHikFeature(node, out var feature))
                    continue;

                Vector2 mapA = new Vector2(feature.MapX, feature.MapY);
                float vertexDistSq = (mapP - mapA).sqrMagnitude;
                if (vertexDistSq <= snapSq)
                {
                    world = node.Position;
                    return true;
                }

                if (vertexDistSq < bestDistSq)
                {
                    bestDistSq = vertexDistSq;
                    bestWorld = node.Position;
                    bestVertex = node;
                    bestVertexFeat = feature;
                    bestIsEdge = false;
                    any = true;
                }

                var neighbors = node.Neighbors;
                if (neighbors == null)
                    continue;

                for (int n = 0; n < neighbors.Count; n++)
                {
                    var other = neighbors[n];
                    if (!TryGetHikFeature(other, out var otherFeature))
                        continue;

                    Vector2 mapB = new Vector2(otherFeature.MapX, otherFeature.MapY);
                    ClosestOnSegment(mapP, mapA, mapB, out float t, out float distSq);
                    if (distSq >= bestDistSq)
                        continue;

                    bestDistSq = distSq;
                    bestWorld = EvaluateWorldOnEdge(mapP, mapA, mapB, node.Position, other.Position, t);
                    bestIsEdge = true;
                    any = true;
                }
            }

            if (!any)
                return false;

            if (!bestIsEdge && bestVertex != null && bestVertexFeat != null)
            {
                Vector3 offset = MapToWorldByFrame(mapX, mapY) -
                                 MapToWorldByFrame(bestVertexFeat.MapX, bestVertexFeat.MapY);
                offset.y = 0f;
                world = bestVertex.Position + offset;
                world.y = bestVertex.Position.y;
                return true;
            }

            world = bestWorld;
            return true;
        }

        bool TryWorldToMapFromNetwork(Vector3 worldP, out Vector2 map)
        {
            map = default;
            var network = Network;
            if (network == null)
                return false;

            var nodes = network.Nodes;
            if (nodes == null || nodes.Count == 0)
                return false;

            Vector2 worldP2 = new Vector2(worldP.x, worldP.z);
            float snapSq = VertexSnapMeters * VertexSnapMeters;
            float bestDistSq = float.PositiveInfinity;
            Vector2 bestMap = default;
            bool any = false;

            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                if (!TryGetHikFeature(node, out var feature))
                    continue;

                Vector2 mapA = new Vector2(feature.MapX, feature.MapY);
                Vector2 worldA2 = new Vector2(node.Position.x, node.Position.z);
                float vertexDistSq = (worldP2 - worldA2).sqrMagnitude;
                if (vertexDistSq <= snapSq)
                {
                    map = mapA;
                    return true;
                }

                if (vertexDistSq < bestDistSq)
                {
                    bestDistSq = vertexDistSq;
                    bestMap = mapA;
                    any = true;
                }

                var neighbors = node.Neighbors;
                if (neighbors == null)
                    continue;

                for (int n = 0; n < neighbors.Count; n++)
                {
                    var other = neighbors[n];
                    if (!TryGetHikFeature(other, out var otherFeature))
                        continue;

                    Vector2 mapB = new Vector2(otherFeature.MapX, otherFeature.MapY);
                    Vector2 worldB2 = new Vector2(other.Position.x, other.Position.z);
                    ClosestOnSegment(worldP2, worldA2, worldB2, out float t, out float distSq);
                    if (distSq >= bestDistSq)
                        continue;

                    bestDistSq = distSq;
                    bestMap = EvaluateMapOnEdge(worldP, node.Position, other.Position, mapA, mapB, t);
                    any = true;
                }
            }

            if (!any)
                return false;

            map = bestMap;
            return true;
        }

        bool TryGetHikFeature(PathNode node, out HikvisionPathPointFeature feature)
        {
            feature = null;
            if (node == null)
                return false;
            feature = node.GetFeature<HikvisionPathPointFeature>();
            if (feature == null)
                return false;
            if (!string.IsNullOrEmpty(m_mapId) &&
                !string.IsNullOrEmpty(feature.MapId) &&
                feature.MapId != m_mapId)
            {
                feature = null;
                return false;
            }

            return true;
        }

        static void ClosestOnSegment(Vector2 p, Vector2 a, Vector2 b, out float t, out float distSq)
        {
            Vector2 ab = b - a;
            float abSq = ab.sqrMagnitude;
            if (abSq < 1e-12f)
            {
                t = 0f;
                distSq = (p - a).sqrMagnitude;
                return;
            }

            t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / abSq);
            Vector2 q = a + ab * t;
            distSq = (p - q).sqrMagnitude;
        }

        static Vector3 EvaluateWorldOnEdge(
            Vector2 mapP, Vector2 mapA, Vector2 mapB, Vector3 worldA, Vector3 worldB, float t)
        {
            Vector3 world = Vector3.LerpUnclamped(worldA, worldB, t);
            float height = world.y;
            Vector2 mapAb = mapB - mapA;
            float mapLen = mapAb.magnitude;
            if (mapLen < 1e-6f)
                return world;

            Vector2 mapOff = mapP - Vector2.LerpUnclamped(mapA, mapB, t);
            Vector3 worldAb = worldB - worldA;
            worldAb.y = 0f;
            float worldLen = worldAb.magnitude;
            float scale = worldLen / mapLen;
            float dAng = Mathf.Atan2(worldAb.z, worldAb.x) - Mathf.Atan2(mapAb.y, mapAb.x);
            float cos = Mathf.Cos(dAng);
            float sin = Mathf.Sin(dAng);
            world.x += (mapOff.x * cos - mapOff.y * sin) * scale;
            world.z += (mapOff.x * sin + mapOff.y * cos) * scale;
            world.y = height;
            return world;
        }

        static Vector2 EvaluateMapOnEdge(
            Vector3 worldP, Vector3 worldA, Vector3 worldB, Vector2 mapA, Vector2 mapB, float t)
        {
            Vector2 map = Vector2.LerpUnclamped(mapA, mapB, t);
            Vector3 worldAb = worldB - worldA;
            worldAb.y = 0f;
            Vector2 mapAb = mapB - mapA;
            float worldLen = worldAb.magnitude;
            float mapLen = mapAb.magnitude;
            if (worldLen < 1e-6f || mapLen < 1e-6f)
                return map;

            Vector3 worldQ = Vector3.LerpUnclamped(worldA, worldB, t);
            Vector3 worldOff = worldP - worldQ;
            worldOff.y = 0f;
            float scale = mapLen / worldLen;
            float dAng = Mathf.Atan2(mapAb.y, mapAb.x) - Mathf.Atan2(worldAb.z, worldAb.x);
            float cos = Mathf.Cos(dAng);
            float sin = Mathf.Sin(dAng);
            map.x += (worldOff.x * cos - worldOff.z * sin) * scale;
            map.y += (worldOff.x * sin + worldOff.z * cos) * scale;
            return map;
        }
    }
}
