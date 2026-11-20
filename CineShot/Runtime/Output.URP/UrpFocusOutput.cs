using CineShot.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CineShot.Output
{
    public sealed class UrpFocusOutput : IFocusOutput
    {
        Volume m_Volume;
        VolumeProfile m_Profile;
        DepthOfField m_Dof;
        bool m_Valid;

        public UrpFocusOutput(Transform host)
        {
            var go = new GameObject("CineShot_RuntimeDOF");
            go.hideFlags = HideFlags.HideAndDontSave;
            if (host != null)
                go.transform.SetParent(host, false);

            m_Volume = go.AddComponent<Volume>();
            m_Volume.isGlobal = true;
            m_Volume.priority = 1000f;

            m_Profile = ScriptableObject.CreateInstance<VolumeProfile>();
            m_Profile.hideFlags = HideFlags.HideAndDontSave;
            m_Volume.sharedProfile = m_Profile;

            m_Dof = m_Profile.Add<DepthOfField>(true);
            m_Dof.active = true;
            m_Dof.mode.overrideState = true;
            m_Dof.focusDistance.overrideState = true;
            m_Dof.gaussianStart.overrideState = true;
            m_Dof.gaussianEnd.overrideState = true;
            m_Dof.aperture.overrideState = true;
            m_Dof.focalLength.overrideState = true;
            m_Dof.bladeCount.overrideState = true;
            m_Valid = true;
        }

        public void Apply(in FocusState state, in FocusSettings settings)
        {
            if (!m_Valid || m_Dof == null)
                return;
            if (settings.dofMode == DofMode.Off)
            {
                m_Dof.active = false;
                return;
            }

            m_Dof.active = true;
            float distance = Mathf.Max(0.1f, state.Distance);
            bool bokeh = settings.dofMode == DofMode.Bokeh;
            m_Dof.mode.value = bokeh ? DepthOfFieldMode.Bokeh : DepthOfFieldMode.Gaussian;
            if (bokeh)
            {
                // 对焦距离跟目标；焦距 mm 是镜头虚化强度，不随目标变
                m_Dof.focusDistance.value = distance;
                m_Dof.aperture.value = settings.aperture;
                m_Dof.focalLength.value = settings.focalLength;
                m_Dof.bladeCount.value = settings.bladeCount;
            }
            else
            {
                // 高斯不用 focusDistance，用 Start/End；跟随目标时随对焦距离挪清晰带
                float span = Mathf.Max(0.5f, distance * Mathf.Clamp(settings.aperture / 5.6f, 0.25f, 2f) * 0.35f);
                m_Dof.gaussianStart.value = distance;
                m_Dof.gaussianEnd.value = distance + span;
            }
        }

        public void Dispose()
        {
            m_Valid = false;
            m_Dof = null;
            if (m_Profile != null)
            {
                Object.DestroyImmediate(m_Profile);
                m_Profile = null;
            }
            if (m_Volume != null)
            {
                var go = m_Volume.gameObject;
                m_Volume = null;
                Object.DestroyImmediate(go);
            }
        }
    }
}
