using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    public static class DurationUtility
    {
        /// <summary>估算/相位数值地板默认值；为 <see cref="ScriptAnimTrackBase"/> 轨级最将 Clip 时长默认一致。</summary>
        public const float DefaultMinClipDuration = 0.01f;

        /// <summary>瞬间动作（Teleport / Duration=0 的交接姿态等）Timeline 占位帧数默认值。</summary>
        public const int DefaultInstantHoldFrames = 50;

        public static float SafeSpeed(float speed) => Mathf.Max(DefaultMinClipDuration, speed);

        public static float TimeForDistance(float distance, float speed)
        {
            return Mathf.Max(DefaultMinClipDuration, Mathf.Max(0f, distance) / SafeSpeed(speed));
        }

        public static float TimeForAngle(float degrees, float degreesPerSecond)
        {
            float abs = Mathf.Abs(degrees);
            if (abs <= 1f)
                return 0f;
            return Mathf.Max(DefaultMinClipDuration, abs / SafeSpeed(degreesPerSecond));
        }

        /// <summary>按帧数换算时长（默认 60fps，与 Timeline 工程默认帧率一致）。</summary>
        public static float TimeForFrames(int frames, float frameRate = 60f)
        {
            if (frames <= 0)
                return 0f;
            return frames / Mathf.Max(1f, frameRate);
        }
    }
}
