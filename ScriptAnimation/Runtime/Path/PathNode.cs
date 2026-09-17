using System;
using System.Collections.Generic;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>路网节点。可手动连 Neighbors，或由 PathNetwork 自动连边。</summary>
    [AddComponentMenu("ScriptAnimation/路网节点 (PathNode)")]
    public class PathNode : ScriptAnimPoint
    {
        [InspectorLabel("邻接节点")]
        [SerializeField] private List<PathNode> m_neighbors = new List<PathNode>();

        [Header("顶升移栽")]
        [Tooltip("勾选后，PathMove 经过本节点且前后路径夹角超过 35° 时会在原地停留（模拟顶升移栽机换向交接）。直线经过不停留。")]
        [InspectorLabel("经过时停界")]
        [SerializeField] private bool m_pauseOnPass;

        [Tooltip("停留秒数。仅在「经过时停留」开启时生效。")]
        [InspectorLabel("停留时长")]
        [SerializeField] private float m_pauseDuration = 1f;

        [Tooltip("SerializeReference 模块袋。停留仍用上方字段，不要放到这里。热路径请走所属 PathNetwork.FeatureStore。")]
        [SerializeReference]
        private List<IPathNodeFeature> m_features = new List<IPathNodeFeature>();

        private PathNetwork _network;

        public IReadOnlyList<PathNode> Neighbors => m_neighbors;
        public IReadOnlyList<IPathNodeFeature> Features => m_features;

        /// <summary>是否在 PathMove 经过时原地停留。</summary>
        public bool PauseOnPass => m_pauseOnPass;

        /// <summary>经过时停留秒数；未开启时为0。</summary>
        public float PauseDuration =>
            m_pauseOnPass ? Mathf.Max(0f, m_pauseDuration) : 0f;

        /// <summary>所属路网；用 <see cref="PathNetwork"/> 在收集节点/ 配置变更时写入。</summary>
        public PathNetwork Network => _network;

        /// <summary>由所属路网调用，避免节点侧反夹 GetComponentInParent。</summary>
        public void SetNetwork(PathNetwork network)
        {
            _network = network;
        }

        public void Connect(PathNode other, bool bidirectional = true)
        {
            if (other == null || other == this)
                return;

            if (!m_neighbors.Contains(other))
                m_neighbors.Add(other);

            if (bidirectional)
                other.Connect(this, false);
        }

        public void ClearNeighbors()
        {
            if (m_neighbors == null)
            {
                m_neighbors = new List<PathNode>();
                return;
            }

            var snapshot = new List<PathNode>(m_neighbors);
            m_neighbors.Clear();
            for (int i = 0; i < snapshot.Count; i++)
            {
                if (snapshot[i] != null)
                    snapshot[i].RemoveNeighbor(this);
            }
        }

        public void RemoveNeighbor(PathNode other)
        {
            if (other == null || m_neighbors == null)
                return;
            m_neighbors.Remove(other);
        }

        /// <summary>
        /// 移除错误邻居：空引用、自环、以及不属于指定路网的节点。
    /// 跨路网边会同时从对端摘掉。返回清理条数。
    /// </summary>
        public int RemoveInvalidNeighbors(PathNetwork expectedNetwork)
        {
            if (m_neighbors == null)
            {
                m_neighbors = new List<PathNode>();
                return 0;
            }

            int removed = 0;
            for (int i = m_neighbors.Count - 1; i >= 0; i--)
            {
                var other = m_neighbors[i];
                bool invalid = other == null ||
                               other == this ||
                               expectedNetwork == null ||
                               other.Network != expectedNetwork;
                if (!invalid)
                    continue;

                m_neighbors.RemoveAt(i);
                removed++;
                if (other != null && other != this)
                    other.RemoveNeighbor(this);
            }

            return removed;
        }

        public bool IsConnectedTo(PathNode other)
        {
            return other != null && m_neighbors != null && m_neighbors.Contains(other);
        }

        public T GetFeature<T>() where T : class, IPathNodeFeature
        {
            return GetFeature(typeof(T)) as T;
        }

        public IPathNodeFeature GetFeature(Type type)
        {
            if (type == null || m_features == null)
                return null;
            for (int i = 0; i < m_features.Count; i++)
            {
                var feature = m_features[i];
                if (feature != null && type.IsInstanceOfType(feature))
                    return feature;
            }

            return null;
        }

        /// <summary>同类型只保留一份：已有则替换，否则追加。</summary>
        public void SetFeature(IPathNodeFeature feature)
        {
            if (feature == null)
                return;
            if (m_features == null)
                m_features = new List<IPathNodeFeature>();

            Type type = feature.GetType();
            for (int i = 0; i < m_features.Count; i++)
            {
                if (m_features[i] != null && m_features[i].GetType() == type)
                {
                    m_features[i] = feature;
                    return;
                }
            }

            m_features.Add(feature);
        }

        public bool RemoveFeature<T>() where T : class, IPathNodeFeature
        {
            if (m_features == null)
                return false;
            Type type = typeof(T);
            for (int i = m_features.Count - 1; i >= 0; i--)
            {
                if (m_features[i] != null && type.IsInstanceOfType(m_features[i]))
                {
                    m_features.RemoveAt(i);
                    return true;
                }
            }

            return false;
        }

        protected override void OnDrawGizmos()
        {
            var network = Network;
            if (network != null && !network.ShowNodeGizmos)
                return;

            Color color = network != null ? network.GizmoColor : new Color(0.25f, 0.85f, 1f, 0.95f);
            if (m_pauseOnPass)
                color = new Color(1f, 0.55f, 0.15f, 0.95f);
            Gizmos.color = color;
            Gizmos.DrawSphere(transform.position, m_pauseOnPass ? 0.18f : 0.14f);

            if (m_neighbors == null)
                return;

            Gizmos.color = new Color(color.r, color.g, color.b, 0.65f);
            for (int i = 0; i < m_neighbors.Count; i++)
            {
                var other = m_neighbors[i];
                if (other == null)
                    continue;

                bool bidirectional = other.IsConnectedTo(this);
                if (bidirectional && other.GetInstanceID() < GetInstanceID())
                    continue;

                Gizmos.DrawLine(transform.position, other.transform.position);

                if (!bidirectional)
                {
                    Vector3 from = transform.position;
                    Vector3 to = other.transform.position;
                    Vector3 dir = to - from;
                    dir.y = 0f;
                    if (dir.sqrMagnitude < 0.0001f)
                        continue;
                    dir.Normalize();
                    Vector3 right = Vector3.Cross(Vector3.up, dir);
                    Vector3 tip = to - dir * 0.22f;
                    Gizmos.DrawLine(tip, tip - dir * 0.18f + right * 0.1f);
                    Gizmos.DrawLine(tip, tip - dir * 0.18f - right * 0.1f);
                }
            }
        }
    }
}
