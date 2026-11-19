using CineShot.Core;
using UnityEngine;

namespace CineShot.Output
{
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoad]
#endif
    public static class OutputBootstrap
    {
        static OutputBootstrap()
        {
            Register();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Register()
        {
            CineOutputFactory.CreateCameraOutput = AdapterCameraOutput.CreateFromComponent;
        }
    }
}
