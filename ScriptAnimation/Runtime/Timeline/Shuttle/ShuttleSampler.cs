using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 穿梭车取/放货：车体冻结。伸出方向只决定往左还是往右；左右夹爪与左右夹紧分别开关。
    /// 取货：夹紧(松→夹) → 侧伸 → 拨爪(开→锁) → 收回。
    /// 放货：侧伸 → 拨爪(锁→开) → 收回 → 松开夹紧(夹→松)。
    /// 显隐切换叠在拨爪旋转时间内，无额外停顿。
    /// </summary>
    public static class ShuttleSampler
    {
        private struct PhaseTimes
        {
            public float Extend;
            public float Clamp;
            public float Paddle;
            public float Retract;

            public float Total => Extend + Clamp + Paddle + Retract;
        }

        public static float EstimateDuration(
            ShuttleAnim anim,
            ShuttleClipData data,
            float startClaw,
            float startClamp,
            float startPaddle)
        {
            if (anim == null || data == null)
                return -1f;

            _ = startPaddle;
            return BuildPhases(anim, data, startClaw, startClamp).Total;
        }

        public static void Sample(
            ShuttleAnim anim,
            ShuttleClipData data,
            Vector3 homePos,
            Quaternion homeRot,
            float startClaw,
            float startClamp,
            float startPaddle,
            float normalizedTime)
        {
            if (anim == null || data == null)
                return;

            PhaseTimes phases = BuildPhases(anim, data, startClaw, startClamp);
            float total = Mathf.Max(0.01f, phases.Total);
            float elapsed = Mathf.Clamp01(normalizedTime) * total;

            float clawIn = data.ClawRetracted;
            float clawOut = data.SignedClawExtended;
            float clampOpen = data.ClampReleased;
            float clampClosed = data.ClampClosed;
            float paddleOpen = anim.PaddleOpenAngle;
            float paddleLocked = anim.PaddleLockedAngle;
            bool pickUp = data.Mode == ForkliftMode.PickUp;
            float paddleFrom = pickUp ? paddleOpen : paddleLocked;
            float paddleTo = pickUp ? paddleLocked : paddleOpen;
            // 取货：松→夹；放货：夹→松
            float clampFrom = pickUp ? clampOpen : clampClosed;
            float clampTo = pickUp ? clampClosed : clampOpen;

            Transform body = anim.Body;
            body.position = homePos;
            body.rotation = homeRot;

            float claw = startClaw;
            float clamp = startClamp;
            float paddle = startPaddle;

            if (pickUp)
            {
                // 0 clamp in（取货先夹紧再侧伸）
                if (phases.Clamp > 0f && elapsed <= phases.Clamp)
                {
                    float u = elapsed / phases.Clamp;
                    clamp = Mathf.Lerp(clampFrom, clampTo, Mathf.Clamp01(u));
                    Apply(anim, data, startClaw, clamp, startPaddle);
                    return;
                }

                float cursor = phases.Clamp;
                clamp = phases.Clamp > 0f ? clampTo : startClamp;

                // 1 extend（保持夹紧）
                if (elapsed <= cursor + phases.Extend)
                {
                    float u = phases.Extend > 1e-5f ? (elapsed - cursor) / phases.Extend : 1f;
                    claw = Mathf.Lerp(startClaw, clawOut, Mathf.Clamp01(u));
                    Apply(anim, data, claw, clamp, startPaddle);
                    return;
                }

                cursor += phases.Extend;
                claw = clawOut;

                // 2 拨爪锁定
                if (phases.Paddle > 0f && elapsed <= cursor + phases.Paddle)
                {
                    float u = (elapsed - cursor) / phases.Paddle;
                    paddle = Mathf.LerpAngle(paddleFrom, paddleTo, Mathf.Clamp01(u));
                    Apply(anim, data, claw, clamp, paddle);
                    return;
                }

                cursor += phases.Paddle;
                paddle = phases.Paddle > 0f ? paddleTo : startPaddle;

                // 3 retract（保持夹紧）
                float uRetract = phases.Retract > 1e-5f
                    ? (elapsed - cursor) / phases.Retract
                    : 1f;
                claw = Mathf.Lerp(clawOut, clawIn, Mathf.Clamp01(uRetract));
                Apply(anim, data, claw, clamp, paddle);
            }
            else
            {
                // 放货收回前保持夹持，避免货物掉落
                float holdClamp = phases.Clamp > 0f ? clampFrom : startClamp;

                // 0 伸出
                if (elapsed <= phases.Extend)
                {
                    float u = phases.Extend > 1e-5f ? elapsed / phases.Extend : 1f;
                    claw = Mathf.Lerp(startClaw, clawOut, Mathf.Clamp01(u));
                    Apply(anim, data, claw, holdClamp, startPaddle);
                    return;
                }

                float cursor = phases.Extend;
                claw = clawOut;

                // 1 拨爪解锁
                if (phases.Paddle > 0f && elapsed <= cursor + phases.Paddle)
                {
                    float u = (elapsed - cursor) / phases.Paddle;
                    paddle = Mathf.LerpAngle(paddleFrom, paddleTo, Mathf.Clamp01(u));
                    Apply(anim, data, claw, holdClamp, paddle);
                    return;
                }

                cursor += phases.Paddle;
                paddle = phases.Paddle > 0f ? paddleTo : startPaddle;

                // 2 retract（仍保持夹持）
                if (elapsed <= cursor + phases.Retract)
                {
                    float u = phases.Retract > 1e-5f
                        ? (elapsed - cursor) / phases.Retract
                        : 1f;
                    claw = Mathf.Lerp(clawOut, clawIn, Mathf.Clamp01(u));
                    Apply(anim, data, claw, holdClamp, paddle);
                    return;
                }

                cursor += phases.Retract;
                claw = clawIn;

                // 3 clamp out（收回后再松开）
                if (phases.Clamp > 0f && elapsed <= cursor + phases.Clamp)
                {
                    float u = (elapsed - cursor) / phases.Clamp;
                    clamp = Mathf.Lerp(clampFrom, clampTo, Mathf.Clamp01(u));
                    Apply(anim, data, claw, clamp, paddle);
                    return;
                }

                clamp = phases.Clamp > 0f ? clampTo : startClamp;
                Apply(anim, data, claw, clamp, paddle);
            }
        }

        private static void Apply(
            ShuttleAnim anim,
            ShuttleClipData data,
            float claw,
            float clamp,
            float paddle)
        {
            float clawIn = data.ClawRetracted;
            float released = data.ClampReleased;
            float leftClaw = data.UsesClawLeft(anim) ? claw : clawIn;
            float rightClaw = data.UsesClawRight(anim) ? claw : clawIn;
            float leftClamp = data.UsesClampLeft(anim) ? clamp : released;
            float rightClamp = data.UsesClampRight(anim) ? clamp : released;
            anim.SetClawExtend(leftClaw, rightClaw);
            anim.SetClampOffset(leftClamp, rightClamp);
            anim.SetPaddleAngle(paddle);
        }

        private static PhaseTimes BuildPhases(
            ShuttleAnim anim,
            ShuttleClipData data,
            float startClaw,
            float startClamp)
        {
            float clawSpeed = DurationUtility.SafeSpeed(data.ClawSpeed);
            float clampSpeed = DurationUtility.SafeSpeed(data.ClampSpeed);
            float paddleSpeed = DurationUtility.SafeSpeed(data.PaddleSpeed);

            float clawIn = data.ClawRetracted;
            float clawOut = data.SignedClawExtended;
            float clampOpen = data.ClampReleased;
            float clampClosed = data.ClampClosed;
            float paddleTravel = Mathf.Abs(Mathf.DeltaAngle(anim.PaddleOpenAngle, anim.PaddleLockedAngle));
            bool hasPaddle = anim.HasAnyPaddle;
            bool hasClaw = data.UsesAnyClaw(anim);
            bool hasClamp = data.UsesAnyClamp(anim);
            _ = startClamp;

            return new PhaseTimes
            {
                Extend = hasClaw
                    ? DurationUtility.TimeForDistance(Mathf.Abs(clawOut - startClaw), clawSpeed)
                    : 0f,
                Clamp = hasClamp
                    ? DurationUtility.TimeForDistance(Mathf.Abs(clampClosed - clampOpen), clampSpeed)
                    : 0f,
                Paddle = hasPaddle
                    ? DurationUtility.TimeForAngle(paddleTravel, paddleSpeed)
                    : 0f,
                Retract = hasClaw
                    ? DurationUtility.TimeForDistance(Mathf.Abs(clawOut - clawIn), clawSpeed)
                    : 0f
            };
        }
    }
}
