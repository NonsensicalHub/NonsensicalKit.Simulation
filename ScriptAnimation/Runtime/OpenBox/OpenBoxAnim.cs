using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 纸箱开合：五个阶段（侧壁 / 底短 / 底长 / 顶短 / 顶长）可独立写入。
    /// 绑定到 <see cref="ScriptAnimTrackBase"/>，由 <see cref="OpenBoxClip"/> 驱动。
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("ScriptAnimation/开箱 (OpenBoxAnim)")]
    public class OpenBoxAnim : ScriptAnimActor
    {
        [Header("折叠：侧壁0=压扁 1=成型；顶/底盖 0=竖直 1=闭合 ±1=外翻/内合 90° ±2=再转 90°")]
        [Range(0f, 1f)]
        [SerializeField, InspectorLabel("侧壁")]
        float m_wallFold;

        [Tooltip("1=闭合，2=闭合后再往里折90°，0=竖直，-1=外翻放平，-2=外翻放平后再转90°")]
        [Range(OpenBoxPose.FlapFoldMin, OpenBoxPose.FlapFoldMax)]
        [SerializeField, InspectorLabel("底盖短边")]
        float m_bottomShortFold;

        [Tooltip("1=闭合，2=闭合后再往里折90°，0=竖直，-1=外翻放平，-2=外翻放平后再转90°")]
        [Range(OpenBoxPose.FlapFoldMin, OpenBoxPose.FlapFoldMax)]
        [SerializeField, InspectorLabel("底盖长边")]
        float m_bottomLongFold;

        [Tooltip("1=闭合，2=闭合后再往里折90°，0=竖直，-1=外翻放平，-2=外翻放平后再转90°")]
        [Range(OpenBoxPose.FlapFoldMin, OpenBoxPose.FlapFoldMax)]
        [SerializeField, InspectorLabel("顶盖短边")]
        float m_topShortFold;

        [Tooltip("1=闭合，2=闭合后再往里折90°，0=竖直，-1=外翻放平，-2=外翻放平后再转90°")]
        [Range(OpenBoxPose.FlapFoldMin, OpenBoxPose.FlapFoldMax)]
        [SerializeField, InspectorLabel("顶盖长边")]
        float m_topLongFold;

        [Header("编辑预览")]
        [SerializeField, InspectorLabel("编辑模式预览")]
        bool m_previewInEditMode = true;

        [Header("折痕绑定")]
        [SerializeField, InspectorLabel("折痕绑定")]
        CartonFoldBinding[] m_bindings = Array.Empty<CartonFoldBinding>();

        [Header("默认状态（无前序 Clip 时的开场姿态）")]
        [Tooltip("同轨无前序 OpenBoxClip 时，采样起点使用此姿态，保证 scrub / 跳播结果确定。")]
        [SerializeField, InspectorLabel("默认姿态")]
        OpenBoxPose m_defaultPose;

        [SerializeField, InspectorLabel("已设置默认姿态")]
        bool m_hasDefaultPose;

        Transform[] m_pivots;
        Vector3[] m_axes;
        float[] m_flat;
        float[] m_closed;
        CartonFoldChannel[] m_channels;
        int m_count;
        float m_lastWall = float.NaN, m_lastBottomShort = float.NaN, m_lastBottomLong = float.NaN;
        float m_lastTopShort = float.NaN, m_lastTopLong = float.NaN;

        OpenBoxPose m_rest;
        bool m_hasRest;
        OpenBoxPose m_sampledPose;
        bool m_hasSampledPose;
        bool m_timelineDriven;

        public float WallFold
        {
            get => m_wallFold;
            set => SetStage(OpenBoxStage.Wall, value);
        }

        public float BottomShortFold
        {
            get => m_bottomShortFold;
            set => SetStage(OpenBoxStage.BottomShort, value);
        }

        public float BottomLongFold
        {
            get => m_bottomLongFold;
            set => SetStage(OpenBoxStage.BottomLong, value);
        }

        public float TopShortFold
        {
            get => m_topShortFold;
            set => SetStage(OpenBoxStage.TopShort, value);
        }

        public float TopLongFold
        {
            get => m_topLongFold;
            set => SetStage(OpenBoxStage.TopLong, value);
        }

        public int BindingCount => m_count > 0 ? m_count : (m_bindings != null ? m_bindings.Length : 0);
        public int PivotCount => m_count;
        public bool HasRestPose => m_hasRest;
        public OpenBoxPose RestPose => m_rest;
        public bool HasDefaultPose => m_hasDefaultPose;
        public OpenBoxPose DefaultPose => m_defaultPose;
        public bool TimelineDriven => m_timelineDriven;

        public void BeginTimelineDrive()
        {
            m_timelineDriven = true;
        }

        public void EndTimelineDrive()
        {
            m_timelineDriven = false;
            m_hasSampledPose = false;
        }

        public void ApplyTimelinePose(in OpenBoxPose pose)
        {
            m_timelineDriven = true;
            m_sampledPose = pose.Clamped();
            m_hasSampledPose = true;
            OnTimelinePoseApplied();
            ApplyFolds();
        }

        public void CommitSampledPose()
        {
            if (!m_hasSampledPose)
                return;
            OpenBoxPose pose = m_sampledPose;
            m_hasSampledPose = false;
            ApplyPoseInternal(pose);
        }

        protected virtual void OnTimelinePoseApplied() { }

        public Transform GetPivot(int index)
        {
            if (m_pivots == null || index < 0 || index >= m_count)
                return null;
            return m_pivots[index];
        }

        public OpenBoxPose CaptureCurrent()
        {
            return new OpenBoxPose
            {
                Wall = m_wallFold,
                BottomShort = m_bottomShortFold,
                BottomLong = m_bottomLongFold,
                TopShort = m_topShortFold,
                TopLong = m_topLongFold
            };
        }

        /// <summary>无前序时的开场姿态；未配置默认姿态时回退为 Flat（确定性，不读当前折叠值）。</summary>
        public OpenBoxPose ResolveDefaultStartPose()
            => m_hasDefaultPose ? m_defaultPose.Clamped() : OpenBoxPose.Flat;

        [ContextMenu("从当前姿态捕获默认状态")]
        public void CaptureDefaultPoseFromCurrent()
        {
            m_defaultPose = CaptureCurrent().Clamped();
            m_hasDefaultPose = true;
        }

        public void CaptureRestIfNeeded()
        {
            if (m_hasRest)
                return;
            m_rest = CaptureCurrent();
            m_hasRest = true;
            OnRestCaptured();
        }

        public void RevertToRest()
        {
            if (!m_hasRest)
                return;
            m_hasSampledPose = false;
            OnRestReverting();
            ApplyPoseInternal(m_rest);
            m_hasRest = false;
        }

        public void ClearRest()
        {
            m_hasRest = false;
            OnRestCleared();
        }

        protected virtual void OnRestCaptured() { }
        protected virtual void OnRestReverting() { }
        protected virtual void OnRestCleared() { }

        public void SetBindings(CartonFoldBinding[] bindings)
        {
            m_bindings = bindings ?? Array.Empty<CartonFoldBinding>();
            RebuildCache(forceApply: true);
        }

        public void EnsurePivotsReady()
        {
            if (m_count == 0)
                RebuildCache(forceApply: false);
        }

        public void SetStage(OpenBoxStage stage, float value)
        {
            OpenBoxPose pose = CaptureCurrent();
            pose.Set(stage, value);
            ApplyPose(pose);
        }

        public void SetPose(in OpenBoxPose pose)
        {
            ApplyPose(pose);
        }

        public virtual void ApplyPose(in OpenBoxPose pose)
        {
            ApplyPoseInternal(pose);
        }

        void ApplyPoseInternal(in OpenBoxPose pose)
        {
            OpenBoxPose clamped = pose.Clamped();
            m_wallFold = clamped.Wall;
            m_bottomShortFold = clamped.BottomShort;
            m_bottomLongFold = clamped.BottomLong;
            m_topShortFold = clamped.TopShort;
            m_topLongFold = clamped.TopLong;
            ApplyFolds();
        }

        public void ApplyFolds()
        {
            if (m_count == 0)
            {
                RebuildCache(forceApply: false);
                if (m_count == 0)
                    return;
            }

            OpenBoxPose pose = m_hasSampledPose ? m_sampledPose : CaptureCurrent();
            if (Approximately(m_lastWall, pose.Wall) &&
                Approximately(m_lastBottomShort, pose.BottomShort) &&
                Approximately(m_lastBottomLong, pose.BottomLong) &&
                Approximately(m_lastTopShort, pose.TopShort) &&
                Approximately(m_lastTopLong, pose.TopLong))
                return;

            m_lastWall = pose.Wall;
            m_lastBottomShort = pose.BottomShort;
            m_lastBottomLong = pose.BottomLong;
            m_lastTopShort = pose.TopShort;
            m_lastTopLong = pose.TopLong;

            float wall = pose.Wall;
            float bShort = pose.BottomShort;
            float bLong = pose.BottomLong;
            float tShort = pose.TopShort;
            float tLong = pose.TopLong;

            for (int i = 0; i < m_count; i++)
            {
                Transform pivot = m_pivots[i];
                if (pivot == null)
                    continue;

                float t = ChannelT(m_channels[i], wall, bShort, bLong, tShort, tLong);
                // fold=0→flat（竖直）（→closed（闭合），可外推到±2
                float angle = Mathf.LerpUnclamped(m_flat[i], m_closed[i], t);
                pivot.localRotation = Quaternion.AngleAxis(angle, m_axes[i]);
            }
        }

        protected virtual void OnEnable()
        {
            RebuildCache(forceApply: !m_timelineDriven);
            if (m_timelineDriven && m_hasSampledPose)
                ApplyFolds();
        }

#if UNITY_EDITOR
        protected virtual void OnValidate()
        {
            if (m_timelineDriven)
                return;
            if (!m_previewInEditMode && !Application.isPlaying)
                return;
            EditorApplication.delayCall -= DeferredApply;
            EditorApplication.delayCall += DeferredApply;
        }

        void DeferredApply()
        {
            EditorApplication.delayCall -= DeferredApply;
            if (this == null)
                return;
            if (m_timelineDriven)
                return;
            if (!m_previewInEditMode && !Application.isPlaying)
                return;
            OnValidatedApply();
            SceneView.RepaintAll();
        }

        protected virtual void OnValidatedApply()
        {
            RebuildCache(forceApply: true);
        }
#endif

        protected void RebuildCache(bool forceApply)
        {
            m_lastWall = m_lastBottomShort = m_lastBottomLong = m_lastTopShort = m_lastTopLong = float.NaN;

            if (m_bindings == null || m_bindings.Length == 0)
            {
                m_count = 0;
                return;
            }

            int n = m_bindings.Length;
            if (m_pivots == null || m_pivots.Length < n)
            {
                m_pivots = new Transform[n];
                m_axes = new Vector3[n];
                m_flat = new float[n];
                m_closed = new float[n];
                m_channels = new CartonFoldChannel[n];
            }

            int write = 0;
            for (int i = 0; i < n; i++)
            {
                CartonFoldBinding b = m_bindings[i];
                if (b == null || b.pivot == null)
                    continue;
                m_pivots[write] = b.pivot;
                m_axes[write] = b.localAxis.sqrMagnitude > 1e-8f ? b.localAxis.normalized : Vector3.up;
                m_flat[write] = b.flatAngle;
                m_closed[write] = b.closedAngle;
                m_channels[write] = b.channel;
                write++;
            }

            m_count = write;
            if (forceApply)
                ApplyFolds();
        }

        static float ChannelT(
            CartonFoldChannel channel,
            float wall, float bShort, float bLong, float tShort, float tLong)
        {
            switch (channel)
            {
                case CartonFoldChannel.WallAcute:
                case CartonFoldChannel.WallObtuse:
                    return wall;
                case CartonFoldChannel.BottomShort: return bShort;
                case CartonFoldChannel.BottomLong: return bLong;
                case CartonFoldChannel.TopShort: return tShort;
                case CartonFoldChannel.TopLong: return tLong;
                default: return 0f;
            }
        }

        static bool Approximately(float a, float b) =>
            !float.IsNaN(a) && !float.IsNaN(b) && Mathf.Abs(a - b) < 1e-5f;
    }
}
