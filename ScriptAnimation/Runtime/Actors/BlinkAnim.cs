using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 在 Clip 时长内按次数快速切换目标显隐，由 <see cref="BlinkClip"/> 驱动。
    /// 建议挂在父物体上，通过「目标」控制子物体；若目标为本物体，隐藏时改用 Renderer.enabled，避免 SetActive 停掉 Timeline 采样。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("ScriptAnimation/间隔显隐 (BlinkAnim)")]
    public class BlinkAnim : ScriptAnimActor
    {
        [Header("目标")]
        [Tooltip("被切换显隐的对象；为空则使用自身。")]
        [InspectorLabel("目标")]
        [SerializeField] private GameObject m_target;

        [Header("运行时")]
        [InspectorLabel("当前可见")]
        [SerializeField] private bool m_visible = true;

        bool m_restVisible;
        bool m_hasRest;

        /// <summary>目标（未指定时为自身）。</summary>
        public GameObject Target => m_target != null ? m_target : gameObject;

        public bool IsVisible => m_visible;

        public void CaptureRestIfNeeded()
        {
            if (m_hasRest)
                return;
            m_restVisible = m_visible;
            m_hasRest = true;
        }

        public void RevertToRest()
        {
            if (!m_hasRest)
                return;
            SetVisible(m_restVisible);
            m_hasRest = false;
        }

        public void ClearRest() => m_hasRest = false;

        /// <summary>保持 Rest 缓存并写回 Rest 显隐（首 Clip 之前 seek 用）。</summary>
        public void ApplyCapturedRest()
        {
            if (!m_hasRest)
                return;
            SetVisible(m_restVisible);
        }

#if UNITY_EDITOR
        /// <summary>Timeline GatherProperties 还原 m_visible 后，同步显隐。</summary>
        void OnDidApplyAnimationProperties()
        {
            SetVisible(m_visible);
        }
#endif

        /// <summary>写入显隐；目标为本物体时用 Renderer.enabled。</summary>
        public void SetVisible(bool visible)
        {
            m_visible = visible;
            GameObject go = Target;
            if (go == null)
                return;

            if (go == gameObject)
            {
                var renderers = go.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i] != null)
                        renderers[i].enabled = visible;
                }

                return;
            }

            if (go.activeSelf != visible)
                go.SetActive(visible);
        }
    }
}
