using System;
using System.Collections.Generic;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// ScriptAnimTrack 写入的 Transform 通道（与 GatherProperties / 结束恢复共用）。
    /// </summary>
    [Flags]
    public enum ScriptAnimPoseChannels
    {
        None = 0,
        LocalPosition = 1 << 0,
        LocalRotation = 1 << 1,
        LocalScale = 1 << 2,
    }

    /// <summary>
    /// 收集并快照 / 恢复 ScriptAnim 轨控制的位姿，供预览属性收集与 PostPlayback Revert 使用。
    /// </summary>
    public static class ScriptAnimControlledPose
    {
        public readonly struct Entry
        {
            public readonly Transform Transform;
            public readonly ScriptAnimPoseChannels Channels;

            public Entry(Transform transform, ScriptAnimPoseChannels channels)
            {
                Transform = transform;
                Channels = channels;
            }
        }

        public struct Snapshot
        {
            Vector3[] m_localPositions;
            Quaternion[] m_localRotations;
            Vector3[] m_localScales;
            ScriptAnimPoseChannels[] m_channels;
            Transform[] m_transforms;
            int m_count;

            public static Snapshot Capture(ScriptAnimActor actor)
            {
                var list = new List<Entry>(8);
                Collect(actor, list);
                var snap = new Snapshot
                {
                    m_count = list.Count,
                    m_transforms = new Transform[list.Count],
                    m_channels = new ScriptAnimPoseChannels[list.Count],
                    m_localPositions = new Vector3[list.Count],
                    m_localRotations = new Quaternion[list.Count],
                    m_localScales = new Vector3[list.Count],
                };

                for (int i = 0; i < list.Count; i++)
                {
                    Entry e = list[i];
                    Transform tr = e.Transform;
                    snap.m_transforms[i] = tr;
                    snap.m_channels[i] = e.Channels;
                    if (tr == null)
                        continue;
                    if ((e.Channels & ScriptAnimPoseChannels.LocalPosition) != 0)
                        snap.m_localPositions[i] = tr.localPosition;
                    if ((e.Channels & ScriptAnimPoseChannels.LocalRotation) != 0)
                        snap.m_localRotations[i] = tr.localRotation;
                    if ((e.Channels & ScriptAnimPoseChannels.LocalScale) != 0)
                        snap.m_localScales[i] = tr.localScale;
                }

                return snap;
            }

            public void Restore()
            {
                for (int i = 0; i < m_count; i++)
                {
                    Transform tr = m_transforms[i];
                    if (tr == null)
                        continue;

                    ScriptAnimPoseChannels ch = m_channels[i];
                    if ((ch & ScriptAnimPoseChannels.LocalPosition) != 0)
                        tr.localPosition = m_localPositions[i];
                    if ((ch & ScriptAnimPoseChannels.LocalRotation) != 0)
                        tr.localRotation = m_localRotations[i];
                    if ((ch & ScriptAnimPoseChannels.LocalScale) != 0)
                        tr.localScale = m_localScales[i];
                }
            }
        }

        public static void Collect(ScriptAnimActor binding, List<Entry> into)
        {
            if (binding == null || into == null)
                return;

            // 锁定轨仅还原 Target 旋转（与时 WorldRotationLock Mixer 一致）
            if (binding is WorldRotationLockAnim lockOnly)
            {
                if (lockOnly.Target != null)
                {
                    into.Add(new Entry(
                        lockOnly.Target,
                        ScriptAnimPoseChannels.LocalRotation));
                }

                return;
            }

            into.Add(new Entry(
                binding.transform,
                ScriptAnimPoseChannels.LocalPosition | ScriptAnimPoseChannels.LocalRotation));

            if (binding is ForkliftAnim forklift && forklift.Fork != null)
            {
                into.Add(new Entry(
                    forklift.Fork,
                    ScriptAnimPoseChannels.LocalPosition | ScriptAnimPoseChannels.LocalRotation));
            }

            if (binding is LatentAgvAnim latent && latent.Platform != null)
            {
                into.Add(new Entry(
                    latent.Platform,
                    ScriptAnimPoseChannels.LocalPosition | ScriptAnimPoseChannels.LocalRotation));
            }

            // 若同物体上也挂了 Lock（可选），一并登记 Target，便于预览还原
            var worldLock = binding.GetComponent<WorldRotationLockAnim>();
            if (worldLock != null &&
                worldLock.Target != null &&
                !(binding is ForkliftAnim fa && fa.Fork == worldLock.Target) &&
                !(binding is LatentAgvAnim la && la.Platform == worldLock.Target))
            {
                into.Add(new Entry(
                    worldLock.Target,
                    ScriptAnimPoseChannels.LocalRotation));
            }

            if (binding is CtuAnim ctu)
            {
                if (ctu.Lift != null)
                    into.Add(new Entry(ctu.Lift, ScriptAnimPoseChannels.LocalPosition));
                if (ctu.Rotate != null)
                    into.Add(new Entry(ctu.Rotate, ScriptAnimPoseChannels.LocalRotation));
                if (ctu.Claw != null)
                    into.Add(new Entry(ctu.Claw, ScriptAnimPoseChannels.LocalPosition));
                if (ctu.PaddleA != null)
                    into.Add(new Entry(ctu.PaddleA, ScriptAnimPoseChannels.LocalRotation));
                if (ctu.PaddleB != null)
                    into.Add(new Entry(ctu.PaddleB, ScriptAnimPoseChannels.LocalRotation));
            }

            if (binding is ShuttleAnim shuttle)
            {
                if (shuttle.ClawLeft != null)
                    into.Add(new Entry(shuttle.ClawLeft, ScriptAnimPoseChannels.LocalPosition));
                if (shuttle.ClawRight != null)
                    into.Add(new Entry(shuttle.ClawRight, ScriptAnimPoseChannels.LocalPosition));
                if (shuttle.ClampLeft != null)
                    into.Add(new Entry(shuttle.ClampLeft, ScriptAnimPoseChannels.LocalPosition));
                if (shuttle.ClampRight != null)
                    into.Add(new Entry(shuttle.ClampRight, ScriptAnimPoseChannels.LocalPosition));
                ShuttleAnim.PaddleSettings[] paddles = shuttle.Paddles;
                if (paddles != null)
                {
                    for (int i = 0; i < paddles.Length; i++)
                    {
                        Transform paddle = paddles[i] != null ? paddles[i].Transform : null;
                        if (paddle != null)
                            into.Add(new Entry(paddle, ScriptAnimPoseChannels.LocalRotation));
                    }
                }
            }

            if (binding is StackerAnim stacker)
            {
                if (stacker.TravelAxis != null && stacker.TravelAxis != stacker.transform)
                    into.Add(new Entry(stacker.TravelAxis, ScriptAnimPoseChannels.LocalPosition));
                if (stacker.LiftAxis != null &&
                    stacker.LiftAxis != stacker.transform &&
                    stacker.LiftAxis != stacker.TravelAxis)
                    into.Add(new Entry(stacker.LiftAxis, ScriptAnimPoseChannels.LocalPosition));
                if (stacker.Fork != null)
                    into.Add(new Entry(stacker.Fork, ScriptAnimPoseChannels.LocalPosition));
                if (stacker.SecondaryFork != null)
                    into.Add(new Entry(stacker.SecondaryFork, ScriptAnimPoseChannels.LocalPosition));
            }

            if (binding is RobotArmAnim robotArm && robotArm.Joints != null)
            {
                for (int i = 0; i < robotArm.Joints.Length; i++)
                {
                    Transform joint = robotArm.GetJoint(i);
                    if (joint != null)
                        into.Add(new Entry(joint, ScriptAnimPoseChannels.LocalRotation));
                }
            }

            if (binding is RobotArm5Anim robotArm5 && robotArm5.Joints != null)
            {
                for (int i = 0; i < robotArm5.Joints.Length; i++)
                {
                    Transform joint = robotArm5.GetJoint(i);
                    if (joint != null)
                        into.Add(new Entry(joint, ScriptAnimPoseChannels.LocalRotation));
                }
            }

            if (binding is SwingFlipAnim swingFlip)
            {
                Transform target = swingFlip.Target;
                if (target != null && target != swingFlip.transform)
                    into.Add(new Entry(target, ScriptAnimPoseChannels.LocalRotation));
            }

            if (binding is SequentialPositionAnim sequential)
            {
                Transform control = sequential.ControlTarget;
                if (control != null && control != sequential.transform)
                    into.Add(new Entry(control, ScriptAnimPoseChannels.LocalPosition));
            }

            if (binding is PoseChangeAnim poseChange)
            {
                ScriptAnimPoseChannels channels = poseChange.GetControlledChannels();
                Transform control = poseChange.ControlTarget;
                if (control != null && control != poseChange.transform)
                {
                    // 根物体已加 Position|Rotation；ControlTarget 按开关写入
                    if (channels != ScriptAnimPoseChannels.None)
                        into.Add(new Entry(control, channels));
                }
                else
                {
                    // 覆盖根条目：仅登记实际控制的通道
                    into[0] = new Entry(binding.transform, channels);
                }
            }

            if (binding is PoseChangeAnimMax poseChangeMax)
            {
                ScriptAnimPoseChannels channels = poseChangeMax.GetControlledChannels();
                Transform[] targets = poseChangeMax.GetControlTransforms();
                bool anyExternal = false;
                for (int i = 0; i < targets.Length; i++)
                {
                    Transform control = targets[i];
                    if (control == null || control == poseChangeMax.transform)
                        continue;

                    anyExternal = true;
                    if (channels != ScriptAnimPoseChannels.None)
                        into.Add(new Entry(control, channels));
                }

                if (!anyExternal)
                    into[0] = new Entry(binding.transform, channels);
            }

            if (binding is OpenBoxAnim openBox)
            {
                openBox.EnsurePivotsReady();
                for (int i = 0; i < openBox.PivotCount; i++)
                {
                    Transform pivot = openBox.GetPivot(i);
                    if (pivot != null && pivot != openBox.transform)
                        into.Add(new Entry(pivot, ScriptAnimPoseChannels.LocalRotation));
                }
            }
        }
    }
}
