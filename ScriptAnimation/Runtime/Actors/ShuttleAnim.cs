using System;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    public enum ShuttleSide
    {
        [InspectorName("左")]
        Left = 0,
        [InspectorName("右")]
        Right = 1
    }

    /// <summary>
    /// 穿梭车脚本动画：车体沿路网移动（基类），取放货时车体不动。
    /// 左右夹爪为独立节点（不可共用父节点）。
    /// Clip「伸出方向」只决定沿夹爪伸出轴往左还是往右；左右夹爪由 Clip 开关分别启用。
    /// 左右夹紧各自开关；未启用的一侧保持松开。取货先夹紧再侧伸；放货收回后再松开。
    /// 拨爪可配置多组独立节点，取放货时同步转到同一逻辑角（各组可设自己的轴与符号）。
    /// </summary>
    [AddComponentMenu("ScriptAnimation/穿梭车 (ShuttleAnim)")]
    public class ShuttleAnim : PathMoveActor
    {
        /// <summary>单组独立拨爪：Transform + 本地旋转轴 + 相对逻辑角的符号。</summary>
        [Serializable]
        public class PaddleSettings
        {
            [Tooltip("拨爪旋转节点")]
            [InspectorLabel("拨爪")]
            public Transform Transform;

            [Tooltip("拨爪本地旋转轴")]
            [InspectorLabel("旋转轴")]
            public Vector3 AxisLocal = Vector3.forward;

            [Tooltip("相对逻辑角的符号：镜像一般为 -1，同向为 1")]
            [InspectorLabel("角度符号")]
            public float Sign = 1f;

            public Vector3 ResolvedAxis => AxisLocal.sqrMagnitude > 1e-6f
                ? AxisLocal.normalized
                : Vector3.forward;

            public float ResolvedSign => Mathf.Approximately(Sign, 0f) ? 1f : Sign;
        }

        [Header("1. 夹爪（左右独立侧伸）")]
        [Tooltip("左侧夹爪；与右侧不可挂在同一父节点下")]
        [InspectorLabel("左夹爪")]
        [SerializeField] private Transform m_clawLeft;

        [Tooltip("右侧夹爪；与左侧不可挂在同一父节点下")]
        [InspectorLabel("右夹爪")]
        [SerializeField] private Transform m_clawRight;

        [Tooltip("夹爪本地伸出轴。正向右，负向=左（Clip「伸出方向」）。左右夹爪共用此轴。")]
        [InspectorLabel("夹爪伸出轴")]
        [SerializeField] private Vector3 m_clawAxisLocal = Vector3.right;

        [Header("2. 夹紧（左右独立，横向内移）")]
        [Tooltip("左侧夹紧；沿夹紧轴横向内移")]
        [InspectorLabel("左夹紧")]
        [SerializeField] private Transform m_clampLeft;

        [Tooltip("右侧夹紧；沿夹紧轴横向内移")]
        [InspectorLabel("右夹紧")]
        [SerializeField] private Transform m_clampRight;

        [Tooltip("夹紧本地平移轴（各夹紧本地正向为向内）")]
        [InspectorLabel("夹紧轴（本地）")]
        [SerializeField] private Vector3 m_clampAxisLocal = Vector3.forward;

        [Header("3. 拨爪（可多组独立；挂在夹爪末端，固定货物时转 90°）")]
        [Tooltip("每组一条：Transform、旋转轴、角度符号。取放货时各组同步转到同一逻辑角。")]
        [InspectorLabel("拨爪列表")]
        [SerializeField] private PaddleSettings[] m_paddles;

        [Tooltip("松开/开位角（度），相对 CapturePaddleBase")]
        [InspectorLabel("拨爪开位角")]
        [SerializeField] private float m_paddleOpenAngle;

        [Tooltip("固定货物时的锁定角（度），相对开位通常为 ±90")]
        [InspectorLabel("拨爪锁定角")]
        [SerializeField] private float m_paddleLockedAngle = 90f;

        [Header("新增 Clip 默认值（写入 Clip，可再改）")]
        [InspectorLabel("夹爪速度")]
        [SerializeField] private float m_clawSpeed = 0.8f;
        [InspectorLabel("夹紧速度")]
        [SerializeField] private float m_clampSpeed = 0.4f;
        [InspectorLabel("拨爪转速")]
        [SerializeField] private float m_paddleSpeed = 180f;

        [InspectorLabel("伸出方向")]
        [SerializeField] private ShuttleSide m_defaultSide = ShuttleSide.Right;
        [InspectorLabel("默认左夹爪")]
        [SerializeField] private bool m_defaultClawLeft = true;
        [InspectorLabel("默认右夹爪")]
        [SerializeField] private bool m_defaultClawRight = true;
        [Tooltip("收回时的夹爪偏移（通常 0）")]
        [InspectorLabel("夹爪收回偏移")]
        [SerializeField] private float m_defaultClawRetracted;
        [Tooltip("侧伸距离（正值）")]
        [InspectorLabel("夹爪伸出距离")]
        [SerializeField] private float m_defaultClawExtended = 0.8f;
        [InspectorLabel("默认左夹紧")]
        [SerializeField] private bool m_defaultClampLeft = true;
        [InspectorLabel("默认右夹紧")]
        [SerializeField] private bool m_defaultClampRight = true;
        [Tooltip("松开时的夹紧偏移（通常 0）")]
        [InspectorLabel("夹紧松开偏移")]
        [SerializeField] private float m_defaultClampReleased;
        [Tooltip("夹持到位时的夹紧偏移")]
        [InspectorLabel("夹紧夹持偏移")]
        [SerializeField] private float m_defaultClampClosed = 0.08f;

        private Quaternion[] m_paddleBaseLocals = Array.Empty<Quaternion>();
        private bool m_paddleBaseCaptured;

        public Transform ClawLeft => m_clawLeft;
        public Transform ClawRight => m_clawRight;
        public Transform GetClaw(ShuttleSide side) =>
            side == ShuttleSide.Left ? m_clawLeft : m_clawRight;

        public Vector3 ClawAxisLocal => m_clawAxisLocal.sqrMagnitude > 1e-6f
            ? m_clawAxisLocal.normalized
            : Vector3.right;

        public Transform ClampLeft => m_clampLeft;
        public Transform ClampRight => m_clampRight;
        public Vector3 ClampAxisLocal => m_clampAxisLocal.sqrMagnitude > 1e-6f
            ? m_clampAxisLocal.normalized
            : Vector3.forward;

        public PaddleSettings[] Paddles => m_paddles;
        public bool HasAnyPaddle
        {
            get
            {
                if (m_paddles == null)
                    return false;
                for (int i = 0; i < m_paddles.Length; i++)
                {
                    if (m_paddles[i] != null && m_paddles[i].Transform != null)
                        return true;
                }

                return false;
            }
        }

        public float PaddleOpenAngle => m_paddleOpenAngle;
        public float PaddleLockedAngle => m_paddleLockedAngle;

        public float ClawSpeed => m_clawSpeed;
        public float ClampSpeed => m_clampSpeed;
        public float PaddleSpeed => m_paddleSpeed;

        /// <summary>将本组件默认值写入 Clip（新增 Clip 时调用）。</summary>
        public void ApplyClipDefaults(ShuttleClipData data)
        {
            if (data == null)
                return;

            data.Side = m_defaultSide;
            data.ClawLeft = m_defaultClawLeft;
            data.ClawRight = m_defaultClawRight;
            data.ClawRetracted = m_defaultClawRetracted;
            data.ClawExtended = m_defaultClawExtended;
            data.ClampLeft = m_defaultClampLeft;
            data.ClampRight = m_defaultClampRight;
            data.ClampReleased = m_defaultClampReleased;
            data.ClampClosed = m_defaultClampClosed;
            data.ClawSpeed = m_clawSpeed;
            data.ClampSpeed = m_clampSpeed;
            data.PaddleSpeed = m_paddleSpeed;
        }

        private void Awake()
        {
            CapturePaddleBaseIfNeeded();
        }

        public void SetClawExtend(ShuttleSide side, float extend)
        {
            SetAxisOffset(GetClaw(side), ClawAxisLocal, extend);
        }

        /// <summary>同时写入两侧夹爪伸出量（未绑定的一侧忽略）。</summary>
        public void SetClawExtend(float leftExtend, float rightExtend)
        {
            SetAxisOffset(m_clawLeft, ClawAxisLocal, leftExtend);
            SetAxisOffset(m_clawRight, ClawAxisLocal, rightExtend);
        }

        public float GetClawExtend(ShuttleSide side)
        {
            Transform claw = GetClaw(side);
            if (claw == null)
                return 0f;
            return Vector3.Dot(claw.localPosition, ClawAxisLocal);
        }

        /// <summary>
        /// 写入左右夹紧偏移（沿各夹紧本地 ClampAxisLocal，正向向内）。
    /// 未绑定的一侧忽略。
    /// </summary>
        public void SetClampOffset(float leftOffset, float rightOffset)
        {
            Vector3 axis = ClampAxisLocal;
            SetAxisOffset(m_clampLeft, axis, leftOffset);
            SetAxisOffset(m_clampRight, axis, rightOffset);
        }

        public float GetClampOffset(ShuttleSide side)
        {
            Transform clamp = side == ShuttleSide.Left ? m_clampLeft : m_clampRight;
            if (clamp == null)
                return 0f;
            return Vector3.Dot(clamp.localPosition, ClampAxisLocal);
        }

        /// <summary>记录各拨爪本地旋转为开位基准（应对准松开姿态后再调）。</summary>
        public void CapturePaddleBase()
        {
            if (m_paddles == null || m_paddles.Length == 0)
            {
                m_paddleBaseLocals = Array.Empty<Quaternion>();
                m_paddleBaseCaptured = false;
                return;
            }

            if (m_paddleBaseLocals == null || m_paddleBaseLocals.Length != m_paddles.Length)
                m_paddleBaseLocals = new Quaternion[m_paddles.Length];

            bool any = false;
            for (int i = 0; i < m_paddles.Length; i++)
            {
                PaddleSettings paddle = m_paddles[i];
                if (paddle?.Transform == null)
                {
                    m_paddleBaseLocals[i] = Quaternion.identity;
                    continue;
                }

                m_paddleBaseLocals[i] = paddle.Transform.localRotation;
                any = true;
            }

            m_paddleBaseCaptured = any;
        }

        /// <summary>
        /// 写入拨爪逻辑角（度，相对开位基准）。各组实际角 = degrees × 该组 Sign。
    /// </summary>
        public void SetPaddleAngle(float degrees)
        {
            CapturePaddleBaseIfNeeded();
            if (m_paddles == null)
                return;

            for (int i = 0; i < m_paddles.Length; i++)
            {
                PaddleSettings paddle = m_paddles[i];
                if (paddle?.Transform == null)
                    continue;

                Quaternion baseLocal = i < m_paddleBaseLocals.Length
                    ? m_paddleBaseLocals[i]
                    : Quaternion.identity;
                paddle.Transform.localRotation =
                    baseLocal * Quaternion.AngleAxis(degrees * paddle.ResolvedSign, paddle.ResolvedAxis);
            }
        }

        public float GetPaddleAngle()
        {
            int index = FindFirstPaddleIndex();
            if (index < 0)
                return m_paddleOpenAngle;

            CapturePaddleBaseIfNeeded();
            PaddleSettings paddle = m_paddles[index];
            Quaternion baseLocal = index < m_paddleBaseLocals.Length
                ? m_paddleBaseLocals[index]
                : Quaternion.identity;
            Quaternion delta = Quaternion.Inverse(baseLocal) * paddle.Transform.localRotation;
            delta.ToAngleAxis(out float angle, out Vector3 axis);
            if (axis.sqrMagnitude < 1e-8f)
                return 0f;
            if (angle > 180f)
                angle -= 360f;
            if (Vector3.Dot(axis.normalized, paddle.ResolvedAxis) < 0f)
                angle = -angle;

            float sign = paddle.ResolvedSign;
            if (Mathf.Abs(sign) > 1e-6f)
                angle /= sign;
            return angle;
        }

        private int FindFirstPaddleIndex()
        {
            if (m_paddles == null)
                return -1;
            for (int i = 0; i < m_paddles.Length; i++)
            {
                if (m_paddles[i] != null && m_paddles[i].Transform != null)
                    return i;
            }

            return -1;
        }

        private static void SetAxisOffset(Transform target, Vector3 axis, float offset)
        {
            if (target == null)
                return;
            Vector3 local = target.localPosition;
            local -= axis * Vector3.Dot(local, axis);
            local += axis * offset;
            target.localPosition = local;
        }

        private void CapturePaddleBaseIfNeeded()
        {
            if (m_paddleBaseCaptured || !HasAnyPaddle)
                return;
            CapturePaddleBase();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            m_clawSpeed = Mathf.Max(0.01f, m_clawSpeed);
            m_clampSpeed = Mathf.Max(0.01f, m_clampSpeed);
            m_paddleSpeed = Mathf.Max(0.01f, m_paddleSpeed);
            if (m_paddles == null)
                m_paddles = Array.Empty<PaddleSettings>();
            for (int i = 0; i < m_paddles.Length; i++)
            {
                if (m_paddles[i] == null)
                    continue;
                if (Mathf.Approximately(m_paddles[i].Sign, 0f))
                    m_paddles[i].Sign = 1f;
            }
        }
#endif
    }
}
