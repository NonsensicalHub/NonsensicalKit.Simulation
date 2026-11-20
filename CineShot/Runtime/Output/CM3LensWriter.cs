#if CM_V3_OR_NEWER
using Unity.Cinemachine;
using UnityEngine;

namespace CineShot.Output
{
    public sealed class CM3LensWriter : ILensWriter
    {
        readonly CinemachineCamera m_Vcam;

        public CM3LensWriter(CinemachineCamera vcam)
        {
            m_Vcam = vcam;
        }

        public bool IsValid { get { return m_Vcam != null; } }

        public int Priority
        {
            get { return m_Vcam != null ? m_Vcam.Priority.Value : 0; }
            set
            {
                if (m_Vcam == null) return;
                var p = m_Vcam.Priority;
                p.Value = value;
                m_Vcam.Priority = p;
            }
        }

        public void SetPose(Vector3 position, Quaternion rotation)
        {
            if (m_Vcam != null)
                m_Vcam.ForceCameraPosition(position, rotation);
        }

        public void SetLens(in LensDTO lens)
        {
            if (m_Vcam == null) return;
            LensSettings l = m_Vcam.Lens;
            l.FieldOfView = lens.FieldOfView;
            l.NearClipPlane = lens.NearClipPlane;
            l.FarClipPlane = lens.FarClipPlane;
            l.Dutch = lens.Dutch;
            m_Vcam.Lens = l;
        }

        public void SetEnabled(bool enabled)
        {
            if (m_Vcam != null)
                m_Vcam.enabled = enabled;
        }
    }
}
#endif
