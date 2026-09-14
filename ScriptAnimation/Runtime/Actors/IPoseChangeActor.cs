using System;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 姿态改变组件公共接口，供 <see cref="PoseChangeAnim"/> 与 Timeline 共用；多目标见 <see cref="PoseChangeAnimMax"/>。
    /// </summary>
    public interface IPoseChangeActor
    {
        Transform ControlTarget { get; }
        Transform[] GetControlTransforms();

        bool ControlPosition { get; }
        bool ControlRotation { get; }
        bool ControlScale { get; }

        PoseSpace Space { get; }
        int PoseCount { get; }
        bool HasDefaultPose { get; }
        PoseDefinition DefaultPose { get; }

        PoseDefinition GetPose(int index);
        int FindPoseIndex(string poseName);
        bool TryGetPose(string poseName, out PoseDefinition pose);
        string[] GetPoseNames();

        void CaptureCurrentInto(PoseDefinition pose);
        PoseDefinition CaptureCurrentAsNew(string poseName);
        void ApplyPose(in PoseChangeSampler.PoseTRS pose);

        ScriptAnimPoseChannels GetControlledChannels();
        PoseChangeSampler.PoseTRS CaptureCurrent();
        PoseChangeSampler.PoseTRS ResolveDefaultStartPose();

        void CaptureDefaultPoseFromCurrent();
        void AddPoseFromCurrent();
    }
}
