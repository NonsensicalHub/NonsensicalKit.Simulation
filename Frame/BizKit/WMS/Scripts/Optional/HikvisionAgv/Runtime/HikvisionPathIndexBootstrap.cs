using NonsensicalKit.ScriptAnimation;
using UnityEngine;

namespace HikvisionAgv
{
    static class HikvisionPathIndexBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Register()
        {
            PathFeatureIndexRegistry.Register<HikvisionPointIndex>();
        }
    }
}
