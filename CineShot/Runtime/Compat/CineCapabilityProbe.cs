using System;
using UnityEngine;

namespace CineShot.Compat
{
    public static class CineCapabilityProbe
    {
        static CineCapabilities s_Current;
        static bool s_Ran;

        public static CineCapabilities Current
        {
            get
            {
                if (!s_Ran)
                    RunOnce();
                return s_Current;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void RuntimeInit()
        {
            RunOnce();
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        static void EditorInit()
        {
            UnityEditor.EditorApplication.delayCall += RunOnce;
        }
#endif

        public static void RunOnce()
        {
            try
            {
                s_Current = Probe();
                s_Ran = true;
                EmitSummary(s_Current);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Cine 兼容层] 能力探针失败，已降级为保守能力位: " + ex.Message);
                s_Current = new CineCapabilities(false, false, false, 0, 0, 0);
                s_Ran = true;
            }
        }

        static CineCapabilities Probe()
        {
#if CM_V3_OR_NEWER
            bool cm = true;
            bool v3 = true;
            int major = 3, minor = 1, patch = 0;
#elif CM_2_8_OR_NEWER
            bool cm = true;
            bool v3 = false;
            int major = 2, minor = 8, patch = 0;
#else
            bool cm = false;
            bool v3 = false;
            int major = 0, minor = 0, patch = 0;
#endif
            return new CineCapabilities(cm, v3, DofChannelProbe.HasWritableDofChannel(), major, minor, patch);
        }

        static void EmitSummary(in CineCapabilities c)
        {
            if (!c.CinemachinePresent)
            {
                WarnOnce.Log("cm.missing", "[Cine 兼容层] 未检测到 Cinemachine，运镜输出将走空实现。");
                return;
            }

            if (!c.CinemachineV3)
            {
                WarnOnce.Log("cm.v2",
                    "[Cine 兼容层] 检测到 Cinemachine 2.8，输出走 CM2 适配器，运镜与预览不受影响。");
            }

            if (!c.UrpVolumeDof)
            {
                WarnOnce.Log("dof.null",
                    "[Cine 兼容层] 当前渲染管线无可写 URP DepthOfField 通道，焦点子系统走 Null。");
            }
        }
    }
}
