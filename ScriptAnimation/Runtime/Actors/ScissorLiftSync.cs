using System;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 剪式抬升机构视觉同步：平台仍用 <see cref="LatentAgvAnim"/> / <see cref="ForkliftAnim"/> 等驱动，
    /// 本组件只根据底铰点 → 顶铰点的间距，同步各层四根剪叉。
    /// <para>
    /// 每一层固定四槽：左近、左远、右近、右远。
    /// 近远是车体两侧的两排，左右是交叉的两向（\ 与 /）。
    /// </para>
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(2000)]
    [DisallowMultipleComponent]
    [AddComponentMenu("ScriptAnimation/剪叉抬升同步 (ScissorLiftSync)")]
    public class ScissorLiftSync : MonoBehaviour
    {
        [Serializable]
        public struct ArmRest
        {
            public Vector3 LocalPosition;
            public Quaternion LocalRotation;
            public float Lateral;
        }

        [Serializable]
        public class Stage
        {
            [Tooltip("近侧同向剪叉（\\）")]
            public Transform LeftNear;

            [Tooltip("远侧同向剪叉（\\）")]
            public Transform LeftFar;

            [Tooltip("近侧反向剪叉（/）")]
            public Transform RightNear;

            [Tooltip("远侧反向剪叉（/）")]
            public Transform RightFar;

            [HideInInspector] public ArmRest LeftNearRest;
            [HideInInspector] public ArmRest LeftFarRest;
            [HideInInspector] public ArmRest RightNearRest;
            [HideInInspector] public ArmRest RightFarRest;
        }

        [Header("铰点")]
        [Tooltip("底铰点，挂在底盘下，升起时不动")]
        [SerializeField] private Transform m_bottomHinge;

        [Tooltip("顶铰点，挂在抬升平台下，随平台升起")]
        [SerializeField] private Transform m_topHinge;

        [Tooltip("抬升平台（仅用于自动绑定顶铰点；运动仍由 LatentAgvAnim / ForkliftAnim 等驱动）")]
        [SerializeField] private Transform m_platform;

        [Tooltip("轴向参考；为空则用本物体")]
        [SerializeField] private Transform m_base;

        [Header("层")]
        [Tooltip("剪叉层数。每一层固定四根：左近 / 左远 / 右近 / 右远")]
        [SerializeField] private int m_stageCount = 1;

        [SerializeField] private Stage[] m_stages = Array.Empty<Stage>();

        [Header("轴向（底盘本地）")]
        [Tooltip("抬升轴，通常 +Y")]
        [SerializeField] private Vector3 m_liftAxisLocal = Vector3.up;

        [Tooltip("剪叉张合方向（展开轴），通常是车体前后 +Z")]
        [SerializeField] private Vector3 m_foldAxisLocal = Vector3.forward;

        [Tooltip("剪叉平面法线（近/远两侧沿此轴分开），通常是车体左右 +X")]
        [SerializeField] private Vector3 m_rotationAxisLocal = Vector3.right;

        [Header("杆")]
        [Tooltip("单根剪叉长度（米）。≤0 则按 Rest 估算。缩短杆长不会改间距。")]
        [SerializeField] private float m_armLength;

        [Tooltip("左右铰点沿展开轴的间距（米）。≤0 则用 Rest 捕获值。")]
        [SerializeField] private float m_foldSpan;

        [Tooltip("近远两排沿旋转轴的间距（米）。≤0 则用 Rest 捕获值。")]
        [SerializeField] private float m_sideSpan;

        [Tooltip("勾选后展开间距随高度按杆长勾股收缩。关闭则间距保持上面的值，只改变张角。")]
        [SerializeField] private bool m_shrinkSpanWithLift;

        [Tooltip("网格原点在杆上的位置：0=在底端铰点，0.5=在杆中心")]
        [SerializeField] [Range(0f, 1f)] private float m_pivotAlongArm;

        [Header("Rest（收拢姿态）")]
        [SerializeField] private bool m_hasRest;
        [SerializeField] private float m_restHeight;
        [SerializeField] private float m_resolvedArmLength;
        [SerializeField] private float m_restFoldSpan;
        [SerializeField] private float m_restSideSpan;

        [Header("驱动")]
        [Tooltip("开启后只运算一次，之后不再每帧更新。适合静止物体摆姿态。")]
        [SerializeField] private bool m_applyOnce;

        [Header("编辑器")]
        [Tooltip("编辑模式下也跟随铰点（便于拖平台预览）。Timeline 预览始终跟随。不要在升起时保存场景。")]
        [SerializeField] private bool m_driveInEditMode;

        private bool m_applied;
        private bool m_appliedOnce;
        private static Func<bool> s_editorInAnimationMode;

        public Transform BottomHinge => m_bottomHinge != null ? m_bottomHinge : BaseTransform;
        public Transform TopHinge => m_topHinge != null ? m_topHinge : m_platform;
        public Transform Platform => m_platform;
        public Transform BaseTransform => m_base != null ? m_base : transform;
        public int StageCount => Mathf.Max(1, m_stageCount);
        public Stage[] Stages => m_stages;
        public bool HasRest => m_hasRest;
        public float RestHeight => m_restHeight;
        public bool ShrinkSpanWithLift => m_shrinkSpanWithLift;
        public bool ApplyOnce => m_applyOnce;

        public float ArmLength =>
            m_armLength > 1e-4f
                ? m_armLength
                : (m_resolvedArmLength > 1e-4f ? m_resolvedArmLength : 0.5f);

        public float FoldSpan => ResolveFoldSpan();
        public float SideSpan => ResolveSideSpan();

        public Vector3 LiftAxisLocal =>
            m_liftAxisLocal.sqrMagnitude > 1e-8f ? m_liftAxisLocal.normalized : Vector3.up;

        public Vector3 FoldAxisLocal =>
            m_foldAxisLocal.sqrMagnitude > 1e-8f ? m_foldAxisLocal.normalized : Vector3.forward;

        public Vector3 RotationAxisLocal =>
            m_rotationAxisLocal.sqrMagnitude > 1e-8f ? m_rotationAxisLocal.normalized : Vector3.right;

        private void Reset()
        {
            TryAutoBindPlatform();
            EnsureStageArray();
        }

        private void OnValidate()
        {
            EnsureStageArray();
            if (!m_applyOnce)
                m_appliedOnce = false;
        }

        private void OnEnable()
        {
            EnsureStageArray();
            m_appliedOnce = false;
            if (!m_hasRest && TopHinge != null && HasAnyArm())
                CaptureRestPose();
        }

        private void OnDisable()
        {
            // 仅初始运算：保留已摆好的姿态，不回 Rest。
            if (!m_applyOnce)
                RestoreRestPose();
            m_applied = false;
            m_appliedOnce = false;
        }

        private void LateUpdate()
        {
            if (!ShouldDrive())
            {
                if (m_applied && !m_applyOnce)
                {
                    RestoreRestPose();
                    m_applied = false;
                }

                return;
            }

            if (m_applyOnce && m_appliedOnce)
                return;

            if (!m_hasRest)
                CaptureRestPose();

            ApplyPose();
            m_applied = true;
            if (m_applyOnce)
                m_appliedOnce = true;
        }

        [ContextMenu("立即应用姿态")]
        public void ApplyPoseNow()
        {
            if (TopHinge == null || BottomHinge == null || !HasAnyArm())
                return;

            if (!m_hasRest)
                CaptureRestPose();

            ApplyPose();
            m_applied = true;
            if (m_applyOnce)
                m_appliedOnce = true;
        }

        public void EnsureStageArray()
        {
            int n = Mathf.Max(1, m_stageCount);
            m_stageCount = n;
            if (m_stages == null)
                m_stages = new Stage[n];
            else if (m_stages.Length != n)
                Array.Resize(ref m_stages, n);

            for (int i = 0; i < m_stages.Length; i++)
            {
                if (m_stages[i] == null)
                    m_stages[i] = new Stage();
            }
        }

        public bool TryAutoBindPlatform()
        {
            if (m_platform != null)
                return true;

            var latent = GetComponent<LatentAgvAnim>();
            if (latent != null && latent.Platform != null)
            {
                m_platform = latent.Platform;
                if (m_topHinge == null)
                    m_topHinge = latent.Platform;
                return true;
            }

            var forklift = GetComponent<ForkliftAnim>();
            if (forklift != null && forklift.Fork != null)
            {
                m_platform = forklift.Fork;
                if (m_topHinge == null)
                    m_topHinge = forklift.Fork;
                return true;
            }

            var stacker = GetComponent<StackerAnim>();
            if (stacker != null && stacker.LiftAxis != null)
            {
                m_platform = stacker.LiftAxis;
                if (m_topHinge == null)
                    m_topHinge = stacker.LiftAxis;
                return true;
            }

            return false;
        }

        [ContextMenu("从当前姿态捕获 Rest")]
        public void CaptureRestPose()
        {
            EnsureStageArray();
            m_restHeight = ReadHeight();
            m_hasRest = true;
            m_resolvedArmLength = m_armLength > 1e-4f ? m_armLength : EstimateArmLength();
            m_restFoldSpan = MeasureLiveFoldSpan();

            Transform b = BaseTransform;
            Vector3 axis = RotationAxisLocal;
            Vector3 hingeLocal = b.InverseTransformPoint(BottomHinge.position);

            for (int i = 0; i < m_stages.Length; i++)
            {
                Stage stage = m_stages[i];
                CaptureArm(stage.LeftNear, ref stage.LeftNearRest, b, hingeLocal, axis);
                CaptureArm(stage.LeftFar, ref stage.LeftFarRest, b, hingeLocal, axis);
                CaptureArm(stage.RightNear, ref stage.RightNearRest, b, hingeLocal, axis);
                CaptureArm(stage.RightFar, ref stage.RightFarRest, b, hingeLocal, axis);
            }

            if (m_restFoldSpan <= 1e-4f)
                m_restFoldSpan = FoldSpanFromRestPoses();
            m_restSideSpan = MeasureRestSideSpanFromPoses();
            if (m_restSideSpan <= 1e-4f)
                m_restSideSpan = MeasureLiveSideSpan();
        }

        [ContextMenu("恢复 Rest 姿态")]
        public void RestoreRestPose()
        {
            if (!m_hasRest || m_stages == null)
                return;

            for (int i = 0; i < m_stages.Length; i++)
            {
                Stage stage = m_stages[i];
                if (stage == null)
                    continue;
                RestoreArm(stage.LeftNear, stage.LeftNearRest);
                RestoreArm(stage.LeftFar, stage.LeftFarRest);
                RestoreArm(stage.RightNear, stage.RightNearRest);
                RestoreArm(stage.RightFar, stage.RightFarRest);
            }
        }

        public float ReadHeight()
        {
            Transform top = TopHinge;
            Transform bottom = BottomHinge;
            if (top == null || bottom == null)
                return 0f;

            Vector3 lift = BaseTransform.TransformDirection(LiftAxisLocal);
            return Vector3.Dot(top.position - bottom.position, lift);
        }

        public float ReadOpenAngleDegrees()
        {
            float length = ArmLength;
            int n = StageCount;
            if (length < 1e-4f || n < 1)
                return 0f;

            float hStage = Mathf.Max(1e-4f, ReadHeight() / n);
            if (m_shrinkSpanWithLift)
            {
                hStage = Mathf.Clamp(hStage, 1e-4f, length * 0.999f);
                return Mathf.Asin(hStage / length) * Mathf.Rad2Deg;
            }

            float span = ResolveFoldSpan();
            float diagonal = Mathf.Sqrt(span * span + hStage * hStage);
            if (diagonal < 1e-4f)
                return 0f;
            return Mathf.Asin(Mathf.Clamp(hStage / diagonal, 0f, 0.999f)) * Mathf.Rad2Deg;
        }

        public bool HasAnyArm()
        {
            if (m_stages == null)
                return false;
            for (int i = 0; i < m_stages.Length; i++)
            {
                Stage stage = m_stages[i];
                if (stage == null)
                    continue;
                if (stage.LeftNear != null || stage.LeftFar != null ||
                    stage.RightNear != null || stage.RightFar != null)
                    return true;
            }

            return false;
        }

        private void ApplyPose()
        {
            if (!TryBuildFrame(out ScissorFrame frame))
                return;

            for (int i = 0; i < m_stages.Length; i++)
            {
                Stage stage = m_stages[i];
                if (stage == null)
                    continue;

                BuildStageCorners(in frame, i, out Vector3 bl, out Vector3 br, out Vector3 tl, out Vector3 tr);
                AimArm(stage.LeftNear, stage.LeftNearRest, bl, tr, frame.Lift, frame.LateralAxis, near: true);
                AimArm(stage.LeftFar, stage.LeftFarRest, bl, tr, frame.Lift, frame.LateralAxis, near: false);
                AimArm(stage.RightNear, stage.RightNearRest, br, tl, frame.Lift, frame.LateralAxis, near: true);
                AimArm(stage.RightFar, stage.RightFarRest, br, tl, frame.Lift, frame.LateralAxis, near: false);
            }
        }

        private struct ScissorFrame
        {
            public Vector3 Bottom;
            public Vector3 Top;
            public Vector3 Lift;
            public Vector3 Fold;
            public Vector3 LateralAxis;
            public float HalfSpan;
            public int StageCount;
        }

        private bool TryBuildFrame(out ScissorFrame frame)
        {
            frame = default;
            Transform bottom = BottomHinge;
            Transform top = TopHinge;
            if (bottom == null || top == null || m_stages == null)
                return false;

            Transform b = BaseTransform;
            Vector3 lift = b.TransformDirection(LiftAxisLocal);
            Vector3 fold = Vector3.ProjectOnPlane(b.TransformDirection(FoldAxisLocal), lift);
            if (fold.sqrMagnitude < 1e-8f)
                return false;
            fold.Normalize();

            Vector3 lateral = b.TransformDirection(RotationAxisLocal);
            lateral = Vector3.ProjectOnPlane(lateral, lift);
            if (lateral.sqrMagnitude < 1e-8f)
                lateral = Vector3.Cross(fold, lift);
            lateral.Normalize();

            int n = StageCount;
            float height = Vector3.Dot(top.position - bottom.position, lift);
            float length = ArmLength;
            float hStage = n > 0 ? height / n : height;
            float restHalfSpan = Mathf.Max(1e-4f, ResolveFoldSpan() * 0.5f);
            float halfSpan = restHalfSpan;
            if (m_shrinkSpanWithLift && length > 1e-4f)
            {
                float clamped = Mathf.Clamp(hStage, 1e-4f, length * 0.999f);
                halfSpan = 0.5f * Mathf.Sqrt(Mathf.Max(0f, length * length - clamped * clamped));
            }

            frame = new ScissorFrame
            {
                Bottom = bottom.position,
                Top = top.position,
                Lift = lift,
                Fold = fold,
                LateralAxis = lateral,
                HalfSpan = halfSpan,
                StageCount = n
            };
            return true;
        }

        private static void BuildStageCorners(
            in ScissorFrame frame, int index,
            out Vector3 bl, out Vector3 br, out Vector3 tl, out Vector3 tr)
        {
            float t0 = frame.StageCount <= 0 ? 0f : (float)index / frame.StageCount;
            float t1 = frame.StageCount <= 0 ? 1f : (float)(index + 1) / frame.StageCount;
            Vector3 centerB = Vector3.Lerp(frame.Bottom, frame.Top, t0);
            Vector3 centerT = Vector3.Lerp(frame.Bottom, frame.Top, t1);
            Vector3 offset = frame.Fold * frame.HalfSpan;
            bl = centerB - offset;
            br = centerB + offset;
            tl = centerT - offset;
            tr = centerT + offset;
        }

        private void AimArm(
            Transform arm, ArmRest rest,
            Vector3 from, Vector3 to,
            Vector3 lift, Vector3 lateralAxis, bool near)
        {
            if (arm == null)
                return;

            Vector3 dir = to - from;
            if (dir.sqrMagnitude < 1e-10f)
                return;

            Quaternion rotation = Quaternion.LookRotation(dir.normalized, lift);
            Vector3 origin = Vector3.Lerp(from, to, m_pivotAlongArm);
            float lateral = ResolveSlotLateral(rest, near);
            arm.SetPositionAndRotation(origin + lateralAxis * lateral, rotation);
        }

        private float ResolveSlotLateral(ArmRest rest, bool near)
        {
            float half = ResolveSideSpan() * 0.5f;
            float sign = near ? -1f : 1f;
            float restHalf = ResolveRestSideSpan() * 0.5f;
            if (restHalf > 1e-4f)
            {
                float nudge = rest.Lateral - sign * restHalf;
                return sign * half + nudge;
            }

            return sign * half;
        }

        private float ResolveSideSpan()
        {
            if (m_sideSpan > 1e-4f)
                return m_sideSpan;
            return ResolveRestSideSpan();
        }

        private float ResolveRestSideSpan()
        {
            if (m_restSideSpan > 1e-4f)
                return m_restSideSpan;
            return MeasureRestSideSpanFromPoses();
        }

        private float EstimateArmLength()
        {
            if (m_armLength > 1e-4f)
                return m_armLength;

            float height = Mathf.Max(1e-4f, ReadHeight());
            float hStage = height / StageCount;
            float span = ResolveFoldSpan();
            if (span > 1e-4f)
                return Mathf.Sqrt(span * span + hStage * hStage);

            return Mathf.Max(0.2f, hStage / Mathf.Sin(18f * Mathf.Deg2Rad));
        }

        private float MeasureLiveFoldSpan()
        {
            if (m_stages == null || m_stages.Length == 0)
                return 0f;

            Stage stage = m_stages[0];
            Transform left = stage.LeftNear != null ? stage.LeftNear : stage.LeftFar;
            Transform right = stage.RightNear != null ? stage.RightNear : stage.RightFar;
            if (left == null || right == null)
                return 0f;

            Vector3 fold = BaseTransform.TransformDirection(FoldAxisLocal);
            if (fold.sqrMagnitude < 1e-8f)
                return 0f;
            return Mathf.Abs(Vector3.Dot(right.position - left.position, fold.normalized));
        }

        private float ResolveFoldSpan()
        {
            if (m_foldSpan > 1e-4f)
                return m_foldSpan;
            if (m_restFoldSpan > 1e-4f)
                return m_restFoldSpan;
            float fromRest = FoldSpanFromRestPoses();
            return fromRest > 1e-4f ? fromRest : MeasureLiveFoldSpan();
        }

        private float FoldSpanFromRestPoses()
        {
            if (m_stages == null || m_stages.Length == 0)
                return 0f;

            Stage stage = m_stages[0];
            Vector3 fold = FoldAxisLocal;
            float left = Vector3.Dot(PickLeftRest(stage).LocalPosition, fold);
            float right = Vector3.Dot(PickRightRest(stage).LocalPosition, fold);
            return Mathf.Abs(right - left);
        }

        private static ArmRest PickLeftRest(Stage stage) =>
            stage.LeftNear != null ? stage.LeftNearRest : stage.LeftFarRest;

        private static ArmRest PickRightRest(Stage stage) =>
            stage.RightNear != null ? stage.RightNearRest : stage.RightFarRest;

        private float MeasureRestSideSpanFromPoses()
        {
            if (m_stages == null || m_stages.Length == 0)
                return 0f;

            Stage stage = m_stages[0];
            float near = 0f;
            float far = 0f;
            int nearCount = 0;
            int farCount = 0;
            AccumulateLateral(stage.LeftNear, stage.LeftNearRest, ref near, ref nearCount);
            AccumulateLateral(stage.RightNear, stage.RightNearRest, ref near, ref nearCount);
            AccumulateLateral(stage.LeftFar, stage.LeftFarRest, ref far, ref farCount);
            AccumulateLateral(stage.RightFar, stage.RightFarRest, ref far, ref farCount);
            if (nearCount == 0 || farCount == 0)
                return 0f;
            return Mathf.Abs(far / farCount - near / nearCount);
        }

        private static void AccumulateLateral(Transform arm, ArmRest rest, ref float sum, ref int count)
        {
            if (arm == null)
                return;
            sum += rest.Lateral;
            count++;
        }

        private float MeasureLiveSideSpan()
        {
            if (m_stages == null || m_stages.Length == 0)
                return 0f;

            Stage stage = m_stages[0];
            Transform near = stage.LeftNear != null ? stage.LeftNear : stage.RightNear;
            Transform far = stage.LeftFar != null ? stage.LeftFar : stage.RightFar;
            if (near == null || far == null)
                return 0f;

            Vector3 axis = BaseTransform.TransformDirection(RotationAxisLocal);
            if (axis.sqrMagnitude < 1e-8f)
                return 0f;
            return Mathf.Abs(Vector3.Dot(far.position - near.position, axis.normalized));
        }

        private static void CaptureArm(
            Transform arm, ref ArmRest rest,
            Transform b, Vector3 hingeLocal, Vector3 axisLocal)
        {
            rest = new ArmRest { LocalRotation = Quaternion.identity };
            if (arm == null)
                return;

            rest.LocalPosition = arm.localPosition;
            rest.LocalRotation = arm.localRotation;
            Vector3 armLocal = b.InverseTransformPoint(arm.position);
            rest.Lateral = Vector3.Dot(armLocal - hingeLocal, axisLocal);
        }

        private static void RestoreArm(Transform arm, ArmRest rest)
        {
            if (arm == null)
                return;
            arm.localPosition = rest.LocalPosition;
            arm.localRotation = rest.LocalRotation;
        }

        private bool ShouldDrive()
        {
            if (!isActiveAndEnabled || TopHinge == null || BottomHinge == null || !HasAnyArm())
                return false;
            if (Application.isPlaying)
                return true;
            if (m_driveInEditMode)
                return true;
            return EditorInAnimationMode();
        }

        private static bool EditorInAnimationMode()
        {
#if UNITY_EDITOR
            if (s_editorInAnimationMode == null)
            {
                Type type = Type.GetType("UnityEditor.AnimationMode, UnityEditor");
                var method = type?.GetMethod("InAnimationMode", Type.EmptyTypes);
                if (method != null)
                    s_editorInAnimationMode = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), method);
            }

            return s_editorInAnimationMode != null && s_editorInAnimationMode();
#else
            return false;
#endif
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!TryBuildFrame(out ScissorFrame frame))
            {
                if (BottomHinge != null)
                {
                    Gizmos.color = Color.yellow;
                    Gizmos.DrawSphere(BottomHinge.position, 0.03f);
                }

                if (TopHinge != null)
                {
                    Gizmos.color = new Color(0.3f, 1f, 0.4f);
                    Gizmos.DrawSphere(TopHinge.position, 0.03f);
                }

                return;
            }

            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(frame.Bottom, 0.03f);
            Gizmos.color = new Color(0.3f, 1f, 0.4f);
            Gizmos.DrawSphere(frame.Top, 0.03f);
            Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.5f);
            Gizmos.DrawLine(frame.Bottom, frame.Top);

            for (int i = 0; i < frame.StageCount; i++)
            {
                BuildStageCorners(in frame, i, out Vector3 bl, out Vector3 br, out Vector3 tl, out Vector3 tr);
                Gizmos.color = new Color(1f, 0.45f, 0.2f, 0.9f);
                Gizmos.DrawLine(bl, tr);
                Gizmos.color = new Color(0.2f, 0.75f, 0.85f, 0.9f);
                Gizmos.DrawLine(br, tl);
            }
        }
#endif
    }
}
