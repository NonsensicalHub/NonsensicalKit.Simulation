using System;
using System.Collections.Generic;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    public enum FilmWrapRiseMode
    {
        [InspectorName("按圈数")]
        Turns = 0,
        [InspectorName("按每圈上升高度")]
        Pitch = 1
    }

    /// <summary>
    /// 缠膜显示：用专用 Shader 按螺旋上升进度裁切已有膜模型，不改原材质球资源。
    /// 绑定到 <see cref="ScriptAnimTrackBase"/>，由 <see cref="FilmWrapClip"/> 驱动。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("ScriptAnimation/缠膜 (FilmWrapAnim)")]
    public class FilmWrapAnim : ScriptAnimActor
    {
        public const string WrapShaderName = "NonsensicalKit/ScriptAnimation/FilmWrap";

        static readonly int WrapParamsId = Shader.PropertyToID("_WrapParams");
        static readonly int WrapHeightId = Shader.PropertyToID("_WrapHeight");
        static readonly int WrapCenterId = Shader.PropertyToID("_WrapCenter");
        static readonly int WrapAxisId = Shader.PropertyToID("_WrapAxis");

        [Header("收集范围")]
        [InspectorLabel("包含自身")]
        [SerializeField] bool m_includeSelf = true;
        [InspectorLabel("包含未激活子物体")]
        [SerializeField] bool m_includeInactiveChildren = true;
        [Tooltip("为空则收集自身与子节点 Renderer")]
        [InspectorLabel("指定 Renderer")]
        [SerializeField] Renderer[] m_explicitRenderers;
        [Tooltip("留空则按名称查找 NonsensicalKit/ScriptAnimation/FilmWrap")]
        [InspectorLabel("缠膜 Shader")]
        [SerializeField] Shader m_shaderOverride;

        [Header("螺旋")]
        [InspectorLabel("本地轴向")]
        [SerializeField] Vector3 m_localAxis = Vector3.up;
        [InspectorLabel("顺时针")]
        [SerializeField] bool m_clockwise = true;
        [InspectorLabel("起始角（度）")]
        [SerializeField] float m_startAngleDegrees;
        [Tooltip("膜头软边，单位圈。 为硬切。")]
        [InspectorLabel("软边（圈）")]
        [SerializeField] [Min(0f)] float m_feather = 0.04f;

        [Header("上升")]
        [InspectorLabel("自下而上")]
        [SerializeField] bool m_riseUp = true;
        [InspectorLabel("上升模式")]
        [SerializeField] FilmWrapRiseMode m_riseMode = FilmWrapRiseMode.Turns;
        [Tooltip("从轴向最低到最高总共绕多少圈")]
        [InspectorLabel("圈数")]
        [SerializeField] [Min(0.01f)] float m_turns = 8f;
        [Tooltip("每绕一圈沿轴上升的高度（米）")]
        [InspectorLabel("每圈上升（米）")]
        [SerializeField] [Min(0.001f)] float m_pitch = 0.15f;

        [Header("范围")]
        [InspectorLabel("范围取包围盒")]
        [SerializeField] bool m_autoBounds = true;
        [InspectorLabel("中心偏移（本地）")]
        [SerializeField] Vector3 m_centerOffset;
        [InspectorLabel("轴向下限（本地）")]
        [SerializeField] float m_manualAxisMin;
        [InspectorLabel("轴向上限（本地）")]
        [SerializeField] float m_manualAxisMax = 1f;

        [Header("运行时")]
        [InspectorLabel("当前进度")]
        [SerializeField, Range(0f, 1f)] float m_currentProgress = 1f;
        [InspectorLabel("编辑模式预览")]
        [SerializeField] bool m_previewInEditMode = true;

        readonly List<WrapTarget> m_targets = new List<WrapTarget>(8);
        MaterialPropertyBlock m_block;
        Shader m_wrapShader;
        bool m_built;
        bool m_materialsApplied;
        bool m_timelineDriven;
        float m_restProgress;
        bool m_hasRest;
        float m_lastAppliedProgress = -1f;

        struct WrapTarget
        {
            public Renderer Renderer;
            public Material[] OriginalShared;
            public Material[] WrapInstances;
            public bool[] Owned;
        }

        public float CurrentProgress => m_currentProgress;
        public int TargetCount => m_targets.Count;
        public bool TimelineDriven => m_timelineDriven;
        public bool HasRestProgress => m_hasRest;
        public float RestProgress => m_restProgress;
        public bool PreviewInEditMode => m_previewInEditMode;
        public float ResolvedTurns => ResolveTurns(out _, out _, out _);
        public float ResolvedPitch => ResolvePitch();

        public void BeginTimelineDrive()
        {
            m_timelineDriven = true;
            EnsureWrapMaterials();
        }

        public void EndTimelineDrive()
        {
            m_timelineDriven = false;
        }

        public void CaptureRestIfNeeded()
        {
            if (m_hasRest)
                return;
            m_restProgress = m_currentProgress;
            m_hasRest = true;
        }

        public void RevertToRest()
        {
            if (!m_hasRest)
                return;
            SetProgress(m_restProgress);
            m_hasRest = false;
        }

        public void ClearRest() => m_hasRest = false;

#if UNITY_EDITOR
        /// <summary>Timeline GatherProperties 还原 m_currentProgress 后，同步缠膜 Shader 参数。</summary>
        void OnDidApplyAnimationProperties()
        {
            if (!m_materialsApplied)
                return;
            ApplyProgress(m_currentProgress, force: true);
        }
#endif

        void OnEnable()
        {
            RebuildTargets();
            if (Application.isPlaying || m_previewInEditMode)
            {
                EnsureWrapMaterials();
                ApplyProgress(m_currentProgress, force: true);
            }
        }

        void OnDisable()
        {
            RestoreOriginalMaterials();
        }

        void OnDestroy()
        {
            RestoreOriginalMaterials();
        }

        void LateUpdate()
        {
            if (!m_materialsApplied)
                return;
            if (!Application.isPlaying && !m_previewInEditMode && !m_timelineDriven)
                return;
            ApplyProgress(m_currentProgress, force: true);
        }

        void OnValidate()
        {
            m_turns = Mathf.Max(0.01f, m_turns);
            m_pitch = Mathf.Max(0.001f, m_pitch);
            m_feather = Mathf.Max(0f, m_feather);
            if (m_localAxis.sqrMagnitude < 1e-8f)
                m_localAxis = Vector3.up;
            if (m_built && isActiveAndEnabled && (Application.isPlaying || m_previewInEditMode))
                ApplyProgress(m_currentProgress, force: true);
        }

        [ContextMenu("重建缠膜目标")]
        public void RebuildTargets()
        {
            bool wasApplied = m_materialsApplied;
            if (wasApplied)
                RestoreOriginalMaterials();

            CollectTargets();

            if (wasApplied || Application.isPlaying || m_previewInEditMode)
            {
                EnsureWrapMaterials();
                ApplyProgress(m_currentProgress, force: true);
            }
        }

        void CollectTargets()
        {
            m_targets.Clear();
            m_built = false;
            m_lastAppliedProgress = -1f;

            if (m_block == null)
                m_block = new MaterialPropertyBlock();

            if (m_explicitRenderers != null && m_explicitRenderers.Length > 0)
            {
                for (int i = 0; i < m_explicitRenderers.Length; i++)
                    TryAddRenderer(m_explicitRenderers[i]);
            }
            else
            {
                var renderers = GetComponentsInChildren<Renderer>(m_includeInactiveChildren);
                for (int i = 0; i < renderers.Length; i++)
                {
                    Renderer renderer = renderers[i];
                    if (renderer == null)
                        continue;
                    if (!m_includeSelf && renderer.transform == transform)
                        continue;
                    TryAddRenderer(renderer);
                }
            }

            m_built = true;
        }

        public void SetProgress(float progress)
        {
            m_currentProgress = Mathf.Clamp01(progress);
            if (!m_built)
                CollectTargets();
            EnsureWrapMaterials();
            ApplyProgress(m_currentProgress, force: true);
        }

        public void RestoreOriginalMaterials()
        {
            if (!m_materialsApplied)
                return;

            for (int i = 0; i < m_targets.Count; i++)
            {
                WrapTarget target = m_targets[i];
                if (target.Renderer != null && target.OriginalShared != null)
                    target.Renderer.sharedMaterials = target.OriginalShared;
                DestroyOwned(target);
                target.WrapInstances = null;
                target.Owned = null;
                m_targets[i] = target;
            }

            m_materialsApplied = false;
            m_lastAppliedProgress = -1f;
        }

        public bool TryGetHelixWorld(
            out Vector3 origin, out Vector3 axis, out float axisMin, out float axisMax, out float radius)
        {
            ComputeHelix(out origin, out axis, out axisMin, out axisMax, out radius, out _, out _);
            return axis.sqrMagnitude > 1e-8f;
        }

        void TryAddRenderer(Renderer renderer)
        {
            if (renderer == null || renderer is ParticleSystemRenderer)
                return;

            m_targets.Add(new WrapTarget
            {
                Renderer = renderer,
                OriginalShared = null,
                WrapInstances = null,
                Owned = null
            });
        }

        void EnsureWrapMaterials()
        {
            if (m_materialsApplied)
                return;
            if (!m_built)
                CollectTargets();

            Shader shader = ResolveShader();
            if (shader == null)
            {
                Debug.LogWarning(
                    $"[FilmWrap] 找不到 Shader「{WrapShaderName}」。", this);
                return;
            }

            for (int i = 0; i < m_targets.Count; i++)
            {
                WrapTarget target = m_targets[i];
                Renderer renderer = target.Renderer;
                if (renderer == null)
                    continue;

                Material[] original = renderer.sharedMaterials;
                target.OriginalShared = original;
                if (original == null || original.Length == 0)
                {
                    m_targets[i] = target;
                    continue;
                }

                var wrapMats = new Material[original.Length];
                var owned = new bool[original.Length];
                bool anySwap = false;
                for (int mi = 0; mi < original.Length; mi++)
                {
                    Material src = original[mi];
                    if (src != null && src.HasProperty(WrapParamsId) && src.shader == shader)
                    {
                        wrapMats[mi] = src;
                        continue;
                    }

                    wrapMats[mi] = CreateWrapMaterial(src, shader);
                    owned[mi] = wrapMats[mi] != null;
                    anySwap = true;
                }

                target.WrapInstances = wrapMats;
                target.Owned = owned;
                if (anySwap)
                    renderer.sharedMaterials = wrapMats;
                m_targets[i] = target;
            }

            m_materialsApplied = true;
        }

        Material CreateWrapMaterial(Material src, Shader shader)
        {
            var mat = new Material(shader)
            {
                name = src != null ? src.name + " (FilmWrap)" : "FilmWrap",
                hideFlags = HideFlags.HideAndDontSave,
                enableInstancing = false
            };

            if (src != null)
                CopySurfaceProperties(src, mat);

            return mat;
        }

        static void CopySurfaceProperties(Material src, Material dst)
        {
            if (src.HasProperty("_BaseMap"))
                CopyTextureProperty(src, dst, "_BaseMap");
            else if (src.HasProperty("_MainTex") && dst.HasProperty("_BaseMap"))
            {
                Texture map = src.GetTexture("_MainTex");
                if (map != null)
                {
                    dst.SetTexture("_BaseMap", map);
                    dst.SetTextureScale("_BaseMap", src.GetTextureScale("_MainTex"));
                    dst.SetTextureOffset("_BaseMap", src.GetTextureOffset("_MainTex"));
                }
            }

            CopyTextureProperty(src, dst, "_BumpMap");
            CopyTextureProperty(src, dst, "_EmissionMap");

            CopyColorProperty(src, dst, "_BaseColor", "_Color");
            CopyColorProperty(src, dst, "_EmissionColor");

            CopyFloatProperty(src, dst, "_Metallic");
            CopyFloatProperty(src, dst, "_Smoothness");
            CopyFloatProperty(src, dst, "_BumpScale");
            CopyFloatProperty(src, dst, "_SpecularHighlights");
            CopyFloatProperty(src, dst, "_EnvironmentReflections");

            if (src.IsKeywordEnabled("_SPECULARHIGHLIGHTS_OFF"))
                dst.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            else
                dst.DisableKeyword("_SPECULARHIGHLIGHTS_OFF");

            if (src.IsKeywordEnabled("_ENVIRONMENTREFLECTIONS_OFF"))
                dst.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            else
                dst.DisableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        }

        static void CopyTextureProperty(Material src, Material dst, string propertyName)
        {
            if (src == null || dst == null || !src.HasProperty(propertyName) || !dst.HasProperty(propertyName))
                return;

            Texture texture = src.GetTexture(propertyName);
            if (texture == null)
                return;

            dst.SetTexture(propertyName, texture);
            dst.SetTextureScale(propertyName, src.GetTextureScale(propertyName));
            dst.SetTextureOffset(propertyName, src.GetTextureOffset(propertyName));
        }

        static void CopyColorProperty(Material src, Material dst, string propertyName, string fallbackName = null)
        {
            if (src == null || dst == null || !dst.HasProperty(propertyName))
                return;

            if (src.HasProperty(propertyName))
            {
                dst.SetColor(propertyName, src.GetColor(propertyName));
                return;
            }

            if (!string.IsNullOrEmpty(fallbackName) && src.HasProperty(fallbackName))
                dst.SetColor(propertyName, src.GetColor(fallbackName));
        }

        static void CopyFloatProperty(Material src, Material dst, string propertyName)
        {
            if (src == null || dst == null || !src.HasProperty(propertyName) || !dst.HasProperty(propertyName))
                return;

            dst.SetFloat(propertyName, src.GetFloat(propertyName));
        }

        void DestroyOwned(in WrapTarget target)
        {
            if (target.WrapInstances == null || target.Owned == null)
                return;

            for (int i = 0; i < target.WrapInstances.Length; i++)
            {
                if (!target.Owned[i])
                    continue;
                Material mat = target.WrapInstances[i];
                if (mat == null)
                    continue;
                if (Application.isPlaying)
                    Destroy(mat);
                else
                    DestroyImmediate(mat);
            }
        }

        Shader ResolveShader()
        {
            if (m_shaderOverride != null)
                return m_shaderOverride;
            if (m_wrapShader == null)
                m_wrapShader = Shader.Find(WrapShaderName);
            return m_wrapShader;
        }

        void ApplyProgress(float progress, bool force)
        {
            if (!m_materialsApplied)
                return;
            if (!force && Mathf.Abs(progress - m_lastAppliedProgress) <= 1e-5f)
                return;

            m_lastAppliedProgress = progress;
            m_currentProgress = Mathf.Clamp01(progress);

            ComputeHelix(
                out Vector3 originWS, out Vector3 axisWS, out float axisMinWS, out float axisMaxWS,
                out _, out float turns, out _);

            if (m_block == null)
                m_block = new MaterialPropertyBlock();

            Vector4 wrapParams = new Vector4(
                m_currentProgress,
                turns,
                m_startAngleDegrees * Mathf.Deg2Rad,
                m_currentProgress >= 1f - 1e-5f ? 0f : m_feather);
            float clockwise = m_clockwise ? 1f : 0f;
            float riseUp = m_riseUp ? 1f : 0f;

            for (int i = 0; i < m_targets.Count; i++)
            {
                WrapTarget target = m_targets[i];
                Renderer renderer = target.Renderer;
                if (renderer == null)
                    continue;

                Transform tf = renderer.transform;
                Vector3 centerOS = tf.InverseTransformPoint(originWS);
                Vector3 axisOS = tf.InverseTransformDirection(axisWS);
                if (axisOS.sqrMagnitude < 1e-8f)
                    axisOS = Vector3.up;
                else
                    axisOS.Normalize();

                Vector3 minPtOS = tf.InverseTransformPoint(originWS + axisWS * axisMinWS);
                Vector3 maxPtOS = tf.InverseTransformPoint(originWS + axisWS * axisMaxWS);
                float yMinOS = Vector3.Dot(minPtOS - centerOS, axisOS);
                float yMaxOS = Vector3.Dot(maxPtOS - centerOS, axisOS);

                Vector4 wrapHeight = new Vector4(yMinOS, yMaxOS, clockwise, riseUp);
                Vector4 wrapCenter = centerOS;
                Vector4 wrapAxis = axisOS;

                int matCount = renderer.sharedMaterials != null ? renderer.sharedMaterials.Length : 1;
                for (int mi = 0; mi < matCount; mi++)
                {
                    Material owned = null;
                    if (target.WrapInstances != null &&
                        target.Owned != null &&
                        mi < target.WrapInstances.Length &&
                        mi < target.Owned.Length &&
                        target.Owned[mi])
                        owned = target.WrapInstances[mi];

                    if (owned != null)
                    {
                        owned.SetVector(WrapParamsId, wrapParams);
                        owned.SetVector(WrapHeightId, wrapHeight);
                        owned.SetVector(WrapCenterId, wrapCenter);
                        owned.SetVector(WrapAxisId, wrapAxis);
                    }

                    renderer.GetPropertyBlock(m_block, mi);
                    m_block.SetVector(WrapParamsId, wrapParams);
                    m_block.SetVector(WrapHeightId, wrapHeight);
                    m_block.SetVector(WrapCenterId, wrapCenter);
                    m_block.SetVector(WrapAxisId, wrapAxis);
                    renderer.SetPropertyBlock(m_block, mi);
                }
            }
        }

        float ResolveTurns(out Vector3 origin, out Vector3 axis, out float heightRange)
        {
            ComputeHelix(out origin, out axis, out float axisMin, out float axisMax, out _, out float turns, out _);
            heightRange = Mathf.Abs(axisMax - axisMin);
            return turns;
        }

        float ResolvePitch()
        {
            ComputeHelix(out _, out _, out float axisMin, out float axisMax, out _, out float turns, out _);
            float heightRange = Mathf.Max(1e-4f, Mathf.Abs(axisMax - axisMin));
            return heightRange / Mathf.Max(turns, 0.01f);
        }

        void ComputeHelix(
            out Vector3 origin,
            out Vector3 axis,
            out float axisMin,
            out float axisMax,
            out float radius,
            out float turns,
            out float pitch)
        {
            Vector3 localAxis = m_localAxis.sqrMagnitude > 1e-8f ? m_localAxis.normalized : Vector3.up;
            axis = transform.TransformDirection(localAxis);
            if (axis.sqrMagnitude < 1e-8f)
                axis = Vector3.up;
            else
                axis.Normalize();

            Bounds localBounds = ComputeLocalBounds();
            Vector3 localCenter = localBounds.center + m_centerOffset;

            if (m_autoBounds)
            {
                origin = transform.TransformPoint(localCenter);
                ProjectLocalBounds(localBounds, localAxis, localCenter, out axisMin, out axisMax, out radius);
                float scale = transform.TransformVector(localAxis).magnitude;
                if (scale < 1e-5f)
                    scale = 1f;
                axisMin *= scale;
                axisMax *= scale;
                radius *= scale;
            }
            else
            {
                origin = transform.TransformPoint(m_centerOffset);
                Vector3 minPt = transform.TransformPoint(m_centerOffset + localAxis * m_manualAxisMin);
                Vector3 maxPt = transform.TransformPoint(m_centerOffset + localAxis * m_manualAxisMax);
                axisMin = Vector3.Dot(minPt - origin, axis);
                axisMax = Vector3.Dot(maxPt - origin, axis);
                if (axisMax < axisMin)
                {
                    float tmp = axisMin;
                    axisMin = axisMax;
                    axisMax = tmp;
                }

                radius = Mathf.Max(localBounds.extents.x, localBounds.extents.z);
                float radialScale = transform.TransformVector(Vector3.right).magnitude;
                if (radialScale < 1e-5f)
                    radialScale = 1f;
                radius *= radialScale;
            }

            float heightRange = Mathf.Max(1e-4f, axisMax - axisMin);
            if (m_riseMode == FilmWrapRiseMode.Pitch)
            {
                pitch = Mathf.Max(0.001f, m_pitch);
                turns = heightRange / pitch;
            }
            else
            {
                turns = Mathf.Max(0.01f, m_turns);
                pitch = heightRange / turns;
            }
        }

        Bounds ComputeLocalBounds()
        {
            var bounds = new Bounds(Vector3.zero, Vector3.zero);
            bool has = false;
            for (int i = 0; i < m_targets.Count; i++)
            {
                Renderer renderer = m_targets[i].Renderer;
                if (renderer == null)
                    continue;

                Bounds world = renderer.bounds;
                EncapsulateWorldBounds(ref bounds, ref has, world);
            }

            if (!has)
                bounds = new Bounds(Vector3.zero, Vector3.one);
            return bounds;
        }

        void EncapsulateWorldBounds(ref Bounds localBounds, ref bool has, Bounds world)
        {
            Vector3 c = world.center;
            Vector3 e = world.extents;
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 worldPt = c + Vector3.Scale(e, new Vector3(x, y, z));
                Vector3 localPt = transform.InverseTransformPoint(worldPt);
                if (!has)
                {
                    localBounds = new Bounds(localPt, Vector3.zero);
                    has = true;
                }
                else
                    localBounds.Encapsulate(localPt);
            }
        }

        static void ProjectLocalBounds(
            Bounds localBounds, Vector3 localAxis, Vector3 localCenter,
            out float axisMin, out float axisMax, out float radius)
        {
            axisMin = float.MaxValue;
            axisMax = float.MinValue;
            radius = 0f;
            Vector3 c = localBounds.center;
            Vector3 e = localBounds.extents;
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 pt = c + Vector3.Scale(e, new Vector3(x, y, z));
                Vector3 rel = pt - localCenter;
                float h = Vector3.Dot(rel, localAxis);
                if (h < axisMin) axisMin = h;
                if (h > axisMax) axisMax = h;
                Vector3 radial = rel - localAxis * h;
                radius = Mathf.Max(radius, radial.magnitude);
            }

            if (axisMax < axisMin)
            {
                axisMin = 0f;
                axisMax = 1f;
            }
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            if (!m_built)
                CollectTargets();

            ComputeHelix(
                out Vector3 origin, out Vector3 axis, out float axisMin, out float axisMax,
                out float radius, out float turns, out _);

            Vector3 a = origin + axis * axisMin;
            Vector3 b = origin + axis * axisMax;
            Gizmos.color = new Color(0.3f, 0.85f, 1f, 0.9f);
            Gizmos.DrawLine(a, b);
            Gizmos.DrawSphere(a, 0.02f);
            Gizmos.DrawSphere(b, 0.02f);

            Vector3 helper = Mathf.Abs(axis.y) < 0.99f ? Vector3.up : Vector3.right;
            Vector3 tangent = Vector3.Cross(helper, axis).normalized;
            Vector3 bitangent = Vector3.Cross(axis, tangent);
            float start = m_startAngleDegrees * Mathf.Deg2Rad;
            int dir = m_clockwise ? -1 : 1;
            bool riseUp = m_riseUp;
            int steps = Mathf.Clamp(Mathf.RoundToInt(turns * 16f), 16, 256);
            Vector3 prev = Vector3.zero;
            Gizmos.color = new Color(1f, 0.7f, 0.2f, 0.85f);
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                float height01 = riseUp ? t : 1f - t;
                float h = Mathf.Lerp(axisMin, axisMax, height01);
                float ang = start + dir * t * turns * Mathf.PI * 2f;
                Vector3 p = origin + axis * h +
                            (tangent * Mathf.Cos(ang) + bitangent * Mathf.Sin(ang)) * Mathf.Max(0.02f, radius);
                if (i > 0)
                    Gizmos.DrawLine(prev, p);
                prev = p;
            }

            Gizmos.color = Color.green;
            Vector3 startDir = tangent * Mathf.Cos(start) + bitangent * Mathf.Sin(start);
            Gizmos.DrawRay(origin + axis * (m_riseUp ? axisMin : axisMax), startDir * Mathf.Max(0.05f, radius));
        }
#endif
    }
}
