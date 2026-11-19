using CineShot.Compat;
using CineShot.Core;
using UnityEngine;

namespace CineShot.Output
{
    public static class UrpBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Register()
        {
            CineOutputFactory.CreateFocusOutput = host =>
                DofChannelProbe.HasWritableDofChannel() ? new UrpFocusOutput(host) : (IFocusOutput)NullFocusOutput.Instance;
        }
    }
}
