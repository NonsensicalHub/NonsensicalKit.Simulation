using System;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>多目标命名姿态：每个控制对象各有一份位姿数据。</summary>
    [Serializable]
    public class MultiTargetPoseDefinition
    {
        [Tooltip("Timeline 下拉框中显示的名称，须唯一")]
        [InspectorLabel("名称")]
        public string Name = "Pose";

        [Tooltip("与控制对象列表按索引一一对应")]
        [InspectorLabel("各目标位姿")]
        public PoseDefinition[] Targets = Array.Empty<PoseDefinition>();

        public void EnsureTargetCount(int count)
        {
            if (count <= 0)
                count = 1;

            if (Targets != null && Targets.Length == count)
                return;

            var next = new PoseDefinition[count];
            for (int i = 0; i < count; i++)
            {
                if (Targets != null && i < Targets.Length && Targets[i] != null)
                    next[i] = Targets[i];
                else
                    next[i] = new PoseDefinition { Scale = Vector3.one };
            }

            Targets = next;
        }

        public static PoseChangeSampler.PoseTRS[] ToPoseTRSArray(MultiTargetPoseDefinition definition)
        {
            if (definition?.Targets == null || definition.Targets.Length == 0)
                return Array.Empty<PoseChangeSampler.PoseTRS>();

            var poses = new PoseChangeSampler.PoseTRS[definition.Targets.Length];
            for (int i = 0; i < definition.Targets.Length; i++)
                poses[i] = PoseChangeSampler.PoseTRS.FromDefinition(definition.Targets[i]);
            return poses;
        }
    }

    /// <summary>
    /// 姿态改变（多控制对象）：每个命名姿态为各控制对象分别配置位姿，由 <see cref="PoseChangeClip"/> 在 Clip 时长内插值到位。
    /// DurationSeconds 为 0 时瞬间切换。
    /// </summary>
    [AddComponentMenu("ScriptAnimation/姿态改变-多目标 (PoseChangeAnimMax)")]
    public class PoseChangeAnimMax : ScriptAnimActor
    {
        [Header("控制对象")]
        [Tooltip("被写入位姿的对象列表；为空则使用自身。")]
        [InspectorLabel("控制对象")]
        [SerializeField] private Transform[] m_controlTargets = Array.Empty<Transform>();

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
        [Tooltip("可配置任意数量命名姿态，供 Timeline Clip 下拉选择；每项内按控制对象分别配置位姿；列表第一项同时作为无前序 Clip 时的开场姿态。")]
        [InspectorLabel("姿态列表")]
        [SerializeField] private MultiTargetPoseDefinition[] m_poses = Array.Empty<MultiTargetPoseDefinition>();

        public Transform[] ControlTargets => m_controlTargets;

        public bool ControlPosition => m_controlPosition;
        public bool ControlRotation => m_controlRotation;
        public bool ControlScale => m_controlScale;

        public PoseSpace Space => m_space;
        public MultiTargetPoseDefinition[] Poses => m_poses;

        public int PoseCount => m_poses != null ? m_poses.Length : 0;

        void OnValidate()
        {
            SyncAllPoseTargetCounts();
        }

        public Transform[] GetControlTransforms()
        {
            if (m_controlTargets == null || m_controlTargets.Length == 0)
                return new[] { transform };

            int count = 0;
            for (int i = 0; i < m_controlTargets.Length; i++)
            {
                if (m_controlTargets[i] != null)
                    count++;
            }

            if (count == 0)
                return new[] { transform };

            var resolved = new Transform[count];
            int w = 0;
            for (int i = 0; i < m_controlTargets.Length; i++)
            {
                Transform target = m_controlTargets[i];
                if (target == null)
                    continue;
                resolved[w++] = target;
            }

            return resolved;
        }

        public int ControlTargetCount => GetControlTransforms().Length;

        public void SyncAllPoseTargetCounts()
        {
            if (m_poses == null)
                return;

            int count = ControlTargetCount;
            for (int i = 0; i < m_poses.Length; i++)
            {
                if (m_poses[i] != null)
                    m_poses[i].EnsureTargetCount(count);
            }
        }

        public MultiTargetPoseDefinition GetPose(int index)
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
                MultiTargetPoseDefinition pose = m_poses[i];
                if (pose != null && pose.Name == poseName)
                    return i;
            }

            return -1;
        }

        public bool TryGetPose(string poseName, out MultiTargetPoseDefinition pose)
        {
            int index = FindPoseIndex(poseName);
            if (index < 0)
            {
                pose = null;
                return false;
            }

            pose = m_poses[index];
            if (pose != null)
                pose.EnsureTargetCount(ControlTargetCount);
            return pose != null;
        }

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
                MultiTargetPoseDefinition pose = m_poses[i];
                if (pose == null || string.IsNullOrEmpty(pose.Name))
                    continue;
                names[w++] = pose.Name;
            }

            return names;
        }

        public void CaptureCurrentInto(MultiTargetPoseDefinition pose)
        {
            if (pose == null)
                return;

            Transform[] targets = GetControlTransforms();
            pose.EnsureTargetCount(targets.Length);
            for (int i = 0; i < targets.Length; i++)
                CaptureTargetInto(pose.Targets[i], targets[i]);
        }

        void CaptureTargetInto(PoseDefinition pose, Transform control)
        {
            if (pose == null || control == null)
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

        public MultiTargetPoseDefinition CaptureCurrentAsNew(string poseName)
        {
            var pose = new MultiTargetPoseDefinition
            {
                Name = string.IsNullOrEmpty(poseName) ? $"Pose {PoseCount + 1}" : poseName
            };
            CaptureCurrentInto(pose);
            return pose;
        }

        public PoseChangeSampler.PoseTRS[] CaptureCurrentPoses()
        {
            Transform[] targets = GetControlTransforms();
            var poses = new PoseChangeSampler.PoseTRS[targets.Length];
            for (int i = 0; i < targets.Length; i++)
                poses[i] = CaptureTarget(targets[i]);
            return poses;
        }

        PoseChangeSampler.PoseTRS CaptureTarget(Transform control)
        {
            if (control == null)
            {
                return new PoseChangeSampler.PoseTRS
                {
                    Position = Vector3.zero,
                    Rotation = Quaternion.identity,
                    Scale = Vector3.one
                };
            }

            if (m_space == PoseSpace.World)
            {
                return new PoseChangeSampler.PoseTRS
                {
                    Position = control.position,
                    Rotation = control.rotation,
                    Scale = control.localScale
                };
            }

            return new PoseChangeSampler.PoseTRS
            {
                Position = control.localPosition,
                Rotation = control.localRotation,
                Scale = control.localScale
            };
        }

        public void ApplyPoses(PoseChangeSampler.PoseTRS[] poses)
        {
            if (poses == null)
                return;

            Transform[] targets = GetControlTransforms();
            int count = Mathf.Min(targets.Length, poses.Length);
            for (int i = 0; i < count; i++)
                ApplyPoseTo(targets[i], poses[i]);
        }

        void ApplyPoseTo(Transform control, in PoseChangeSampler.PoseTRS pose)
        {
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

        /// <summary>无前序时的开场姿态：取姿态列表第一项；列表为空时回退为零位姿（确定性，不读当前 Transform）。</summary>
        public PoseChangeSampler.PoseTRS[] ResolveDefaultStartPoses()
        {
            int count = ControlTargetCount;
            MultiTargetPoseDefinition first = GetPose(0);
            if (first != null)
            {
                first.EnsureTargetCount(count);
                return MultiTargetPoseDefinition.ToPoseTRSArray(first);
            }

            var poses = new PoseChangeSampler.PoseTRS[count];
            for (int i = 0; i < count; i++)
            {
                poses[i] = new PoseChangeSampler.PoseTRS
                {
                    Position = Vector3.zero,
                    Rotation = Quaternion.identity,
                    Scale = Vector3.one
                };
            }

            return poses;
        }

        [ContextMenu("添加姿态（捕获当前）")]
        public void AddPoseFromCurrent()
        {
            var extra = CaptureCurrentAsNew(null);
            int count = PoseCount;
            var next = new MultiTargetPoseDefinition[count + 1];
            if (m_poses != null && count > 0)
                Array.Copy(m_poses, next, count);
            next[count] = extra;
            m_poses = next;
        }
    }
}
