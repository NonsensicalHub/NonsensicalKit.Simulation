using System;
using NaughtyAttributes;
using NonsensicalKit.Core;
using UnityEngine;


namespace TemperatureVisualization
{
    /// <summary>
    /// 温度场可视化模式。
    /// </summary>
    public enum VisualizationMode
    {
        /// <summary>仅渲染正交切片（XY/XZ/YZ），适合剖面分析；需开启对应切片开关。</summary>
        [InspectorName("切片 — 正交剖面")]
        Slice = 0,

        /// <summary>对整个立方体做 Raymarching 体积渲染，显示完整温度云图（默认推荐）。</summary>
        [InspectorName("体积 — 立方体云图")]
        Volume = 1,

        /// <summary>Marching Cubes 等温面网格，显示指定温度的三维等值面。</summary>
        [InspectorName("等温面 — 温度等值面")]
        Isosurface = 2,

        /// <summary>体积与切片可同时显示；等温面需在 Combined 下额外勾选 EnableIsosurfaceInCombined。</summary>
        [InspectorName("组合 — 体积+切片叠加")]
        Combined = 3
    }

    /// <summary>
    /// 温度场可视化总控制器，串联数据、插值与各渲染模式。
    /// </summary>
    public class TemperatureVisualizationController : MonoBehaviour
    {
        [SerializeField] private string ID;
        [SerializeField] private bool m_initShow = true;

        [Header("Core")]
        [Label("体积边界")]
        [SerializeField] private TemperatureVolumeBounds m_VolumeBounds;

        [Label("传感器管理器")]
        [SerializeField] private TemperatureSensorManager m_SensorManager;

        [Label("温度场插值器")]
        [SerializeField] private TemperatureFieldInterpolator m_Interpolator;

        [Label("色标")]
        [SerializeField] private TemperatureColorRamp m_ColorRamp;

        [Header("Renderers")]
        [Label("切片渲染器")]
        [SerializeField] private TemperatureSliceRenderer m_SliceRenderer;

        [Label("体积渲染器")]
        [SerializeField] private TemperatureVolumeRenderer m_VolumeRenderer;

        [Label("等温面渲染器")]
        [SerializeField] private TemperatureIsosurfaceRenderer m_IsosurfaceRenderer;

        [Header("Settings")]
        [Tooltip("切片：仅剖面平面 | 体积：立方体云图(Raymarching) | 等温面：Marching Cubes 等值面 | 组合：体积与切片叠加显示")]
        [Label("可视化模式")]
        [SerializeField] private VisualizationMode m_Mode = VisualizationMode.Volume;

        [Label("温度下限 (℃)")]
        [SerializeField] private float m_TempMin = 10f;

        [Label("温度上限 (℃)")]
        [SerializeField] private float m_TempMax = 40f;

        [Label("不透明度")]
        [SerializeField] private float m_Opacity = 0.06f;

        [Label("切面不透明度")]
        [SerializeField] private float m_sliceOpacity = 0.75f;

        [Label("组合模式下启用等温面")]
        [SerializeField] private bool m_EnableIsosurfaceInCombined;

        [Label("等温面重建间隔 (秒)")]
        [SerializeField] private float m_IsoRebuildInterval = 1.0f;

        private float m_IsoRebuildTimer;
        private bool m_Initialized;
        private bool m_RenderDirty = true;
        private bool m_ActiveVolume;
        private bool m_ActiveSlices;
        private bool m_ActiveIso;
        private byte m_EnabledSliceMask;
        private float m_LastTempMin;
        private float m_LastTempMax;
        private float m_LastOpacity;

        public TemperatureVolumeBounds VolumeBounds => m_VolumeBounds;
        public TemperatureSensorManager SensorManager => m_SensorManager;
        public TemperatureFieldInterpolator Interpolator => m_Interpolator;
        public TemperatureColorRamp ColorRamp => m_ColorRamp;
        public TemperatureSliceRenderer SliceRenderer => m_SliceRenderer;
        public TemperatureVolumeRenderer VolumeRenderer => m_VolumeRenderer;
        public TemperatureIsosurfaceRenderer IsosurfaceRenderer => m_IsosurfaceRenderer;

        public VisualizationMode Mode
        {
            get => m_Mode;
            set
            {
                if (m_Mode == value) return;
                m_Mode = value;
                m_RenderDirty = true;
                ApplyMode();
            }
        }

        public float TempMin
        {
            get => m_TempMin;
            set
            {
                if (Mathf.Approximately(m_TempMin, value)) return;
                m_TempMin = value;
                OnTemperatureRangeChanged();
            }
        }

        public float TempMax
        {
            get => m_TempMax;
            set
            {
                if (Mathf.Approximately(m_TempMax, value)) return;
                m_TempMax = value;
                OnTemperatureRangeChanged();
            }
        }

        public float Opacity
        {
            get => m_Opacity;
            set
            {
                float clamped = Mathf.Clamp01(value);
                if (Mathf.Approximately(m_Opacity, clamped)) return;
                m_Opacity = clamped;
                m_RenderDirty = true;
            }
        }

        private void Reset()
        {
            AutoWireReferences();
        }

        private void Awake()
        {
            IOCC.Set(ID, this);
            SubscribeUiCommands();
            AutoWireReferences();
            this.gameObject.SetActive(m_initShow);
        }

        private void SubscribeUiCommands()
        {
            if (string.IsNullOrEmpty(ID))
            {
                Debug.LogWarning($"[{nameof(TemperatureVisualizationController)}] ID 为空，无法订阅 UI 命令。", this);
                return;
            }

            IOCC.Subscribe<bool>(TemperatureVisualEvents.SetVisible, ID, OnUiSetVisible);
            IOCC.Subscribe<int>(TemperatureVisualEvents.SetMode, ID, OnUiSetMode);
            IOCC.Subscribe<float>(TemperatureVisualEvents.SetSlicePositionXY, ID, OnUiSetSlicePositionXY);
            IOCC.Subscribe<float>(TemperatureVisualEvents.SetSlicePositionXZ, ID, OnUiSetSlicePositionXZ);
            IOCC.Subscribe<float>(TemperatureVisualEvents.SetSlicePositionYZ, ID, OnUiSetSlicePositionYZ);
            IOCC.Subscribe<bool>(TemperatureVisualEvents.SetShowSliceXY, ID, OnUiSetShowSliceXY);
            IOCC.Subscribe<bool>(TemperatureVisualEvents.SetShowSliceXZ, ID, OnUiSetShowSliceXZ);
            IOCC.Subscribe<bool>(TemperatureVisualEvents.SetShowSliceYZ, ID, OnUiSetShowSliceYZ);
            IOCC.Subscribe<float>(TemperatureVisualEvents.SetIsoTemperature, ID, OnUiSetIsoTemperature);
        }

        private void UnsubscribeUiCommands()
        {
            if (string.IsNullOrEmpty(ID)) return;

            IOCC.Unsubscribe<bool>(TemperatureVisualEvents.SetVisible, ID, OnUiSetVisible);
            IOCC.Unsubscribe<int>(TemperatureVisualEvents.SetMode, ID, OnUiSetMode);
            IOCC.Unsubscribe<float>(TemperatureVisualEvents.SetSlicePositionXY, ID, OnUiSetSlicePositionXY);
            IOCC.Unsubscribe<float>(TemperatureVisualEvents.SetSlicePositionXZ, ID, OnUiSetSlicePositionXZ);
            IOCC.Unsubscribe<float>(TemperatureVisualEvents.SetSlicePositionYZ, ID, OnUiSetSlicePositionYZ);
            IOCC.Unsubscribe<bool>(TemperatureVisualEvents.SetShowSliceXY, ID, OnUiSetShowSliceXY);
            IOCC.Unsubscribe<bool>(TemperatureVisualEvents.SetShowSliceXZ, ID, OnUiSetShowSliceXZ);
            IOCC.Unsubscribe<bool>(TemperatureVisualEvents.SetShowSliceYZ, ID, OnUiSetShowSliceYZ);
            IOCC.Unsubscribe<float>(TemperatureVisualEvents.SetIsoTemperature, ID, OnUiSetIsoTemperature);
        }

        private void OnUiSetVisible(bool visible)
        {
            gameObject.SetActive(visible);
        }

        private void OnUiSetMode(int index)
        {
            Mode = (VisualizationMode)index;
            MarkRenderDirty();
        }

        private void OnUiSetSlicePositionXY(float value)
        {
            if (m_SliceRenderer == null) return;
            m_SliceRenderer.PositionXY = value;
        }

        private void OnUiSetSlicePositionXZ(float value)
        {
            if (m_SliceRenderer == null) return;
            m_SliceRenderer.PositionXZ = value;
        }

        private void OnUiSetSlicePositionYZ(float value)
        {
            if (m_SliceRenderer == null) return;
            m_SliceRenderer.PositionYZ = value;
        }

        private void OnUiSetShowSliceXY(bool visible)
        {
            if (m_SliceRenderer == null) return;
            m_SliceRenderer.ShowSliceXY = visible;
            MarkRenderDirty();
        }

        private void OnUiSetShowSliceXZ(bool visible)
        {
            if (m_SliceRenderer == null) return;
            m_SliceRenderer.ShowSliceXZ = visible;
            MarkRenderDirty();
        }

        private void OnUiSetShowSliceYZ(bool visible)
        {
            if (m_SliceRenderer == null) return;
            m_SliceRenderer.ShowSliceYZ = visible;
            MarkRenderDirty();
        }

        private void OnUiSetIsoTemperature(float value)
        {
            if (m_IsosurfaceRenderer == null) return;
            m_IsosurfaceRenderer.IsoTemperature = value;
            RebuildIsosurface();
        }

        public void Initialize(
            TemperatureVolumeBounds volumeBounds,
            TemperatureSensorManager sensorManager,
            TemperatureFieldInterpolator interpolator,
            TemperatureColorRamp colorRamp,
            TemperatureSliceRenderer sliceRenderer,
            TemperatureVolumeRenderer volumeRenderer,
            TemperatureIsosurfaceRenderer isosurfaceRenderer)
        {
            m_VolumeBounds = volumeBounds;
            m_SensorManager = sensorManager;
            m_Interpolator = interpolator;
            m_ColorRamp = colorRamp;
            m_SliceRenderer = sliceRenderer;
            m_VolumeRenderer = volumeRenderer;
            m_IsosurfaceRenderer = isosurfaceRenderer;

            WireDependencies();
            m_Initialized = true;
            m_RenderDirty = true;
            BeginVisualization();
        }

        private void Start()
        {
            if (!m_Initialized)
            {
                AutoWireReferences();
                WireDependencies();
                m_Initialized = true;
            }

            BeginVisualization();
        }

        private void BeginVisualization()
        {
            if (m_Interpolator != null)
            {
                m_Interpolator.TextureUpdated -= OnTextureUpdated;
                m_Interpolator.TextureUpdated += OnTextureUpdated;
            }

            if (m_SliceRenderer != null)
            {
                m_SliceRenderer.SlicePositionChanged -= OnSlicePositionChanged;
                m_SliceRenderer.SlicePositionChanged += OnSlicePositionChanged;
            }

            ApplyMode();
            m_RenderDirty = true;
            RefreshActiveFlags();
            m_Interpolator?.RequestRebuild(force: true);
        }

        private void OnEnable()
        {
            if (m_Initialized)
            {
                BeginVisualization();
            }
        }

        private void OnDisable()
        {
            if (m_Interpolator != null)
            {
                m_Interpolator.TextureUpdated -= OnTextureUpdated;
            }

            if (m_SliceRenderer != null)
            {
                m_SliceRenderer.SlicePositionChanged -= OnSlicePositionChanged;
            }
        }

        private void LateUpdate()
        {
            if (!CanRender()) return;

            if (m_ActiveSlices && m_EnabledSliceMask != 0 && m_SliceRenderer != null)
            {
                m_SliceRenderer.Render(m_Interpolator, m_ColorRamp, m_VolumeBounds, m_TempMin, m_TempMax, m_sliceOpacity, m_EnabledSliceMask);
            }

            if (m_ActiveVolume && m_VolumeRenderer != null)
            {
                // 每帧同步，保证父物体移动/旋转后体积盒与插值场仍对齐
                m_VolumeRenderer.ApplySharedState(m_Interpolator, m_ColorRamp, m_VolumeBounds, m_TempMin, m_TempMax, m_Opacity);
            }

            if (m_RenderDirty
                || !Mathf.Approximately(m_LastTempMin, m_TempMin)
                || !Mathf.Approximately(m_LastTempMax, m_TempMax)
                || !Mathf.Approximately(m_LastOpacity, m_Opacity))
            {
                m_LastTempMin = m_TempMin;
                m_LastTempMax = m_TempMax;
                m_LastOpacity = m_Opacity;
                m_RenderDirty = false;
            }

            if (m_ActiveIso)
            {
                m_IsosurfaceRenderer?.SyncBoundsTransformIfChanged(m_VolumeBounds);

                m_IsoRebuildTimer += Time.deltaTime;
                if (m_IsoRebuildTimer >= m_IsoRebuildInterval)
                {
                    m_IsoRebuildTimer = 0f;
                    RebuildIsosurface();
                }
            }
        }

        private void OnDestroy()
        {
            UnsubscribeUiCommands();
            IOCC.Set<TemperatureVisualizationController>(ID, null);
        }

        public void MarkRenderDirty()
        {
            m_RenderDirty = true;
            RefreshActiveFlags();
        }

        public void ApplyMode()
        {
            bool volume = m_Mode == VisualizationMode.Volume || m_Mode == VisualizationMode.Combined;
            bool iso = IsIsosurfaceModeActive();

            if (m_SliceRenderer != null)
            {
                m_SliceRenderer.gameObject.SetActive(true);
                if (m_Mode == VisualizationMode.Slice)
                {
                    EnsureDefaultSliceVisible();
                }
            }

            m_VolumeRenderer?.SetEnabled(volume);
            m_IsosurfaceRenderer?.SetEnabled(iso);
            RefreshActiveFlags();

            if (iso)
            {
                RebuildIsosurface();
            }

            m_RenderDirty = true;
        }

        private void EnsureDefaultSliceVisible()
        {
            if (m_SliceRenderer == null) return;
            if (m_SliceRenderer.ShowSliceXY || m_SliceRenderer.ShowSliceXZ || m_SliceRenderer.ShowSliceYZ) return;
            m_SliceRenderer.ShowSliceXY = true;
        }

        public void RebuildField()
        {
            m_Interpolator?.RequestRebuild();
        }

        public void RebuildIsosurface()
        {
            if (!IsIsosurfaceModeActive()) return;
            if (m_IsosurfaceRenderer == null || m_Interpolator == null || m_ColorRamp == null || m_VolumeBounds == null) return;
            m_IsosurfaceRenderer.RebuildMesh(m_Interpolator, m_ColorRamp, m_VolumeBounds, m_TempMin, m_TempMax);
            m_IsosurfaceRenderer.SetEnabled(true);
        }

        public void ApplyColorPreset(int index)
        {
            if (m_ColorRamp == null) return;
            m_ColorRamp.ApplyPreset(index);
            m_VolumeRenderer?.InvalidateMaterialCache();
            if (m_ActiveIso)
            {
                RebuildIsosurface();
            }

            m_RenderDirty = true;
        }

        private void OnTemperatureRangeChanged()
        {
            m_RenderDirty = true;
            if (m_ActiveIso)
            {
                RebuildIsosurface();
            }
        }

        private void OnTextureUpdated()
        {
            m_RenderDirty = true;
            m_VolumeRenderer?.InvalidateMaterialCache();
            m_SliceRenderer?.InvalidateCache();
        }

        private bool IsIsosurfaceModeActive()
        {
            return m_Mode == VisualizationMode.Isosurface
                   || (m_Mode == VisualizationMode.Combined && m_EnableIsosurfaceInCombined);
        }

        private void RefreshActiveFlags()
        {
            m_ActiveVolume = m_Mode == VisualizationMode.Volume || m_Mode == VisualizationMode.Combined;
            m_ActiveIso = IsIsosurfaceModeActive();
            if (m_SliceRenderer == null)
            {
                m_ActiveSlices = false;
                m_EnabledSliceMask = 0;
                return;
            }

            m_EnabledSliceMask = 0;
            if (m_SliceRenderer.ShowSliceXY) m_EnabledSliceMask |= 1;
            if (m_SliceRenderer.ShowSliceXZ) m_EnabledSliceMask |= 2;
            if (m_SliceRenderer.ShowSliceYZ) m_EnabledSliceMask |= 4;
            m_ActiveSlices = (m_Mode == VisualizationMode.Slice || m_Mode == VisualizationMode.Combined)
                             && m_EnabledSliceMask != 0;
        }

        private void OnSlicePositionChanged(SliceAxis axis, float position)
        {
            m_RenderDirty = true;
        }

        private bool CanRender()
        {
            return m_Interpolator != null
                   && m_ColorRamp != null
                   && m_VolumeBounds != null
                   && m_Interpolator.CurrentTexture != null;
        }

        private void AutoWireReferences()
        {
            if (m_VolumeBounds == null) m_VolumeBounds = GetComponentInChildren<TemperatureVolumeBounds>();
            if (m_SensorManager == null) m_SensorManager = GetComponentInChildren<TemperatureSensorManager>();
            if (m_Interpolator == null) m_Interpolator = GetComponentInChildren<TemperatureFieldInterpolator>();
            if (m_ColorRamp == null) m_ColorRamp = GetComponentInChildren<TemperatureColorRamp>();
            if (m_SliceRenderer == null) m_SliceRenderer = GetComponentInChildren<TemperatureSliceRenderer>();
            if (m_VolumeRenderer == null) m_VolumeRenderer = GetComponentInChildren<TemperatureVolumeRenderer>();
            if (m_IsosurfaceRenderer == null) m_IsosurfaceRenderer = GetComponentInChildren<TemperatureIsosurfaceRenderer>();
        }

        private void WireDependencies()
        {
            if (m_SensorManager != null && m_VolumeBounds != null)
            {
                m_SensorManager.VolumeBounds = m_VolumeBounds;
                m_SensorManager.Initialize(m_VolumeBounds);
            }

            if (m_Interpolator != null)
            {
                m_Interpolator.Configure(m_SensorManager, m_VolumeBounds);
            }
        }
    }
}
