using System;
using System.Collections.Generic;
using System.Linq;
using NonsensicalKit.Core;
using NonsensicalKit.Core.Log;
using NonsensicalKit.Tools;
using UnityEngine;
using Color = UnityEngine.Color;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace NonsensicalKit.Simulation.NetNavigation
{
    [DefaultExecutionOrder(-1)]
    public class Net : MonoBehaviour
    {
        [SerializeField] private List<NetPoint> m_points = new();
        [SerializeField] private bool m_sameLength;
        [SerializeField] public bool m_DrawPath;
        [SerializeField] public bool m_DrawHandle;

        private readonly Dictionary<NetPoint, NetNode> _nodes = new();

        private const float DistanceWeightModification = 0.5f; //距离权重修正，决定了与终点的距离在寻路中的重要性

        public List<NetPoint> Points
        {
            get => m_points;
            set => m_points = value;
        }

        private void Awake()
        {
            InitNet();
        }

        public bool TryFindPath(Vector3 startPos, Vector3 endPos, out List<NodePath> path)
        {
            NetPoint startPoint = GetNearestPoint(startPos);
            NetPoint endPoint = GetNearestPoint(endPos);
            return TryFindPath(startPoint, endPoint, out path);
        }

        public NetPoint GetNearestPoint(Vector3 pos)
        {
            if (m_points.Count == 0) return null;

            float minDistance = Vector3.Distance(m_points[0].transform.position, pos);
            NetPoint nearestPoint = m_points[0];
            for (int i = 1; i < m_points.Count; i++)
            {
                var distance = Vector3.Distance(m_points[i].transform.position, pos);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    nearestPoint = m_points[i];
                }
            }

            return nearestPoint;
        }

        private void InitNet()
        {
            _nodes.Clear();
            foreach (var netPoint in m_points)
            {
                if (netPoint == null) continue;

                netPoint.Net = this;
                var newNode = new NetNode
                {
                    Name = netPoint.name,
                    Position = netPoint.transform.position,
                    ConnectedPath = new NodePath[netPoint.m_Path.Count]
                };
                _nodes.TryAdd(netPoint, newNode);
            }

            foreach (var netPoint in m_points)
            {
                if (netPoint == null) continue;
                var node = _nodes[netPoint];
                for (int i = 0; i < netPoint.m_Path.Count; i++)
                {
                    if (netPoint.m_Path[i] == null) continue;
                    if (_nodes.TryGetValue(netPoint.m_Path[i].Target, out var target))
                    {
                        var path = new NodePath();
                        path.Type = netPoint.m_Path[i].Type;
                        path.Node = target;
                        switch (path.Type)
                        {
                            case PathType.Straight:
                            {
                                path.Distance = m_sameLength ? 1f : Vector3.Distance(node.Position, target.Position);
                                break;
                            }
                            case PathType.Bezier:
                            {
                                path.Curve = new CubicBezierCurve(node.Position, node.Position + netPoint.m_Path[i].StartControlPointOffset,
                                    target.Position + netPoint.m_Path[i].EndControlPointOffset, target.Position);
                                path.Distance = m_sameLength
                                    ? 1f
                                    : path.Curve.ArcLength;
                                break;
                            }
                            default: throw new ArgumentOutOfRangeException();
                        }


                        node.ConnectedPath[i] = path;
                    }
                    else
                    {
                        LogCore.Warning("连接了未被网络配置的节点", netPoint);
                    }
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="startPoint"></param>
        /// <param name="endPoint"></param>
        /// <param name="path">返回包含起点和重点的路径</param>
        /// <returns>是否存在路径</returns>
        public bool TryFindPath(NetPoint startPoint, NetPoint endPoint, out List<NodePath> path)
        {
            path = new List<NodePath>();
            var startNode = _nodes[startPoint];
            var endNode = _nodes[endPoint];
            if (startNode == null || endNode == null) return false;

            SortedList<float, AStarEndNode> findingNode = new SortedList<float, AStarEndNode> { { 0, new AStarEndNode(startNode) } };
            Dictionary<NetNode, float> minLength = new Dictionary<NetNode, float> { { startNode, 0 } };

            while (findingNode.Count > 0)
            {
                var currentNode = findingNode.Values[0];
                findingNode.RemoveAt(0);

                for (int i = 0; i < currentNode.Node.ConnectedPath.Length; i++)
                {
                    var cPath = currentNode.Node.ConnectedPath[i];

                    var cNode = cPath.Node;

                    if (cNode == endNode)
                    {
                        currentNode.Path.Add(cPath);
                        path = currentNode.Path;
                        return true;
                    }

                    var length = currentNode.PathLength + currentNode.Node.ConnectedPath[i].Distance;
                    if (minLength.ContainsKey(cNode) && minLength[cNode] < length) continue;
                    minLength[cNode] = length;

                    var newEndNode = new AStarEndNode(cPath, currentNode, currentNode.Node.ConnectedPath[i].Distance);
                    var power = newEndNode.PathLength + Vector3.Distance(cNode.Position, endNode.Position) * DistanceWeightModification;
                    findingNode.Add(power, newEndNode);
                }
            }

            return false;
        }

        private class AStarEndNode
        {
            public readonly NetNode Node;
            public readonly List<NodePath> Path = new();
            public readonly float PathLength;

            public AStarEndNode(NetNode node)
            {
                Node = node;
                Path.Add(new NodePath() { Node = node });
            }

            public AStarEndNode(NodePath path)
            {
                Node = path.Node;
                Path.Add(path);
            }

            public AStarEndNode(NodePath path, AStarEndNode lastNode, float distance)
            {
                Node = path.Node;
                Path = new List<NodePath>(lastNode.Path) { path };
                PathLength = lastNode.PathLength + distance;
            }
        }

#if UNITY_EDITOR

        [ContextMenu("检测子节点")]
        private void CheckChildren()
        {
            foreach (Transform child in transform)
            {
                if (child.TryGetComponent<NetPoint>(out var p))
                {
                    if (m_points.Contains(p) == false)
                    {
                        m_points.Add(p);
                    }
                }
            }
        }

        [ContextMenu("清空")]
        private void Clear()
        {
            foreach (var t in m_points)
            {
                if (t.m_Path != null)
                {
                    t.m_Path.Clear();
                }
            }
        }

        [ContextMenu("智能连接")]
        private void SmartConnection()
        {
            if (m_points.Count < 1) return;

            //检测是否完全联通，没有的话就联通最近的点
            while (true)
            {
                for (int i = 0; i < m_points.Count; i++)
                {
                    if (m_points[i] == null)
                    {
                        m_points.RemoveAt(i);
                        i--;
                    }
                }

                for (int i = 0; i < m_points.Count; i++)
                {
                    for (int j = 0; j < m_points[i].m_Path.Count; j++)
                    {
                        if (m_points[i].m_Path[j] == null || m_points[i].m_Path[j].Target == null)
                        {
                            m_points[i].m_Path.RemoveAt(j);
                            j--;
                        }
                    }
                }

                List<NetPoint> points = new() { m_points[0] };

                for (int i = 0; i < points.Count; i++)
                {
                    foreach (var point in points[i].m_Path)
                    {
                        if (points.Contains(point.Target) == false)
                        {
                            points.Add(point.Target);
                        }
                    }
                }

                if (points.Count == m_points.Count)
                {
                    break;
                }

                List<NetPoint> otherPoints = new();
                foreach (var point in m_points)
                {
                    if (points.Contains(point) == false)
                    {
                        otherPoints.Add(point);
                    }
                }

                float minDistance = float.MaxValue;
                NetPoint minPoint = null;
                NetPoint minLinkPoint = null;
                foreach (var point in points)
                {
                    foreach (var otherPoint in otherPoints)
                    {
                        var distance = Vector3.Distance(point.transform.position, otherPoint.transform.position);
                        if (distance < minDistance)
                        {
                            minDistance = distance;
                            minPoint = point;
                            minLinkPoint = otherPoint;
                        }
                    }
                }

                if (!minPoint.m_Path.Any(item => item.Target == minLinkPoint))
                {
                    minPoint.m_Path.Add(new NetPath() { Target = minLinkPoint });
                }

                if (!minLinkPoint.m_Path.Any(item => item.Target == minPoint))
                {
                    minLinkPoint.m_Path.Add(new NetPath() { Target = minPoint });
                }
            }
        }

        private void OnDrawGizmos()
        {
            if (m_points == null || !m_DrawPath) return;
            foreach (var point in m_points)
            {
                if (point == null || point.m_Path == null) continue;
                foreach (var cPath in point.m_Path)
                {
                    if (cPath == null || cPath.Target == null) continue;
                    switch (cPath.Type)
                    {
                        case PathType.Straight:
                        {
                            EditorDrawTool.DrawArrowGizmo(point.transform.position, cPath.Target.transform.position - point.transform.position,
                                Color.cyan,
                                0.5f);
                            break;
                        }
                        case PathType.Bezier:
                        {
                            EditorDrawTool.DrawBezierArrowGizmo(point.transform.position, point.transform.position + cPath.StartControlPointOffset,
                                cPath.Target.transform.position + cPath.EndControlPointOffset, cPath.Target.transform.position, 16, Color.cyan,
                                0.5f);
                            break;
                        }
                        default: throw new ArgumentOutOfRangeException();
                    }
                }
            }
        }

#endif
    }

    public class NetNode
    {
        public string Name;
        public Vector3 Position;
        public NodePath[] ConnectedPath;
    }

    public class NodePath
    {
        public NetNode Node;
        public PathType Type;
        public CubicBezierCurve Curve;
        public float Distance;
    }


#if UNITY_EDITOR
    [CustomEditor(typeof(Net))]
    public class NetEditor : Editor
    {
        private Net _net;

        private void OnEnable()
        {
            _net = target as Net;
        }

        public void OnSceneGUI()
        {
            if (_net == null)
            {
                return;
            }

            if (_net.m_DrawHandle)
            {
                foreach (var point in _net.Points)
                {
                    if (point == null) return;
                    var pos = point.transform.position;
                    var newPos = Handles.PositionHandle(pos, Quaternion.identity);

                    if (pos != newPos)
                    {
                        Undo.RecordObject(point.transform, "Changed Point Position");
                        point.transform.position = newPos;
                    }
                }
            }
        }
    }
#endif
}
