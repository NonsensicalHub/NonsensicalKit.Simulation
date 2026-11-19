using CineShot.Core;
using UnityEngine;

namespace CineShot.Output
{
    public sealed class AdapterCameraOutput : ICameraOutput
    {
        const int k_PlayingPriority = 100;

        readonly ILensWriter m_Writer;
        readonly int m_IdlePriority;

        public AdapterCameraOutput(ILensWriter writer)
        {
            m_Writer = writer;
            m_IdlePriority = writer.Priority;
        }

        public void Apply(in CameraPose pose)
        {
            if (!m_Writer.IsValid)
                return;
            m_Writer.SetPose(pose.Position, pose.Rotation);
            var lens = new LensDTO
            {
                FieldOfView = pose.Fov,
                NearClipPlane = pose.NearClip,
                FarClipPlane = pose.FarClip,
                Dutch = pose.Dutch
            };
            m_Writer.SetLens(in lens);
        }

        public void SetControlling(bool controlling)
        {
            m_Writer.SetEnabled(controlling);
            m_Writer.Priority = controlling ? k_PlayingPriority : m_IdlePriority;
        }

        public static ICameraOutput CreateFromComponent(Component vcam)
        {
            if (vcam == null)
                return NullCameraOutput.Instance;

#if CM_V3_OR_NEWER
            var cm3 = vcam as Unity.Cinemachine.CinemachineCamera;
            if (cm3 != null)
                return new AdapterCameraOutput(new CM3LensWriter(cm3));
#elif CM_2_8_OR_NEWER
            var cm2 = vcam as Cinemachine.CinemachineVirtualCamera;
            if (cm2 != null)
                return new AdapterCameraOutput(new CM2LensWriter(cm2));
#endif
            return NullCameraOutput.Instance;
        }
    }
}
