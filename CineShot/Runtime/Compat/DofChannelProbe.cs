using UnityEngine;
using UnityEngine.Rendering;

namespace CineShot.Compat
{
    public static class DofChannelProbe
    {
        public static bool HasWritableDofChannel()
        {
#if !CINE_URP_PRESENT
            return false;
#else
            var rp = GraphicsSettings.currentRenderPipeline;
            if (rp == null)
                return false;

            var typeName = rp.GetType().FullName;
            if (typeName == null || typeName.IndexOf("Universal") < 0)
                return false;

            var volumeType = System.Type.GetType("UnityEngine.Rendering.Volume, Unity.RenderPipelines.Core.Runtime");
            var profileType = System.Type.GetType("UnityEngine.Rendering.VolumeProfile, Unity.RenderPipelines.Core.Runtime");
            var dofType = System.Type.GetType("UnityEngine.Rendering.Universal.DepthOfField, Unity.RenderPipelines.Universal.Runtime");
            return volumeType != null && profileType != null && dofType != null;
#endif
        }
    }
}
