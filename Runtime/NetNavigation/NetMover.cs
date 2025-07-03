using System;
using System.Collections.Generic;
using UnityEngine;

namespace NonsensicalKit.Simulation.NetNavigation
{
    public class NetMover : MonoBehaviour
    {
        [SerializeField] private Net m_net;
        [SerializeField] private bool m_rotateOnNode;
        [SerializeField] private LineRenderer m_lineRenderer;
        [SerializeField] private float m_rotateSpeed = 1;
        [SerializeField] private float m_moveSpeed = 1;
        [SerializeField] private bool m_skipFirstPoint;
        [SerializeField] private bool m_forcesBezierEndAngle = true;

        private bool _moving;

        public float GlobalSpeed { get; set; } = 1;

        private Queue<NodePath> _path = new();
        private readonly Queue<Vector3> _targets = new();
        private Vector3 _lastPos;
        private float _moveTimer;
        private float _moveTotalTime;
        private NodePath _currentPath;

        private void Awake()
        {
            if (m_net != null)
            {
                transform.position = m_net.GetNearestPoint(transform.position).transform.position;
            }
        }

        private void OnDisable()
        {
            _moving = false;
            _path.Clear();
            _targets.Clear();
        }


        private void Update()
        {
            if (!_moving)
            {
                if (_targets.Count > 0)
                {
                    if (m_net.TryFindPath(transform.position, _targets.Dequeue(), out var path))
                    {
                        _path = new Queue<NodePath>(path);
                        if (m_skipFirstPoint)
                        {
                            _path.Dequeue();
                        }

                        //前往起点
                        _lastPos = transform.position;
                        _currentPath = _path.Dequeue();
                        _moving = true;
                        _moveTimer = 0;
                        _moveTotalTime = Vector3.Distance(_currentPath.Node.Position, transform.position) / m_moveSpeed;
                        if (m_lineRenderer)
                        {
                            m_lineRenderer.enabled = true;
                        }
                    }
                }
            }

            if (_moving)
            {
                if (m_lineRenderer != null)
                {
                    Vector3[] line = new Vector3[_path.Count + 2];
                    Vector3 upOffset = new Vector3(0, 0.3f, 0);
                    line[0] = transform.position + upOffset;
                    line[1] = _currentPath.Node.Position + upOffset;
                    int index = 2;
                    foreach (var path in _path)
                    {
                        line[index++] = path.Node.Position + upOffset;
                    }

                    m_lineRenderer.positionCount = line.Length;
                    m_lineRenderer.SetPositions(line);
                }

                if (_moveTimer >= _moveTotalTime)
                {
                    if (_path.Count == 0)
                    {
                        _moving = false;
                        if (m_lineRenderer)
                        {
                            m_lineRenderer.enabled = false;
                        }
                    }
                    else
                    {
                        //前往下一个点位
                        _lastPos = _currentPath.Node.Position;
                        bool bezier = _currentPath.Type == PathType.Bezier;
                        _currentPath = _path.Dequeue();
                        _moveTimer = 0;
                        _moveTotalTime = _currentPath.Distance / m_moveSpeed;
                        if (bezier && m_forcesBezierEndAngle) //贝塞尔曲线运动结束时角度会略有偏差，为了运动连贯性强制修正角度
                        {
                            var lookDir = new Vector3(_currentPath.Node.Position.x, transform.position.y, _currentPath.Node.Position.z) -
                                          transform.position;
                            if (lookDir != Vector3.zero)
                            {
                                transform.rotation = Quaternion.LookRotation(-lookDir, Vector3.up);
                            }
                        }
                    }
                }
                else
                {
                    switch (_currentPath.Type)
                    {
                        case PathType.Straight:
                        {
                            var targetNode = _currentPath.Node;
                            if (m_rotateOnNode)
                            {
                                var lookDir = new Vector3(targetNode.Position.x, transform.position.y, targetNode.Position.z) - transform.position;
                                if (lookDir != Vector3.zero)
                                {
                                    var targetRotation = Quaternion.LookRotation(-lookDir, Vector3.up);
                                    if (Quaternion.Angle(transform.rotation, targetRotation) > 1f)
                                    {
                                        transform.rotation = Quaternion.Lerp(transform.rotation, targetRotation,
                                            GlobalSpeed * m_rotateSpeed * Time.deltaTime * 2.0f);
                                        return;
                                    }

                                    transform.rotation = targetRotation;
                                }
                            }

                            _moveTimer += Time.deltaTime * GlobalSpeed;

                            var t = Mathf.Min(1, _moveTimer / _moveTotalTime);
                            transform.position = Vector3.Lerp(_lastPos, targetNode.Position, t);
                            break;
                        }
                        case PathType.Bezier:
                        {
                            _moveTimer += Time.deltaTime * GlobalSpeed;

                            var radio = Mathf.Min(1, _moveTimer / _moveTotalTime);
                            var (point, dir) = _currentPath.Curve.GetPointAndTangentByArcLengthRadio(radio);
                            transform.position = point;
                            if (dir != Vector3.zero)
                            {
                                transform.rotation = Quaternion.LookRotation(-dir, Vector3.up);
                            }

                            break;
                        }
                        default: throw new ArgumentOutOfRangeException();
                    }
                }
            }
        }

        public void Move(NetPoint point, bool force = false)
        {
            m_net = point.Net;
            transform.position = m_net.GetNearestPoint(transform.position).transform.position;
            Move(point.transform.position, force);
        }

        public void Move(Vector3 pos, bool force = false)
        {
            if (force)
            {
                _path.Clear();
                _targets.Clear();
            }

            _targets.Enqueue(pos);
        }
    }
}
