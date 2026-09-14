using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// CTU 脚本动画：车体沿路网移动（基类），取放货时车体不动。
    /// 机构层级：车 → 举升 → 旋转 → 夹爪 → 拨爪（取货拨爪锁定、放货抬起拨爪）。
    /// 普通叉车请用 <see cref="ForkliftAnim"/>；潜伏车（举升 AGV）请用 <see cref="LatentAgvAnim"/>。
    /// 穿梭车（无举升旋转，带夹紧机构）请用 <see cref="ShuttleAnim"/>。
    /// </summary>
    [AddComponentMenu("ScriptAnimation/CTU (CtuAnim)")]
    public class CtuAnim : PathMoveActor
    {
        [Header("1. 举升（挂在车体下）")]
        [Tooltip("相对车体沿抬升轴运动的平台；旋转机构应挂在其下")]
        [InspectorLabel("举升平台")]
        [SerializeField] private Transform m_lift;

        [Tooltip("升降本地轴（通常 +Y）")]
        [InspectorLabel("升降轴（本地）")]
        [SerializeField] private Vector3 m_liftAxisLocal = Vector3.up;

        [Header("2. 旋转（挂在举升下）")]
        [Tooltip("相对举升平台绕竖直轴旋转的转台；夹爪应挂在其上")]
        [InspectorLabel("转台")]
        [SerializeField] private Transform m_rotate;

        [Tooltip("转台本地旋转轴（通常 +Y）")]
        [InspectorLabel("转台轴（本地）")]
        [SerializeField] private Vector3 m_rotateAxisLocal = Vector3.up;

        [Tooltip("行驶时转台角（度，绕 RotateAxisLocal）。0 表示对准车头；请在转台对准车头时 CaptureRotateBase。")]
        [InspectorLabel("行驶转台角")]
        [SerializeField] private float m_rotateTravelAngle;

        [Header("3. 夹爪（挂在旋转下，侧伸）")]
        [Tooltip("侧向伸出的夹爪 / 货叉；拨爪挂在其末端")]
        [InspectorLabel("夹爪")]
        [SerializeField] private Transform m_claw;

        [Tooltip("夹爪本地伸出轴（侧向，多为 ±X 或 ±Z）")]
        [InspectorLabel("夹爪伸出轴")]
        [SerializeField] private Vector3 m_clawAxisLocal = Vector3.right;

        [Header("4. 拨爪（挂在夹爪末端，固定货物时转 90°）")]
        [Tooltip("拨爪 A")]
        [InspectorLabel("拨爪 A")]
        [SerializeField] private Transform m_paddleA;

        [Tooltip("拨爪 B（通常与 A 镜像）")]
        [InspectorLabel("拨爪 B")]
        [SerializeField] private Transform m_paddleB;

        [Tooltip("拨爪本地旋转轴")]
        [InspectorLabel("拨爪旋转轴")]
        [SerializeField] private Vector3 m_paddleAxisLocal = Vector3.forward;

        [Tooltip("松开/开位角（度），相对 CapturePaddleBase")]
        [InspectorLabel("拨爪开位角")]
        [SerializeField] private float m_paddleOpenAngle;

        [Tooltip("固定货物时的锁定角（度），相对开位通常为 ±90")]
        [InspectorLabel("拨爪锁定角")]
        [SerializeField] private float m_paddleLockedAngle = 90f;

        [Tooltip("拨爪 B 相对 A 的符号：镜像一般为 -1")]
        [InspectorLabel("拨爪 B 符号")]
        [SerializeField] private float m_paddleBSign = -1f;

        [Header("新增 Clip 默认值（写入 Clip，可再改）")]
        // 不可叫 m_rotateSpeed：基类 PathMoveActor 已有同名字段（车体转速），
        // Unity 不支持同名字段在基类与派生类中重复序列化。
        [InspectorLabel("转台转速")]
        [SerializeField] private float m_turretRotateSpeed = 90f;
        [InspectorLabel("升降速度")]
        [SerializeField] private float m_liftSpeed = 0.8f;
        [InspectorLabel("夹爪速度")]
        [SerializeField] private float m_clawSpeed = 0.8f;
        [InspectorLabel("拨爪转速")]
        [SerializeField] private float m_paddleSpeed = 180f;

        [Tooltip("行驶/待机时的升降高度")]
        [InspectorLabel("行驶升降高度")]
        [SerializeField] private float m_defaultLiftTravelHeight = 0.15f;
        [Tooltip("取放时的目标升降高度")]
        [InspectorLabel("取放升降高度")]
        [SerializeField] private float m_defaultLiftPlaceHeight;
        [Tooltip("收回时的夹爪偏移（通常 0）")]
        [InspectorLabel("夹爪收回偏移")]
        [SerializeField] private float m_defaultClawRetracted;
        [Tooltip("侧伸到位时的夹爪偏移")]
        [InspectorLabel("夹爪伸出偏移")]
        [SerializeField] private float m_defaultClawExtended = 0.8f;
        [Tooltip("取放结束后是否转回行驶角")]
        [InspectorLabel("结束后回行驶角")]
        [SerializeField] private bool m_defaultReturnRotateToTravel = true;
        [Tooltip("取放结束后是否恢复到行驶升降高度")]
        [InspectorLabel("结束后回行驶高度")]
        [SerializeField] private bool m_defaultReturnLiftToTravel = true;

        // TODO: 存放点（车体后方多货位缓存，用于提高运输效率）
        // 取货后可先收到某个存放点再去下一站；放货时可从存放点取出再侧伸放置。
        // 尚未实现，仅预留注释。
        // [SerializeField] private Transform[] m_storageSlots;

        private Quaternion m_rotateBaseLocal = Quaternion.identity;
        private bool m_rotateBaseCaptured;
        private Quaternion m_paddleABaseLocal = Quaternion.identity;
        private Quaternion m_paddleBBaseLocal = Quaternion.identity;
        private bool m_paddleBaseCaptured;

        public Transform Lift => m_lift;
        public Vector3 LiftAxisLocal => m_liftAxisLocal.sqrMagnitude > 1e-6f
            ? m_liftAxisLocal.normalized
            : Vector3.up;

        public Transform Rotate => m_rotate;
        public Vector3 RotateAxisLocal => m_rotateAxisLocal.sqrMagnitude > 1e-6f
            ? m_rotateAxisLocal.normalized
            : Vector3.up;
        public float RotateTravelAngle => m_rotateTravelAngle;

        public Transform Claw => m_claw;
        public Vector3 ClawAxisLocal => m_clawAxisLocal.sqrMagnitude > 1e-6f
            ? m_clawAxisLocal.normalized
            : Vector3.right;

        public Transform PaddleA => m_paddleA;
        public Transform PaddleB => m_paddleB;
        public Vector3 PaddleAxisLocal => m_paddleAxisLocal.sqrMagnitude > 1e-6f
            ? m_paddleAxisLocal.normalized
            : Vector3.forward;
        public float PaddleOpenAngle => m_paddleOpenAngle;
        public float PaddleLockedAngle => m_paddleLockedAngle;
        public float PaddleBSign => Mathf.Approximately(m_paddleBSign, 0f) ? -1f : m_paddleBSign;

        /// <summary>转台转速（度/秒）。车体旋转速度见基类 <see cref="PathMoveActor.RotateSpeed"/>。</summary>
        public float TurretRotateSpeed => m_turretRotateSpeed;

        public float LiftSpeed => m_liftSpeed;
        public float ClawSpeed => m_clawSpeed;
        public float PaddleSpeed => m_paddleSpeed;

        public float DefaultLiftTravelHeight => m_defaultLiftTravelHeight;
        public float DefaultLiftPlaceHeight => m_defaultLiftPlaceHeight;
        public float DefaultClawRetracted => m_defaultClawRetracted;
        public float DefaultClawExtended => m_defaultClawExtended;
        public bool DefaultReturnRotateToTravel => m_defaultReturnRotateToTravel;
        public bool DefaultReturnLiftToTravel => m_defaultReturnLiftToTravel;

        /// <summary>将本组件默认值写入 Clip（新增 Clip 时调用）。</summary>
        public void ApplyClipDefaults(CtuClipData data)
        {
            if (data == null)
                return;

            data.LiftTravelHeight = m_defaultLiftTravelHeight;
            data.LiftPlaceHeight = m_defaultLiftPlaceHeight;
            data.ClawRetracted = m_defaultClawRetracted;
            data.ClawExtended = m_defaultClawExtended;
            data.ReturnRotateToTravel = m_defaultReturnRotateToTravel;
            data.ReturnLiftToTravel = m_defaultReturnLiftToTravel;
            data.RotateSpeed = m_turretRotateSpeed;
            data.LiftSpeed = m_liftSpeed;
            data.ClawSpeed = m_clawSpeed;
            data.PaddleSpeed = m_paddleSpeed;
        }

        private void Awake()
        {
            CaptureRotateBaseIfNeeded();
            CapturePaddleBaseIfNeeded();
        }

        /// <summary>
        /// 记录当前转台本地旋转为 0° 基准（应对准车头后再调；Timeline / 采样共用）。
    /// </summary>
        public void CaptureRotateBase()
        {
            if (m_rotate == null)
                return;
            m_rotateBaseLocal = m_rotate.localRotation;
            m_rotateBaseCaptured = true;
        }

        public void SetRotateAngle(float degrees)
        {
            if (m_rotate == null)
                return;

            CaptureRotateBaseIfNeeded();
            m_rotate.localRotation =
                m_rotateBaseLocal * Quaternion.AngleAxis(degrees, RotateAxisLocal);
        }

        public float GetRotateAngle()
        {
            if (m_rotate == null)
                return m_rotateTravelAngle;

            CaptureRotateBaseIfNeeded();
            Quaternion delta = Quaternion.Inverse(m_rotateBaseLocal) * m_rotate.localRotation;
            delta.ToAngleAxis(out float angle, out Vector3 axis);
            if (axis.sqrMagnitude < 1e-8f)
                return 0f;
            if (angle > 180f)
                angle -= 360f;
            if (Vector3.Dot(axis.normalized, RotateAxisLocal) < 0f)
                angle = -angle;
            return angle;
        }

        public void SetLiftHeight(float height)
        {
            if (m_lift == null)
                return;

            Vector3 axis = LiftAxisLocal;
            Vector3 local = m_lift.localPosition;
            local -= axis * Vector3.Dot(local, axis);
            local += axis * height;
            m_lift.localPosition = local;
        }

        public float GetLiftHeight()
        {
            if (m_lift == null)
                return 0f;
            return Vector3.Dot(m_lift.localPosition, LiftAxisLocal);
        }

        public void SetClawExtend(float extend)
        {
            if (m_claw == null)
                return;

            Vector3 axis = ClawAxisLocal;
            Vector3 local = m_claw.localPosition;
            local -= axis * Vector3.Dot(local, axis);
            local += axis * extend;
            m_claw.localPosition = local;
        }

        public float GetClawExtend()
        {
            if (m_claw == null)
                return 0f;
            return Vector3.Dot(m_claw.localPosition, ClawAxisLocal);
        }

        /// <summary>记录拨爪本地旋转为开位基准（应对准松开姿态后再调）。</summary>
        public void CapturePaddleBase()
        {
            if (m_paddleA != null)
                m_paddleABaseLocal = m_paddleA.localRotation;
            if (m_paddleB != null)
                m_paddleBBaseLocal = m_paddleB.localRotation;
            m_paddleBaseCaptured = m_paddleA != null || m_paddleB != null;
        }

        /// <summary>
        /// 写入拨爪角（度，相对开位基准）。A 用 <paramref name="degrees"/>，B 乘 <see cref="PaddleBSign"/>。
    /// 开位一般为 <see cref="PaddleOpenAngle"/>，固定货物为 <see cref="PaddleLockedAngle"/>。
    /// </summary>
        public void SetPaddleAngle(float degrees)
        {
            CapturePaddleBaseIfNeeded();
            Vector3 axis = PaddleAxisLocal;
            if (m_paddleA != null)
            {
                m_paddleA.localRotation =
                    m_paddleABaseLocal * Quaternion.AngleAxis(degrees, axis);
            }

            if (m_paddleB != null)
            {
                m_paddleB.localRotation =
                    m_paddleBBaseLocal * Quaternion.AngleAxis(degrees * PaddleBSign, axis);
            }
        }

        public float GetPaddleAngle()
        {
            if (m_paddleA == null)
                return m_paddleOpenAngle;

            CapturePaddleBaseIfNeeded();
            Quaternion delta = Quaternion.Inverse(m_paddleABaseLocal) * m_paddleA.localRotation;
            delta.ToAngleAxis(out float angle, out Vector3 axis);
            if (axis.sqrMagnitude < 1e-8f)
                return 0f;
            if (angle > 180f)
                angle -= 360f;
            if (Vector3.Dot(axis.normalized, PaddleAxisLocal) < 0f)
                angle = -angle;
            return angle;
        }

        /// <summary>
        /// 转台应对准货点的目标角（度）：车体前方 → 水平指向货点的有符号角。
    /// </summary>
        public float ResolveRotateAngleTo(Vector3 worldTarget) =>
            ResolveRotateAngleTo(worldTarget, Body.position, Body.rotation);

        /// <summary>
        /// 按指定车体位姿计算转台目标角（采样前车体可能尚未写入 Home）。
    /// 旋转节点在举升之下，水平指向仍相对车体前方。
    /// </summary>
        public float ResolveRotateAngleTo(Vector3 worldTarget, Vector3 bodyPos, Quaternion bodyRot)
        {
            Vector3 from = bodyPos;
            if (m_rotate != null && m_rotate.IsChildOf(Body))
            {
                // 旋转节点相对车体的本地偏移（含举升高度），用目标车体位姿还原世界位置
                Vector3 local = Body.InverseTransformPoint(m_rotate.position);
                from = bodyPos + bodyRot * local;
            }

            Vector3 flat = Flatten(worldTarget - from);
            if (flat.sqrMagnitude < 1e-8f)
                return GetRotateAngle();

            Vector3 bodyFwd = Flatten(bodyRot * Forward);
            if (bodyFwd.sqrMagnitude < 1e-8f)
                return GetRotateAngle();

            return Vector3.SignedAngle(bodyFwd.normalized, flat.normalized, Vector3.up);
        }

        private void CaptureRotateBaseIfNeeded()
        {
            if (m_rotateBaseCaptured || m_rotate == null)
                return;
            CaptureRotateBase();
        }

        private void CapturePaddleBaseIfNeeded()
        {
            if (m_paddleBaseCaptured || (m_paddleA == null && m_paddleB == null))
                return;
            CapturePaddleBase();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            m_turretRotateSpeed = Mathf.Max(0.01f, m_turretRotateSpeed);
            m_liftSpeed = Mathf.Max(0.01f, m_liftSpeed);
            m_clawSpeed = Mathf.Max(0.01f, m_clawSpeed);
            m_paddleSpeed = Mathf.Max(0.01f, m_paddleSpeed);
            if (Mathf.Approximately(m_paddleBSign, 0f))
                m_paddleBSign = -1f;
        }
#endif
    }
}
