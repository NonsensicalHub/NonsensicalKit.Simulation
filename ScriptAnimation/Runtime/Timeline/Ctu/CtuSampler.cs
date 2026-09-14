using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// CTU 可放货：车体冻结；机构层级为车→举升→旋转→夹爪→拨爪。
    /// 流程：升降到指定高度 → 旋转对准 → 侧伸 → 拨爪动作 → 收回 → 旋转回正 →（可选）恢复默认高度。
    /// 取货拨爪：开→锁（放拨爪）；放货拨爪：锁→开（抬起拨爪）。显隐切换叠在拨爪旋转时间内，无额外停顿。
    /// </summary>
    public static class CtuSampler
    {
        private struct PhaseTimes
        {
            public float PreLift;
            public float RotateIn;
            public float Extend;
            public float Paddle;
            public float Retract;
            public float RotateOut;
            public float PostLift;

            public float Total =>
                PreLift + RotateIn + Extend + Paddle + Retract + RotateOut + PostLift;
        }

        public static float EstimateDuration(
            CtuAnim anim,
            CtuClipData data,
            Transform station,
            Vector3 homePos,
            Quaternion homeRot,
            float startRotate,
            float startLift,
            float startClaw,
            float startPaddle)
        {
            if (anim == null || data == null || station == null)
                return -1f;

            _ = startPaddle; // 时长按开↔锁满行程估算；开场角仅影响采样插值
            return BuildPhases(
                anim, data, station, homePos, homeRot,
                startRotate, startLift, startClaw).Total;
        }

        public static void Sample(
            CtuAnim anim,
            CtuClipData data,
            Transform station,
            Vector3 homePos,
            Quaternion homeRot,
            float startRotate,
            float startLift,
            float startClaw,
            float startPaddle,
            float normalizedTime)
        {
            if (anim == null || data == null || station == null)
                return;

            PhaseTimes phases = BuildPhases(
                anim, data, station, homePos, homeRot,
                startRotate, startLift, startClaw);
            float total = Mathf.Max(0.01f, phases.Total);
            float elapsed = Mathf.Clamp01(normalizedTime) * total;

            Vector3 stationPos = station.position + data.DestinationOffset;
            float faceAngle = anim.ResolveRotateAngleTo(stationPos, homePos, homeRot);
            float travelAngle = anim.RotateTravelAngle;
            float endAngle = data.ReturnRotateToTravel ? travelAngle : faceAngle;

            float placeH = data.LiftPlaceHeight;
            float travelH = data.LiftTravelHeight;
            float endLift = data.ReturnLiftToTravel ? travelH : placeH;
            float clawIn = data.ClawRetracted;
            float clawOut = data.ClawExtended;
            float paddleOpen = anim.PaddleOpenAngle;
            float paddleLocked = anim.PaddleLockedAngle;
            bool pickUp = data.Mode == ForkliftMode.PickUp;
            // 取货：开→锁；放货：锁→开
            float paddleFrom = pickUp ? paddleOpen : paddleLocked;
            float paddleTo = pickUp ? paddleLocked : paddleOpen;

            Transform body = anim.Body;
            body.position = homePos;
            body.rotation = homeRot;

            float cursor = 0f;
            float paddle = startPaddle;

            // 0 pre lift → 指定取放高度（同时收到收回位）
            if (elapsed <= cursor + phases.PreLift)
            {
                float u = phases.PreLift > 1e-5f ? (elapsed - cursor) / phases.PreLift : 1f;
                u = Mathf.Clamp01(u);
                anim.SetRotateAngle(startRotate);
                anim.SetLiftHeight(Mathf.Lerp(startLift, placeH, u));
                anim.SetClawExtend(Mathf.Lerp(startClaw, clawIn, u));
                anim.SetPaddleAngle(startPaddle);
                return;
            }

            cursor += phases.PreLift;
            anim.SetLiftHeight(placeH);
            anim.SetClawExtend(clawIn);
            anim.SetPaddleAngle(startPaddle);

            // 1 转入货位朝向
            if (elapsed <= cursor + phases.RotateIn)
            {
                float u = phases.RotateIn > 1e-5f ? (elapsed - cursor) / phases.RotateIn : 1f;
                anim.SetRotateAngle(Mathf.LerpAngle(startRotate, faceAngle, Mathf.Clamp01(u)));
                anim.SetLiftHeight(placeH);
                anim.SetClawExtend(clawIn);
                anim.SetPaddleAngle(startPaddle);
                return;
            }

            cursor += phases.RotateIn;
            anim.SetRotateAngle(faceAngle);

            // 2 伸出夹爪
            if (elapsed <= cursor + phases.Extend)
            {
                float u = phases.Extend > 1e-5f ? (elapsed - cursor) / phases.Extend : 1f;
                anim.SetClawExtend(Mathf.Lerp(clawIn, clawOut, Mathf.Clamp01(u)));
                anim.SetPaddleAngle(startPaddle);
                return;
            }

            cursor += phases.Extend;
            anim.SetClawExtend(clawOut);

            // 3 paddle（取货放拨爪 / 放货抬起拨爪；显隐叠在此时间内）
            if (phases.Paddle > 0f && elapsed <= cursor + phases.Paddle)
            {
                float u = phases.Paddle > 1e-5f ? (elapsed - cursor) / phases.Paddle : 1f;
                paddle = Mathf.LerpAngle(paddleFrom, paddleTo, Mathf.Clamp01(u));
                anim.SetPaddleAngle(paddle);
                return;
            }

            cursor += phases.Paddle;
            paddle = phases.Paddle > 0f ? paddleTo : startPaddle;
            anim.SetPaddleAngle(paddle);

            // 4 收回夹爪
            if (elapsed <= cursor + phases.Retract)
            {
                float u = phases.Retract > 1e-5f ? (elapsed - cursor) / phases.Retract : 1f;
                anim.SetClawExtend(Mathf.Lerp(clawOut, clawIn, Mathf.Clamp01(u)));
                anim.SetPaddleAngle(paddle);
                return;
            }

            cursor += phases.Retract;
            anim.SetClawExtend(clawIn);

            // 5 转回行驶朝向
            if (elapsed <= cursor + phases.RotateOut)
            {
                float u = phases.RotateOut > 1e-5f ? (elapsed - cursor) / phases.RotateOut : 1f;
                anim.SetRotateAngle(Mathf.LerpAngle(faceAngle, endAngle, Mathf.Clamp01(u)));
                anim.SetLiftHeight(placeH);
                anim.SetPaddleAngle(paddleTo);
                return;
            }

            cursor += phases.RotateOut;
            anim.SetRotateAngle(endAngle);
            anim.SetPaddleAngle(paddleTo);

            // 6 post lift → 默认高度（可选）
            {
                float u = phases.PostLift > 1e-5f ? (elapsed - cursor) / phases.PostLift : 1f;
                anim.SetLiftHeight(Mathf.Lerp(placeH, endLift, Mathf.Clamp01(u)));
                anim.SetPaddleAngle(paddleTo);
            }
        }

        private static PhaseTimes BuildPhases(
            CtuAnim anim,
            CtuClipData data,
            Transform station,
            Vector3 homePos,
            Quaternion homeRot,
            float startRotate,
            float startLift,
            float startClaw)
        {
            float rotateSpeed = DurationUtility.SafeSpeed(data.RotateSpeed);
            float liftSpeed = DurationUtility.SafeSpeed(data.LiftSpeed);
            float clawSpeed = DurationUtility.SafeSpeed(data.ClawSpeed);
            float paddleSpeed = DurationUtility.SafeSpeed(data.PaddleSpeed);

            Vector3 stationPos = station.position + data.DestinationOffset;
            float faceAngle = anim.ResolveRotateAngleTo(stationPos, homePos, homeRot);
            float endAngle = data.ReturnRotateToTravel ? anim.RotateTravelAngle : faceAngle;

            float placeH = data.LiftPlaceHeight;
            float travelH = data.LiftTravelHeight;
            float endLift = data.ReturnLiftToTravel ? travelH : placeH;
            float clawIn = data.ClawRetracted;
            float clawOut = data.ClawExtended;
            float paddleOpen = anim.PaddleOpenAngle;
            float paddleLocked = anim.PaddleLockedAngle;
            float paddleTravel = Mathf.Abs(Mathf.DeltaAngle(paddleOpen, paddleLocked));
            bool hasPaddle = anim.PaddleA != null || anim.PaddleB != null;

            return new PhaseTimes
            {
                PreLift = Mathf.Max(
                    DurationUtility.TimeForDistance(Mathf.Abs(placeH - startLift), liftSpeed),
                    DurationUtility.TimeForDistance(Mathf.Abs(clawIn - startClaw), clawSpeed)),
                RotateIn = DurationUtility.TimeForAngle(
                    Mathf.Abs(Mathf.DeltaAngle(startRotate, faceAngle)), rotateSpeed),
                Extend = DurationUtility.TimeForDistance(Mathf.Abs(clawOut - clawIn), clawSpeed),
                Paddle = hasPaddle
                    ? DurationUtility.TimeForAngle(paddleTravel, paddleSpeed)
                    : 0f,
                Retract = DurationUtility.TimeForDistance(Mathf.Abs(clawOut - clawIn), clawSpeed),
                RotateOut = DurationUtility.TimeForAngle(
                    Mathf.Abs(Mathf.DeltaAngle(faceAngle, endAngle)), rotateSpeed),
                PostLift = data.ReturnLiftToTravel
                    ? DurationUtility.TimeForDistance(Mathf.Abs(endLift - placeH), liftSpeed)
                    : 0f
            };
        }
    }
}
