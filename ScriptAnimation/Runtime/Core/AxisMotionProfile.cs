using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 单轴梯形速度曲线：先加速、匀速（可选）、再减速。
    /// 路程过短达不到峰值速度时退化为三角曲线。
    /// </summary>
    public static class AxisMotionProfile
    {
        public readonly struct Plan
        {
            public readonly float Distance;
            public readonly float MaxSpeed;
            public readonly float Accel;
            public readonly float Decel;
            public readonly float PeakSpeed;
            public readonly float AccelTime;
            public readonly float CruiseTime;
            public readonly float DecelTime;
            public readonly float Duration;

            public Plan(
                float distance,
                float maxSpeed,
                float accel,
                float decel,
                float peakSpeed,
                float accelTime,
                float cruiseTime,
                float decelTime)
            {
                Distance = distance;
                MaxSpeed = maxSpeed;
                Accel = accel;
                Decel = decel;
                PeakSpeed = peakSpeed;
                AccelTime = accelTime;
                CruiseTime = cruiseTime;
                DecelTime = decelTime;
                Duration = accelTime + cruiseTime + decelTime;
            }

            public bool IsStationary => Distance < 1e-6f || Duration < 1e-6f;
        }

        public static Plan Build(float distance, float maxSpeed, float acceleration)
        {
            float d = Mathf.Max(0f, distance);
            float v = DurationUtility.SafeSpeed(maxSpeed);
            float a = Mathf.Max(0.01f, acceleration);

            if (d < 1e-6f)
                return new Plan(0f, v, a, a, 0f, 0f, 0f, 0f);

            // 对称加减速：达峰所需最短路程 = v²/a
            float minDistForPeak = v * v / a;
            if (d >= minDistForPeak - 1e-8f)
            {
                float ta = v / a;
                float da = 0.5f * a * ta * ta;
                float cruise = Mathf.Max(0f, d - 2f * da) / v;
                return new Plan(d, v, a, a, v, ta, cruise, ta);
            }

            // 三角：peak = sqrt(a*d)，加减速各一半路程
            float peak = Mathf.Sqrt(a * d);
            float tTri = peak / a;
            return new Plan(d, v, a, a, peak, tTri, 0f, tTri);
        }

        /// <summary>在计划时刻 t 已走过的路程（米）。</summary>
        public static float DistanceAt(in Plan plan, float time)
        {
            if (plan.IsStationary)
                return plan.Distance;

            float t = Mathf.Clamp(time, 0f, plan.Duration);
            float a = plan.Accel;
            float peak = plan.PeakSpeed;

            if (t <= plan.AccelTime)
                return 0.5f * a * t * t;

            float sAccel = 0.5f * a * plan.AccelTime * plan.AccelTime;
            if (t <= plan.AccelTime + plan.CruiseTime)
                return sAccel + peak * (t - plan.AccelTime);

            float sCruise = peak * plan.CruiseTime;
            float td = t - plan.AccelTime - plan.CruiseTime;
            // s = s0 + peak*td - 0.5*a*td²
            return sAccel + sCruise + peak * td - 0.5f * a * td * td;
        }

        /// <summary>归一化进度 [0,1]。</summary>
        public static float ProgressAt(in Plan plan, float time)
        {
            if (plan.IsStationary)
                return 1f;
            return Mathf.Clamp01(DistanceAt(plan, time) / plan.Distance);
        }
    }
}
