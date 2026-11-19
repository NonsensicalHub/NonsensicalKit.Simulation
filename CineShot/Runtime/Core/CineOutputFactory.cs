using System;
using UnityEngine;

namespace CineShot.Core
{
    public static class CineOutputFactory
    {
        public static Func<Component, ICameraOutput> CreateCameraOutput;
        public static Func<Transform, IFocusOutput> CreateFocusOutput;

        public static ICameraOutput GetCameraOutput(Component vcam)
        {
            if (CreateCameraOutput != null)
            {
                var output = CreateCameraOutput(vcam);
                if (output != null)
                    return output;
            }
            return NullCameraOutput.Instance;
        }

        public static IFocusOutput GetFocusOutput(Transform host)
        {
            if (CreateFocusOutput != null)
            {
                var output = CreateFocusOutput(host);
                if (output != null)
                    return output;
            }
            return NullFocusOutput.Instance;
        }
    }
}
