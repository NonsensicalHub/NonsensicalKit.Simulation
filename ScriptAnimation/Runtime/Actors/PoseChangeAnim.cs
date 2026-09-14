using System;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>姿态坐标空间。</summary>
    public enum PoseSpace
    {
        [InspectorName("本地")]
        Local = 0,

        [InspectorName("世界")]
        World = 1
    }

    /// <summary>命名姿态：位置、旋转（欧拉角）、缩放。</summary>
    [Serializable]
    public class PoseDefinition
    {
        [Tooltip("Timeline 下拉框中显示的名称，须唯一")]
        [InspectorLabel("名称")]
        public string Name = "Pose";

        [InspectorLabel("位置")]
        public Vector3 Position;

        [InspectorLabel("旋转")]
        public Vector3 EulerAngles;

        [InspectorLabel("缩放")]
        public Vector3 Scale = Vector3.one;

        public Quaternion Rotation => Quaternion.Euler(EulerAngles);
    }

    /// <summary>
    /// 姿态改变：在组件上配置任意数量命名姿态，由 <see cref="PoseChangeClip"/> 在 Clip 时长内插值到位。
    /// DurationSeconds 为 0 时瞬间切换。
    /// </summary>
    [AddComponentMenu("ScriptAnimation/姿态改变 (PoseChangeAnim)")]
    public class PoseChangeAnim : ScriptAnimActor, IPoseChangeActor
    {
        [Header("控制对象")]
        [Tooltip("被写入位姿的对象；为空则使用自身。")]
        [InspectorLabel("控制对象")]
        [SerializeField] private Transform m_controlTarget;

        [Header("详细控制")]
        [Tooltip("勾选后对控制对象写入位置")]
        [InspectorLabel("控制位置")]
        [SerializeField] private bool m_controlPosition = true;

        [Tooltip("勾选后对控制对象写入旋转")]
        [InspectorLabel("控制旋转")]
        [SerializeField] private bool m_controlRotation = true;

        [Tooltip("勾选后对控制对象写入缩放（localScale）")]
        [InspectorLabel("控制大小")]
        [SerializeField] private bool m_controlScale = true;

        [Header("空间")]
        [Tooltip("位置/旋转按本地或世界写入；缩放始终为 localScale。")]
        [InspectorLabel("姿态空间")]
        [SerializeField] private PoseSpace m_space = PoseSpace.Local;

        [Header("姿态")]
        [Tooltip("可配置任意数量命名姿态，供 Timeline Clip 下拉选择")]
        [InspectorLabel("姿态列表")]
        [SerializeField] private PoseDefinition[] m_poses = Array.Empty<PoseDefinition>();

        [Header("默认状态（无前序 Clip 时的开场姿态）")]
        [Tooltip("同轨无前序 PoseChangeClip 时，插值起点使用此姿态，保证 scrub / 跳播结果确定。")]
        [InspectorLabel("默认姿态")]
        [SerializeField] private PoseDefinition m_defaultPose = new PoseDefinition { Name = "Default", Scale = Vector3.one };

        [InspectorLabel("已设置默认姿态")]
        [SerializeField] private bool m_hasDefaultPose;

        /// <summary>控制对象（未指定时为自身）。</summary>
        public Transform ControlTarget => m_controlTarget != null ? m_controlTarget : transform;

        public Transform[] GetControlTransforms()
        {
            Transform control = ControlTarget;
            return control != null ? new[] { control } : Array.Empty<Transform>();
        }

        public bool ControlPosition => m_controlPosition;
        public bool ControlRotation => m_controlRotation;
        public bool ControlScale => m_controlScale;

        public PoseSpace Space => m_space;
        public PoseDefinition[] Poses => m_poses;
        public bool HasDefaultPose => m_hasDefaultPose;
        public PoseDefinition DefaultPose => m_defaultPose;

        public int PoseCount => m_poses != null ? m_poses.Length : 0;

        public PoseDefinition GetPose(int index)
        {
            if (m_poses == null || index < 0 || index >= m_poses.Length)
                return null;
            return m_poses[index];
        }

        public int FindPoseIndex(string poseName)
        {
            if (m_poses == null || string.IsNullOrEmpty(poseName))
                return -1;

            for (int i = 0; i < m_poses.Length; i++)
            {
                PoseDefinition pose = m_poses[i];
                if (pose != null && pose.Name == poseName)
                    return i;
            }

            return -1;
        }

        public bool TryGetPose(string poseName, out PoseDefinition pose)
        {
            int index = FindPoseIndex(poseName);
            if (index < 0)
            {
                pose = null;
                return false;
            }

            pose = m_poses[index];
            return pose != null;
        }

        /// <summary>收集非空姿态名（供 Clip 下拉框）。</summary>
        public string[] GetPoseNames()
        {
            if (m_poses == null || m_poses.Length == 0)
                return Array.Empty<string>();

            int count = 0;
            for (int i = 0; i < m_poses.Length; i++)
            {
                if (m_poses[i] != null && !string.IsNullOrEmpty(m_poses[i].Name))
                    count++;
            }

            var names = new string[count];
            int w = 0;
            for (int i = 0; i < m_poses.Length; i++)
            {
                PoseDefinition pose = m_poses[i];
                if (pose == null || string.IsNullOrEmpty(pose.Name))
                    continue;
                names[w++] = pose.Name;
            }

            return names;
        }

        public void CaptureCurrentInto(PoseDefinition pose)
        {
            if (pose == null)
                return;

            Transform control = ControlTarget;
            if (control == null)
                return;

            if (m_space == PoseSpace.World)
            {
                pose.Position = control.position;
                pose.EulerAngles = control.eulerAngles;
            }
            else
            {
                pose.Position = control.localPosition;
                pose.EulerAngles = control.localEulerAngles;
            }

            pose.Scale = control.localScale;
        }

        public PoseDefinition CaptureCurrentAsNew(string poseName)
        {
            var pose = new PoseDefinition
            {
                Name = string.IsNullOrEmpty(poseName) ? $"Pose {PoseCount + 1}" : poseName
            };
            CaptureCurrentInto(pose);
            return pose;
        }

        public void ApplyPose(in PoseChangeSampler.PoseTRS pose)
        {
            Transform control = ControlTarget;
            if (control == null)
                return;

            if (m_space == PoseSpace.World)
            {
                if (m_controlPosition && m_controlRotation)
                    control.SetPositionAndRotation(pose.Position, pose.Rotation);
                else if (m_controlPosition)
                    control.position = pose.Position;
                else if (m_controlRotation)
                    control.rotation = pose.Rotation;
            }
            else
            {
                if (m_controlPosition)
                    control.localPosition = pose.Position;
                if (m_controlRotation)
                    control.localRotation = pose.Rotation;
            }

            if (m_controlScale)
                control.localScale = pose.Scale;
        }

        /// <summary>当前启用的 Timeline / 预览位姿通道。</summary>
        public ScriptAnimPoseChannels GetControlledChannels()
        {
            ScriptAnimPoseChannels channels = ScriptAnimPoseChannels.None;
            if (m_controlPosition)
                channels |= ScriptAnimPoseChannels.LocalPosition;
            if (m_controlRotation)
                channels |= ScriptAnimPoseChannels.LocalRotation;
            if (m_controlScale)
                channels |= ScriptAnimPoseChannels.LocalScale;
            return channels;
        }

        public PoseChangeSampler.PoseTRS CaptureCurrent()
            => PoseChangeSampler.CaptureCurrent(this);

        /// <summary>无前序时的开场姿态；未配置默认姿态时回退为零位姿（确定性，不读当前 Transform）。</summary>
        public PoseChangeSampler.PoseTRS ResolveDefaultStartPose()
        {
            if (m_hasDefaultPose && m_defaultPose != null)
                return PoseChangeSampler.PoseTRS.FromDefinition(m_defaultPose);

            return new PoseChangeSampler.PoseTRS
            {
                Position = Vector3.zero,
                Rotation = Quaternion.identity,
                Scale = Vector3.one
            };
        }

        [ContextMenu("添加姿态（捕获当前）")]
        public void CaptureDefaultPoseFromCurrent()
        {
            if (m_defaultPose == null)
                m_defaultPose = new PoseDefinition { Name = "Default", Scale = Vector3.one };
            CaptureCurrentInto(m_defaultPose);
            if (string.IsNullOrEmpty(m_defaultPose.Name))
                m_defaultPose.Name = "Default";
            m_hasDefaultPose = true;
        }

        [ContextMenu("添加姿态（捕获当前）")]
        public void AddPoseFromCurrent()
        {
            var extra = CaptureCurrentAsNew(null);
            int count = PoseCount;
            var next = new PoseDefinition[count + 1];
            if (m_poses != null && count > 0)
                Array.Copy(m_poses, next, count);
            next[count] = extra;
            m_poses = next;
        }
    }
}
