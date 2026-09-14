using System.Collections.Generic;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 对象切换：维护一组候选物体，按索引只激活其中一个，其余全部隐藏。
    /// 由 <see cref="ScriptDedicatedTrack"/> 绑定，经 <see cref="ObjectSwitchClip"/> 驱动。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("ScriptAnimation/对象切换 (ObjectSwitchAnim)")]
    public class ObjectSwitchAnim : ScriptAnimActor
    {
        [Tooltip("候选对象列表（按索引选择显示）。可用「收集子物体」自动填充。")]
        [InspectorLabel("候选对象")]
        [SerializeField] private List<GameObject> m_targets = new List<GameObject>();

        [Header("收集子物体")]
        [Tooltip("启用后在 Awake 时自动收集子物体填入列表。")]
        [InspectorLabel("Awake 时自动收集")]
        [SerializeField] private bool m_autoCollectOnAwake = true;

        [Tooltip("仅收集直接子物体；关闭则递归收集所有后代。")]
        [InspectorLabel("仅直接子物体")]
        [SerializeField] private bool m_directChildrenOnly = true;

        [Tooltip("收集时是否包含当前未激活的子物体。")]
        [InspectorLabel("包含未激活")]
        [SerializeField] private bool m_includeInactive = true;

        bool[] m_restStates;
        bool m_hasRest;

        public IReadOnlyList<GameObject> Targets => m_targets;
        public int TargetCount => m_targets != null ? m_targets.Count : 0;

        private void Awake()
        {
            if (m_autoCollectOnAwake)
                CollectChildren();
        }

        /// <summary>
        /// 按当前选项收集子物体，覆盖 Targets 列表（不含自身）。
        /// </summary>
        [ContextMenu("收集子物体")]
        public void CollectChildren()
        {
            if (m_targets == null)
                m_targets = new List<GameObject>();
            else
                m_targets.Clear();

            if (m_directChildrenOnly)
            {
                for (int i = 0; i < transform.childCount; i++)
                {
                    Transform child = transform.GetChild(i);
                    if (child == null)
                        continue;
                    if (!m_includeInactive && !child.gameObject.activeSelf)
                        continue;
                    m_targets.Add(child.gameObject);
                }

                return;
            }

            var children = GetComponentsInChildren<Transform>(m_includeInactive);
            for (int i = 0; i < children.Length; i++)
            {
                Transform t = children[i];
                if (t == null || t == transform)
                    continue;
                m_targets.Add(t.gameObject);
            }
        }

        public void CaptureRestIfNeeded()
        {
            if (m_hasRest)
                return;
            m_restStates = CaptureActiveStates();
            m_hasRest = true;
        }

        public void RevertToRest()
        {
            if (!m_hasRest)
                return;
            RestoreActiveStates(m_restStates);
            m_hasRest = false;
        }

        public void ClearRest() => m_hasRest = false;

        /// <summary>
        /// 激活指定索引的对象，其余全部 SetActive(false)。
        /// index &lt; 0 或越界时全部隐藏。
        /// </summary>
        public void SetVisibleIndex(int index)
        {
            if (m_targets == null)
                return;

            for (int i = 0; i < m_targets.Count; i++)
            {
                GameObject go = m_targets[i];
                if (go == null)
                    continue;

                // 每帧强制写入，避免被其它系统改回后因 early-out 漏关
                go.SetActive(i == index);
            }
        }

        /// <summary>记录当前各目标的 activeSelf，供 Timeline 结束还原。</summary>
        public bool[] CaptureActiveStates()
        {
            if (m_targets == null || m_targets.Count == 0)
                return System.Array.Empty<bool>();

            var states = new bool[m_targets.Count];
            for (int i = 0; i < m_targets.Count; i++)
            {
                GameObject go = m_targets[i];
                states[i] = go != null && go.activeSelf;
            }

            return states;
        }

        /// <summary>按捕获结果还原各目标的激活状态。</summary>
        public void RestoreActiveStates(bool[] states)
        {
            if (m_targets == null || states == null)
                return;

            int count = Mathf.Min(m_targets.Count, states.Length);
            for (int i = 0; i < count; i++)
            {
                GameObject go = m_targets[i];
                if (go == null)
                    continue;
                if (go.activeSelf != states[i])
                    go.SetActive(states[i]);
            }
        }
    }
}
