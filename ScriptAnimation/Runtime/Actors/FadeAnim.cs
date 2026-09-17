using System;
using System.Collections.Generic;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 批量管理自身与子节点 Renderer 的透明度（MaterialPropertyBlock，不改 sharedMaterial）。
    /// 绑定到 <see cref="ScriptAnimTrackBase"/>，由 <see cref="FadeClip"/> 驱动。
    /// </summary>
    [AddComponentMenu("ScriptAnimation/透明度 (FadeAnim)")]
    public class FadeAnim : ScriptAnimActor
    {
        [Serializable]
        private struct FadeTarget
        {
            public Renderer Renderer;
            public int MaterialIndex;
            public int ColorPropertyId;
            public Color BaseColor;
            public bool IsSprite;
        }

        [Header("收集范围")]
        [InspectorLabel("包含自身")]
        [SerializeField] private bool m_includeSelf = true;
        [InspectorLabel("包含未激活子物体")]
        [SerializeField] private bool m_includeInactiveChildren = true;

        [Tooltip("按顺序尝试的颜色属性；命中第一个即缓存")]
        [InspectorLabel("颜色属性名")]
        [SerializeField]
        private string[] m_colorPropertyNames = { "_BaseColor", "_Color", "_TintColor" };

        [Header("运行时")]
        [InspectorLabel("当前透明度")]
        [SerializeField, Range(0f, 1f)] private float m_currentAlpha = 1f;

        private readonly List<FadeTarget> m_targets = new List<FadeTarget>(32);
        private MaterialPropertyBlock m_block;
        private bool m_built;
        float m_restAlpha;
        bool m_hasRest;

        public float CurrentAlpha => m_currentAlpha;
        public int TargetCount => m_targets.Count;

        public void CaptureRestIfNeeded()
        {
            if (m_hasRest)
                return;
            m_restAlpha = m_currentAlpha;
            m_hasRest = true;
        }

        public void RevertToRest()
        {
            if (!m_hasRest)
                return;
            SetAlpha(m_restAlpha);
            m_hasRest = false;
        }

        public void ClearRest() => m_hasRest = false;

        /// <summary>保持 Rest 缓存并写回 Rest 透明度（首 Clip 之前 seek 用）。</summary>
        public void ApplyCapturedRest()
        {
            if (!m_hasRest)
                return;
            SetAlpha(m_restAlpha);
        }

        private void Awake()
        {
            RebuildTargets();
        }

        private void OnValidate()
        {
            if (!Application.isPlaying)
                return;
            if (m_built)
                ApplyAlpha(m_currentAlpha);
        }

#if UNITY_EDITOR
        /// <summary>Timeline GatherProperties 还原序列化字段后，同步 MaterialPropertyBlock。</summary>
        void OnDidApplyAnimationProperties()
        {
            if (m_built)
                ApplyAlpha(m_currentAlpha);
        }
#endif

        /// <summary>重新收集子树 Renderer 并缓存基准色。</summary>
        [ContextMenu("重建透明目标")]
        public void RebuildTargets()
        {
            m_targets.Clear();
            m_built = false;

            if (m_block == null)
                m_block = new MaterialPropertyBlock();

            var renderers = GetComponentsInChildren<Renderer>(m_includeInactiveChildren);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                    continue;
                if (!m_includeSelf && renderer.transform == transform)
                    continue;

                if (renderer is SpriteRenderer sprite)
                {
                    m_targets.Add(new FadeTarget
                    {
                        Renderer = sprite,
                        MaterialIndex = 0,
                        ColorPropertyId = 0,
                        BaseColor = sprite.color,
                        IsSprite = true
                    });
                    continue;
                }

                Material[] shared = renderer.sharedMaterials;
                if (shared == null || shared.Length == 0)
                    continue;

                for (int mi = 0; mi < shared.Length; mi++)
                {
                    Material mat = shared[mi];
                    if (mat == null)
                        continue;
                    if (!TryResolveColorProperty(mat, out int propId, out Color baseColor))
                        continue;

                    m_targets.Add(new FadeTarget
                    {
                        Renderer = renderer,
                        MaterialIndex = mi,
                        ColorPropertyId = propId,
                        BaseColor = baseColor,
                        IsSprite = false
                    });
                }
            }

            m_built = true;
            ApplyAlpha(m_currentAlpha);
        }

        /// <summary>将所有目标的 alpha 设为 [0,1]（保留缓存的 RGB）。alpha 未变则跳过写入。</summary>
        public void SetAlpha(float alpha)
        {
            float clamped = Mathf.Clamp01(alpha);
            if (!m_built)
            {
                m_currentAlpha = clamped;
                RebuildTargets();
                return;
            }

            if (Mathf.Abs(clamped - m_currentAlpha) <= 1e-5f)
                return;

            m_currentAlpha = clamped;
            ApplyAlpha(m_currentAlpha);
        }

        private void ApplyAlpha(float alpha)
        {
            if (m_block == null)
                m_block = new MaterialPropertyBlock();

            for (int i = 0; i < m_targets.Count; i++)
            {
                FadeTarget target = m_targets[i];
                if (target.Renderer == null)
                    continue;

                Color color = target.BaseColor;
                color.a = alpha;

                if (target.IsSprite)
                {
                    var sprite = (SpriteRenderer)target.Renderer;
                    sprite.color = color;
                    continue;
                }

                target.Renderer.GetPropertyBlock(m_block, target.MaterialIndex);
                m_block.SetColor(target.ColorPropertyId, color);
                target.Renderer.SetPropertyBlock(m_block, target.MaterialIndex);
            }
        }

        private bool TryResolveColorProperty(Material mat, out int propId, out Color color)
        {
            propId = 0;
            color = Color.white;
            if (mat == null || m_colorPropertyNames == null)
                return false;

            for (int i = 0; i < m_colorPropertyNames.Length; i++)
            {
                string name = m_colorPropertyNames[i];
                if (string.IsNullOrEmpty(name) || !mat.HasProperty(name))
                    continue;

                propId = Shader.PropertyToID(name);
                color = mat.GetColor(propId);
                return true;
            }

            return false;
        }
    }
}
