using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 路径移动基类（PathMove）：沿 PathNetwork 折线移动的参数与能力。
    /// 堆垛机货位坐标移动请用 <see cref="StackerAnim"/>。
    /// </summary>
    [AddComponentMenu("ScriptAnimation/路径移动 (PathMoveActor)")]
    public class PathMoveActor : ScriptAnimActor
    {
        [Header("新增 Clip 默认值（写入 Clip，可再改）")]
        [Tooltip("新增移动类 Clip 时写入的默认移动速度（米/秒）。")]
        [InspectorLabel("移动速度")]
        [SerializeField] private float m_moveSpeed = 2f;

        [Tooltip("移动类型：边走边转 / 先转后移 / 仅移动。")]
        [InspectorLabel("移动类型")]
        [SerializeField] private PathMoveMode m_moveMode = PathMoveMode.FaceWhileMove;

        [Tooltip("新增 Clip 时写入的默认旋转速度（度/秒）。")]
        [InspectorLabel("旋转速度")]
        [SerializeField] private float m_rotateSpeed = 90f;

        [Tooltip("新增 BezierCorner / BezierDualCorner / ReverseUTurn Clip 时写入的提前转弯距离。")]
        [InspectorLabel("提前转弯距离")]
        [SerializeField] private float m_earlyTurnDistance = 1f;

        [Tooltip("新增 ThreePointTurn / ReverseUTurn Clip 时写入的后退/机动距离。")]
        [InspectorLabel("后退距离")]
        [SerializeField] private float m_backDistance = 0.8f;

        [Tooltip("新增移动类 Clip 时复制写入的移动曲线。")]
        [InspectorLabel("移动曲线")]
        [SerializeField] private AnimationCurve m_moveCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Header("路径")]
        [InspectorLabel("路径点偏移")]
        [SerializeField] private Vector3 m_pathOffset;

        [Header("Home（无前序开场）")]
        [Tooltip("轨上第一段 Clip / 找不到前序时用此位姿开场，不读场景当前 Transform。")]
        [InspectorLabel("已设置 Home")]
        [SerializeField] private bool m_hasHome;

        [InspectorLabel("Home 位置")]
        [SerializeField] private Vector3 m_homePosition;

        [Tooltip("绕世界+Y 的偏航角（度）。")]
        [InspectorLabel("Home 偏航")]
        [SerializeField] private float m_homeYawDegrees;

        [Header("朝向轴（模型本地）")]
        [Tooltip("模型本地上方 / 车顶（默认 +Y）。例如模型 Z 朝上则选 +Z。地面仍为世界 +Y。")]
        [InspectorLabel("上方轴")]
        [SerializeField] private SignedAxis m_upAxis = SignedAxis.PositiveY;

        [Tooltip("模型本地前方 / 车头（默认 +Z）。例如车头沿 -X 则选 -X。")]
        [InspectorLabel("前方轴")]
        [SerializeField] private SignedAxis m_forwardAxis = SignedAxis.PositiveZ;

        public float MoveSpeed => m_moveSpeed;
        /// <summary>新增 Clip 时写入的默认旋转速度（度/秒）。</summary>
        public float RotateSpeed => m_rotateSpeed;
        public float EarlyTurnDistance => m_earlyTurnDistance;
        public float BackDistance => m_backDistance;
        public PathMoveMode MoveMode => m_moveMode;
        public bool RotateBeforeMove => m_moveMode == PathMoveMode.RotateThenMove;
        public Vector3 PathOffset => m_pathOffset;

        public SignedAxis UpAxis => m_upAxis;
        public SignedAxis ForwardAxis => m_forwardAxis;

        public bool HasHome => m_hasHome;
        public Vector3 HomePosition => m_homePosition;
        public float HomeYawDegrees => m_homeYawDegrees;

        /// <summary>Home 朝向（由偏航 + 模型前后轴合成）。</summary>
        public Quaternion HomeRotation =>
            RotationFromYawDegrees(m_homeYawDegrees, Quaternion.identity);

        /// <summary>模型本地上方单位向量。</summary>
        public Vector3 Up => SignedAxisUtil.ToVector(m_upAxis);

        /// <summary>模型本地前方单位向量。</summary>
        public Vector3 Forward => SignedAxisUtil.ToVector(m_forwardAxis);

        static readonly AnimationCurve Linear01 = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        public AnimationCurve MoveCurve =>
            m_moveCurve != null && m_moveCurve.length > 0
                ? m_moveCurve
                : Linear01;

        /// <summary>投影到世界 XZ 地面（与 PathNetwork 一致，世界上方固定 +Y）。</summary>
        public Vector3 Flatten(Vector3 direction)
        {
            direction.y = 0f;
            return direction;
        }

        /// <summary>保持参考点的世界 Y，水平位置取 pos。</summary>
        public Vector3 WithUpHeight(Vector3 pos, Vector3 reference)
        {
            pos.y = reference.y;
            return pos;
        }

        /// <summary>
        /// 用模型本地上方前方轴，把世界前进方向转成物体旋转。
    /// 模型上方 → 世界 +Y，模型前方 → 水平前进方向。
    /// </summary>
        public Quaternion LookRotation(Vector3 worldDirection, Quaternion fallback)
        {
            Vector3 flat = Flatten(worldDirection);
            if (flat.sqrMagnitude < 1e-8f)
                return fallback;

            Vector3 upLocal = Up;
            Vector3 fwdLocal = Forward;
            Quaternion worldBasis = Quaternion.LookRotation(flat.normalized, Vector3.up);
            if (Mathf.Abs(Vector3.Dot(fwdLocal, upLocal)) > 0.999f)
                return worldBasis;

            Quaternion localBasis = Quaternion.LookRotation(fwdLocal, upLocal);
            return worldBasis * Quaternion.Inverse(localBasis);
        }

        /// <summary>将任意旋转压到「模型前方落在水平面、模型上方对齐世界 +Y」。</summary>
        public Quaternion FlattenRotation(Quaternion rotation, Quaternion fallback) =>
            LookRotation(rotation * Forward, fallback);

        /// <summary>绕世界 +Y 的 yaw（度）。</summary>
        public Quaternion RotationFromYawDegrees(float yawDegrees, Quaternion fallback)
        {
            Vector3 dir = Quaternion.Euler(0f, yawDegrees, 0f) * Vector3.forward;
            return LookRotation(dir, fallback);
        }

        /// <summary>已配置 Home 时写出位姿；未配置返回 false（不读 Body）。</summary>
        public bool TryGetHomePose(out Vector3 position, out Quaternion rotation)
        {
            position = m_homePosition;
            rotation = HomeRotation;
            return m_hasHome;
        }

        /// <summary>
        /// 无前序开场位姿：优先组件 Home；未配置时确定性回退到原点+ 偏航 0，并 Warning。
    /// </summary>
        public void ResolveHomePoseOrFallback(
            out Vector3 position,
            out Quaternion rotation,
            string callerLabel = null)
        {
            if (TryGetHomePose(out position, out rotation))
                return;

            position = Vector3.zero;
            rotation = RotationFromYawDegrees(0f, Quaternion.identity);
            string who = string.IsNullOrEmpty(callerLabel) ? name : callerLabel;
            Debug.LogWarning(
                $"[ScriptAnim] {who}：未配置默认 Home，回退原点朝前（偏航0）。" +
                "请在 PathMoveActor 上「从当前位姿捕获 Home」。",
                this);
        }

        /// <summary>仅取 Home 朝向；未配置时回退偏航 0 并 Warning。</summary>
        public Quaternion ResolveHomeRotationOrFallback(
            Quaternion geometricFallback,
            string callerLabel = null)
        {
            if (m_hasHome)
                return HomeRotation;

            string who = string.IsNullOrEmpty(callerLabel) ? name : callerLabel;
            Debug.LogWarning(
                $"[ScriptAnim] {who}：未配置默认 Home 朝向，使用默认零偏航回退。" +
                "请在 PathMoveActor 上「从当前位姿捕获 Home」。",
                this);
            return geometricFallback;
        }

        /// <summary>仅取 Home 位置；未配置时回退原点并 Warning。</summary>
        public Vector3 ResolveHomePositionOrFallback(string callerLabel = null)
        {
            if (m_hasHome)
                return m_homePosition;

            string who = string.IsNullOrEmpty(callerLabel) ? name : callerLabel;
            Debug.LogWarning(
                $"[ScriptAnim] {who}：未配置默认 Home 位置，回退原点。" +
                "请在 PathMoveActor 上「从当前位姿捕获 Home」。",
                this);
            return Vector3.zero;
        }

        /// <summary>写入组件 Home（或确定性回退），供首 Clip 之前 / 任意 seek 开场使用。</summary>
        public void ApplyHomePose(string callerLabel = null)
        {
            ResolveHomePoseOrFallback(out Vector3 position, out Quaternion rotation, callerLabel ?? "首 Clip 之前");
            Transform tr = MoverTransform;
            tr.position = position;
            tr.rotation = rotation;
        }

        [ContextMenu("从当前位姿捕获 Home")]
        public void CaptureHomeFromCurrent()
        {
            Transform body = Body;
            m_homePosition = body.position;
            Vector3 flat = Flatten(body.rotation * Forward);
            m_homeYawDegrees = flat.sqrMagnitude < 1e-8f
                ? 0f
                : Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
            m_hasHome = true;
        }

        /// <summary>复制曲线键；源为空时回退线性 0→1。</summary>
        public static AnimationCurve CopyCurve(AnimationCurve source)
        {
            if (source != null && source.length > 0)
                return new AnimationCurve(source.keys);
            return AnimationCurve.Linear(0f, 0f, 1f, 1f);
        }

        /// <summary>将本组件默认值写入 PathMove Clip（新增 Clip 时调用）。</summary>
        public void ApplyClipDefaults(PathMoveClipData data)
        {
            if (data == null)
                return;

            data.MoveSpeed = m_moveSpeed;
            data.MoveMode = m_moveMode;
            data.RotateSpeed = m_rotateSpeed;
            data.MoveCurve = CopyCurve(MoveCurve);
        }

        /// <summary>将本组件默认值写入 DirectMove Clip（新增 Clip 时调用）。</summary>
        public void ApplyClipDefaults(DirectMoveClipData data)
        {
            if (data == null)
                return;

            data.MoveSpeed = m_moveSpeed;
            data.MoveCurve = CopyCurve(MoveCurve);
        }

        /// <summary>将本组件默认值写入 Rotate Clip（新增 Clip 时调用）。</summary>
        public void ApplyClipDefaults(RotateClipData data)
        {
            if (data == null)
                return;

            data.RotateSpeed = m_rotateSpeed;
            data.RotateCurve = CopyCurve(MoveCurve);
        }

        /// <summary>将本组件默认值写入三点转向 Clip（新增 Clip 时调用）。</summary>
        public void ApplyClipDefaults(ThreePointTurnClipData data)
        {
            if (data == null)
                return;

            data.ManeuverDistance = m_backDistance;
            data.MoveSpeed = m_moveSpeed;
            data.RotateSpeed = m_rotateSpeed;
            data.MoveCurve = CopyCurve(MoveCurve);
        }

        /// <summary>将本组件默认值写入贝塞尔直角弯 Clip（新增 Clip 时调用）。</summary>
        public void ApplyClipDefaults(BezierCornerClipData data)
        {
            if (data == null)
                return;

            data.EarlyTurnDistance = m_earlyTurnDistance;
            data.MoveSpeed = m_moveSpeed;
            data.MoveCurve = CopyCurve(MoveCurve);
        }

        /// <summary>将本组件默认值写入双拐点贝塞尔弯 Clip（新增 Clip 时调用）。</summary>
        public void ApplyClipDefaults(BezierDualCornerClipData data)
        {
            if (data == null)
                return;

            data.EarlyTurnDistance = m_earlyTurnDistance;
            data.MoveSpeed = m_moveSpeed;
            data.MoveCurve = CopyCurve(MoveCurve);
        }

        /// <summary>将本组件默认值写入倒车掉头 Clip（新增 Clip 时调用）。</summary>
        public void ApplyClipDefaults(ReverseUTurnClipData data)
        {
            if (data == null)
                return;

            data.BackDistance = m_backDistance;
            data.EarlyTurnDistance = m_earlyTurnDistance;
            data.MoveSpeed = m_moveSpeed;
            data.RotateSpeed = m_rotateSpeed;
            data.MoveCurve = CopyCurve(MoveCurve);
        }
    }
}
