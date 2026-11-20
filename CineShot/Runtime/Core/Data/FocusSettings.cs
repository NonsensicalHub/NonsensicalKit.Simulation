using System;
using UnityEngine;

namespace CineShot.Core
{
    public enum DofMode
    {
        [InspectorName("高斯模糊")] Gaussian = 0,
        [InspectorName("散景 Bokeh")] Bokeh = 1,
        [InspectorName("关闭")] Off = 2
    }

    [Serializable]
    public struct FocusSettings
    {
        public DofMode dofMode;
        public float damping;
        public float maxFocusSpeed;
        public float pullDuration;
        public float aperture;
        public float focalLength;
        public int bladeCount;
        public float hyperfocalCircleOfConfusion;

        public static FocusSettings Default
        {
            get
            {
                return new FocusSettings
                {
                    dofMode = DofMode.Bokeh,
                    damping = 6f,
                    maxFocusSpeed = 20f,
                    pullDuration = 0.5f,
                    aperture = 5.6f,
                    focalLength = 50f,
                    bladeCount = 5,
                    hyperfocalCircleOfConfusion = 0.00003f
                };
            }
        }

        /// <summary>超焦距（米）：H = f² / (N·c) + f，f 为焦距（米），N 为光圈，c 为弥散圈（米）。</summary>
        public float HyperfocalDistance
        {
            get
            {
                float f = focalLength * 0.001f;
                float n = Mathf.Max(0.1f, aperture);
                float c = Mathf.Max(1e-6f, hyperfocalCircleOfConfusion);
                return f * f / (n * c) + f;
            }
        }

        public void Sanitize()
        {
            if (damping < 0.01f)
                damping = 6f;
            if (maxFocusSpeed < 0.01f)
                maxFocusSpeed = 20f;
            if (pullDuration < 0.01f)
                pullDuration = 0.5f;
            if (aperture < 0.1f)
                aperture = 5.6f;
            if (focalLength < 1f)
                focalLength = 50f;
            if (bladeCount < 3)
                bladeCount = 5;
            if (hyperfocalCircleOfConfusion <= 0f)
                hyperfocalCircleOfConfusion = 0.00003f;
        }
    }
}
