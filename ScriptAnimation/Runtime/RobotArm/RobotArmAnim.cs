using System;
using System.Collections.Generic;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 六轴串链机械臂脚本动画：以轴点空对象父子链配置连杆，
    /// 由 <see cref="RobotArmClip"/> 给定点位链表后 IK 求解并依次补全关节运动。
    /// 轴数固定为 <see cref="JointCountFixed"/>（J1–J6）。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("ScriptAnimation/机械臂 (RobotArmAnim)")]
    public class RobotArmAnim : ScriptAnimActor
    {
        public const int JointCountFixed = 6;
        /// <summary>兼容旧校验：六轴机械臂最少/标准轴数均为 6。</summary>
        public const int MinJointCount = JointCountFixed;
        public const int DefaultJointCount = JointCountFixed;
        public const int J6Index = 5;
        public const int TipDownJointIndexFixed = 4;
        public const int ArmJointCountFixed = 3;

        [Serializable]
        public class JointSettings
        {
            [Tooltip("必填：该关节旋转中心。须与下一轴点 / 末端构成父子链。")]
            [InspectorLabel("轴点（空对象）")]
            public Transform AxisPoint;

            [Tooltip("实际旋转的 Transform。留空时 = 轴点自身。")]
            [InspectorLabel("驱动关节")]
            public Transform DriveJoint;

            [Tooltip("驱动关节本地空间的旋转轴（单位向量）")]
            [InspectorLabel("旋转轴（本地）")]
            public Vector3 AxisLocal = Vector3.up;

            [Tooltip("该关节之后的连杆长度（米），由轴点父子链自动测量")]
            [InspectorLabel("连杆长度（自动）")]
            public float LinkLength = 0.4f;

            [Tooltip("连杆在驱动关节本地空间中的方向（自动）")]
            [InspectorLabel("连杆方向（自动）")]
            public Vector3 LinkDirectionLocal = Vector3.up;

            [InspectorLabel("最小角")]
            public float MinAngle = -175f;
            [InspectorLabel("最大角")]
            public float MaxAngle = 175f;
        }

        [Header("轴链（轴点须父子串联：J1 → J2 → … → 末端）")]
        [InspectorLabel("关节列表")]
        [SerializeField] private JointSettings[] m_joints = CreateDefaultJoints();

        [Tooltip("轴点引用变更后，自动按子轴点 / 末端偏移写入连杆长度与方向")]
        [InspectorLabel("轴点变更时自动测连杆")]
        [SerializeField] private bool m_autoMeasureLinks = true;

        [Header("末端")]
        [Tooltip("末轴的子对象，标记 TCP。连杆 = 末轴点 → 末端轴点。")]
        [InspectorLabel("末端轴点")]
        [SerializeField] private Transform m_toolTip;

        [Tooltip("相对最后一轴的工具中心点偏移（本地）；有末端轴点且已测量时通常为 0")]
        [InspectorLabel("工具中心偏移")]
        [SerializeField] private Vector3 m_toolOffsetLocal = new Vector3(0f, 0.05f, 0f);

        [Tooltip("工具接近轴（末端本地）。完整朝向约束时使用；「末端保持向下」改用第 5 轴连杆方向")]
        [InspectorLabel("工具接近轴")]
        [SerializeField] private Vector3 m_toolApproachLocal = Vector3.up;

        [Header("末端姿态约束")]
        [Tooltip("夹爪模式：将 J5→TCP 方向对齐世界下方并全程保持。位置按腕心求解；第 6 轴扭转与朝下同帧求解。")]
        [InspectorLabel("末端保持向下")]
        [SerializeField] private bool m_keepTipDown = true;

        [Tooltip("向下约束的目标方向（世界空间），默认 Vector3.down")]
        [InspectorLabel("向下方向（世界）")]
        [SerializeField] private Vector3 m_downWorldAxis = Vector3.down;

        [Header("IK 目标")]
        [InspectorLabel("IK 跟随目标")]
        [SerializeField] private Transform m_ikTarget;

        [Tooltip("Play 模式每帧 IK 解算到目标点（无 Timeline 时用于调试）")]
        [InspectorLabel("运行时 IK 跟随")]
        [SerializeField] private bool m_followIkTargetInPlayMode;

        [SerializeField, HideInInspector] private int[] m_linkMeasureJointIds;

        [Header("新增 Clip 默认值（写入 Clip，可再改）")]
        [Tooltip("最大角速度（度/秒）")]
        [InspectorLabel("关节角速度")]
        [SerializeField] private float m_jointSpeed = 60f;

        [Tooltip("角加速度（度/秒²），减速与加速相同")]
        [InspectorLabel("关节角加速度")]
        [SerializeField] private float m_jointAcceleration = 90f;

        [Header("IK（逆解）")]
        [InspectorLabel("IK 迭代次数")]
        [SerializeField] private int m_ikIterations = 24;
        [InspectorLabel("IK 位置容差")]
        [SerializeField] private float m_ikPositionTolerance = 0.01f;

        [Header("Home（无前序 / 未指定起点时的回退关节角）")]
        [InspectorLabel("Home 关节角")]
        [SerializeField] private float[] m_homeAngles = new float[DefaultJointCount];
        [InspectorLabel("已设置 Home")]
        [SerializeField] private bool m_hasHome;

        [InspectorLabel("Rest 本地旋转")]
        [SerializeField] private Quaternion[] m_restLocalRotations = new Quaternion[DefaultJointCount];
        [InspectorLabel("已捕获 Rest")]
        [SerializeField] private bool m_hasRestPose;

        [Header("调试")]
        [Tooltip("开启后 Timeline 预览禁用 IK 缓存，每帧完整重解并输出 [RobotArm DBG] 日志")]
        [InspectorLabel("强制播放（禁缓存）")]
        [SerializeField] private bool m_debugForcePlayback;

        /// <summary>当前轴数（定死为 <see cref="JointCountFixed"/>；配置异常时可能暂为 0）。</summary>
        public int JointCount =>
            m_joints != null && m_joints.Length == JointCountFixed ? JointCountFixed : 0;

        /// <summary>臂部关节数（J1–J3，用于位置 IK）。</summary>
        public int ArmJointCount => ArmJointCountFixed;

        /// <summary>腕部起始关节索引（含），六轴为 J4。</summary>
        public int WristStartIndex => ArmJointCountFixed;

        /// <summary>
        /// 末端朝下约束所参照的关节（0-based）。
    /// 六轴为第 5 轴（index 4）：以其连杆/输出方向对齐世界下方，第 6 轴仅扭转。
    /// </summary>
        public int TipDownJointIndex => TipDownJointIndexFixed;

        /// <summary>朝下约束用的本地轴：第 5 轴连杆方向（自动测量），而非末端 ToolApproachLocal。</summary>
        public Vector3 TipDownAxisLocal => GetLinkDirectionLocal(TipDownJointIndex);

        public JointSettings[] Joints => m_joints;
        public bool AutoMeasureLinks => m_autoMeasureLinks;
        public Transform ToolTip => m_toolTip;
        public Vector3 ToolOffsetLocal => m_toolOffsetLocal;

        public Vector3 ToolApproachLocal =>
            m_toolApproachLocal.sqrMagnitude > 1e-8f
                ? m_toolApproachLocal.normalized
                : Vector3.up;

        public bool KeepTipDown => m_keepTipDown;
        public Vector3 DownWorldAxis =>
            m_downWorldAxis.sqrMagnitude > 1e-8f ? m_downWorldAxis.normalized : Vector3.down;
        public Transform IkTarget => m_ikTarget;
        public bool FollowIkTargetInPlayMode => m_followIkTargetInPlayMode;

        public float JointSpeed => m_jointSpeed;
        public float JointAcceleration => m_jointAcceleration;
        public int IkIterations => Mathf.Max(1, m_ikIterations);
        public float IkPositionTolerance => Mathf.Max(1e-4f, m_ikPositionTolerance);
        public bool HasHome => m_hasHome;
        public float[] HomeAngles => m_homeAngles;
        public bool HasRestPose => m_hasRestPose;

        /// <summary>将本组件默认值写入 Clip（新增 Clip 时调用）。</summary>
        public void ApplyClipDefaults(RobotArmClipData data)
        {
            if (data == null)
                return;

            data.JointSpeed = m_jointSpeed;
            data.JointAcceleration = m_jointAcceleration;
        }
        public bool DebugForcePlayback => m_debugForcePlayback;

        /// <summary>驱动关节：显式指定 &gt; 轴点自身。</summary>
        public static Transform ResolveDriveJoint(JointSettings joint)
        {
            if (joint == null)
                return null;
            if (joint.DriveJoint != null)
                return joint.DriveJoint;
            return joint.AxisPoint;
        }

        public Transform GetAxisPoint(int index)
        {
            if (m_joints == null || index < 0 || index >= m_joints.Length)
                return null;
            return m_joints[index]?.AxisPoint;
        }

        public Transform GetDriveJoint(int index)
        {
            if (m_joints == null || index < 0 || index >= m_joints.Length)
                return null;
            return ResolveDriveJoint(m_joints[index]);
        }

        public Transform GetLinkChild(int index)
        {
            if (index + 1 < JointCount)
                return GetAxisPoint(index + 1);
            return m_toolTip != null ? m_toolTip : ResolveToolTip();
        }

        public Transform GetJoint(int index) => GetDriveJoint(index);

        public Vector3 GetAxisLocal(int index)
        {
            if (!TryGetJointSettings(index, out JointSettings s))
                return Vector3.up;
            return s.AxisLocal.sqrMagnitude > 1e-8f ? s.AxisLocal.normalized : Vector3.up;
        }

        public float GetLinkLength(int index)
        {
            return TryGetJointSettings(index, out JointSettings s) ? Mathf.Max(0f, s.LinkLength) : 0f;
        }

        public Vector3 GetLinkDirectionLocal(int index)
        {
            if (!TryGetJointSettings(index, out JointSettings s))
                return Vector3.up;
            return s.LinkDirectionLocal.sqrMagnitude > 1e-8f
                ? s.LinkDirectionLocal.normalized
                : Vector3.up;
        }

        public void GetJointLimits(int index, out float min, out float max)
        {
            if (!TryGetJointSettings(index, out JointSettings s))
            {
                min = -180f;
                max = 180f;
                return;
            }

            min = Mathf.Min(s.MinAngle, s.MaxAngle);
            max = Mathf.Max(s.MinAngle, s.MaxAngle);
        }

        public Quaternion GetRestLocalRotation(int index)
        {
            if (m_hasRestPose &&
                m_restLocalRotations != null &&
                index >= 0 &&
                index < m_restLocalRotations.Length)
                return m_restLocalRotations[index];
            return Quaternion.identity;
        }

        public bool TryGetJointSettings(int index, out JointSettings settings)
        {
            settings = null;
            if (m_joints == null || index < 0 || index >= m_joints.Length)
                return false;
            settings = m_joints[index];
            return settings != null;
        }

        /// <summary>读取当前已应用到关节上的角度（相对 Rest）。</summary>
        public void ReadCurrentAngles(float[] angles)
        {
            EnsureAngleBuffer(angles);
            int n = JointCount;
            for (int i = 0; i < n; i++)
            {
                Transform joint = GetJoint(i);
                if (joint == null)
                {
                    angles[i] = m_hasHome && m_homeAngles != null && i < m_homeAngles.Length
                        ? m_homeAngles[i]
                        : 0f;
                    continue;
                }

                Quaternion rest = GetRestLocalRotation(i);
                Quaternion delta = Quaternion.Inverse(rest) * joint.localRotation;
                Vector3 axis = GetAxisLocal(i);
                delta.ToAngleAxis(out float angle, out Vector3 rotAxis);
                if (rotAxis.sqrMagnitude < 1e-8f)
                {
                    angles[i] = 0f;
                    continue;
                }

                if (Vector3.Dot(rotAxis.normalized, axis) < 0f)
                    angle = -angle;
                angles[i] = ClampAngle(i, NormalizeSignedAngle(angle));
            }
        }

        public void ApplyAngles(float[] angles)
        {
            int n = JointCount;
            if (angles == null || angles.Length < n || n != JointCountFixed)
                return;

            EnsureRestPose();
            for (int i = 0; i < n; i++)
            {
                Transform joint = GetJoint(i);
                if (joint == null)
                    continue;

                float angle = ClampAngle(i, angles[i]);
                joint.localRotation =
                    GetRestLocalRotation(i) * Quaternion.AngleAxis(angle, GetAxisLocal(i));
            }
        }

        public float ClampAngle(int index, float angle)
        {
            GetJointLimits(index, out float min, out float max);
            return Mathf.Clamp(angle, min, max);
        }

        [ContextMenu("从当前姿态捕获 Rest")]
        public void CaptureRestPoseFromCurrent()
        {
            EnsureJointArray();
            int n = JointCountFixed;

            if (m_restLocalRotations == null || m_restLocalRotations.Length != n)
                m_restLocalRotations = new Quaternion[n];

            for (int i = 0; i < n; i++)
            {
                Transform joint = GetJoint(i);
                m_restLocalRotations[i] = joint != null ? joint.localRotation : Quaternion.identity;
            }

            m_hasRestPose = true;
        }

        [ContextMenu("从当前姿态捕获 Home 关节角")]
        public void CaptureHomeFromCurrent()
        {
            EnsureRestPose();
            EnsureJointArray();
            int n = JointCountFixed;
            if (m_homeAngles == null || m_homeAngles.Length != n)
                m_homeAngles = new float[n];
            ReadCurrentAngles(m_homeAngles);
            m_hasHome = true;
        }

        [ContextMenu("打印当前姿态")]
        public void LogCurrentPose()
        {
            Debug.Log(RobotArmIk.BuildPoseDebugReport(this), this);
        }

        /// <summary>
        /// 由轴点父子链测量连杆：子轴点 / 末端相对驱动关节的世界偏移即连杆向量。
    /// </summary>
        [ContextMenu("从轴点测量连杆长度")]
        public int MeasureLinkLengthsFromHierarchy()
        {
            EnsureJointArray();
            int n = JointCount;
            int measured = 0;

            for (int i = 0; i < n; i++)
            {
                Transform drive = GetDriveJoint(i);
                Transform linkChild = GetLinkChild(i);
                if (drive == null || linkChild == null)
                    continue;

                if (ApplyLinkFromWorldDelta(i, drive, linkChild.position - drive.position))
                {
                    if (i == n - 1 && m_toolTip != null)
                        m_toolOffsetLocal = Vector3.zero;
                    measured++;
                }
            }

            StoreLinkMeasureFingerprint();
            return measured;
        }

        /// <summary>驱动关节是否已全部指定（Timeline / IK 可运行）。</summary>
        public bool AreJointsAssigned()
        {
            int n = JointCount;
            if (n != JointCountFixed)
                return false;
            for (int i = 0; i < n; i++)
            {
                if (GetJoint(i) == null)
                    return false;
            }

            return true;
        }

        /// <summary>轴点与末端是否已全部指定（可测连杆、可校验父子链）。</summary>
        public bool AreAxisPointsAssigned()
        {
            int n = JointCount;
            if (n != JointCountFixed || m_toolTip == null)
                return false;
            for (int i = 0; i < n; i++)
            {
                if (GetAxisPoint(i) == null)
                    return false;
            }

            return true;
        }

        public bool Validate(out List<string> errors, out List<string> warnings)
        {
            errors = new List<string>();
            warnings = new List<string>();

            EnsureJointArray();
            int n = JointCount;
            if (n != JointCountFixed)
                errors.Add($"轴数须为 {JointCountFixed}，当前 {n}。");

            var seenPoints = new HashSet<int>();
            Transform prevAxis = null;

            for (int i = 0; i < n; i++)
            {
                var joint = m_joints[i];
                if (joint == null)
                {
                    errors.Add($"轴 {i + 1} 定义为空。");
                    continue;
                }

                Transform point = joint.AxisPoint;
                if (point == null)
                {
                    errors.Add($"轴 {i + 1}（J{i + 1}）未指定轴点。");
                    continue;
                }

                if (!seenPoints.Add(point.GetInstanceID()))
                    errors.Add($"轴 {i + 1} 轴点重复：{point.name}");

                if (ResolveDriveJoint(joint) == null)
                    errors.Add($"轴 {i + 1} 无法解析驱动关节。");

                if (i == 0)
                {
                    if (point.parent != transform)
                        errors.Add($"J1 轴点须为机械臂根「{name}」的直接子对象。");
                    if (point.localPosition.sqrMagnitude > 1e-6f)
                        warnings.Add(
                            $"J1 轴点本地坐标非零 ({point.localPosition})，FK 以根节点为基座，建议置于 (0,0,0)。");
                }
                else if (prevAxis != null && point.parent != prevAxis)
                {
                    errors.Add(
                        $"J{i + 1} 轴点「{point.name}」须为 J{i} 轴点「{prevAxis.name}」的直接子对象。");
                }

                Transform linkChild = GetLinkChild(i);
                if (linkChild == null)
                    errors.Add(i < n - 1
                        ? $"J{i + 1} 缺少子轴点 J{i + 2}。"
                        : "末轴缺少末端轴点子对象。");
                else if (linkChild.parent != point)
                    errors.Add(
                        $"J{i + 1} 的连杆子节点「{linkChild.name}」须直接挂在轴点「{point.name}」下。");

                if (joint.AxisLocal.sqrMagnitude < 1e-8f)
                    warnings.Add($"轴 {i + 1} 旋转轴接近零，将使用 Vector3.up。");

                if (joint.LinkLength < 1e-6f)
                    warnings.Add($"轴 {i + 1} 连杆未测量，请执行「测量连杆」。");

                if (HasNonTransformComponents(point))
                    warnings.Add($"轴 {i + 1} 轴点「{point.name}」含 Mesh/Collider，建议仅保留 Transform。");

                if (Quaternion.Angle(point.localRotation, Quaternion.identity) > 0.5f)
                    warnings.Add(
                        $"J{i + 1} 轴点含模型导入旋转（{point.localEulerAngles}），须捕获 Rest。");

                prevAxis = point;
            }

            if (!m_hasRestPose)
                warnings.Add("未捕获 Rest，请执行「捕获 Rest / Home」。");

            if (m_toolTip == null)
                errors.Add("未指定末端轴点（末轴的子对象）。");
            else if (n > 0 && m_toolTip.parent != GetAxisPoint(n - 1))
                errors.Add("末端轴点须为最后一轴轴点的直接子对象。");

            if (m_ikTarget == null)
                warnings.Add("未配置 IK 跟随目标。");

            return errors.Count == 0;
        }

        /// <summary>运行时 / 预览前：测连杆并确保 Rest/Home。</summary>
        public bool EnsureRuntimeSetup(bool captureRest = true)
        {
            if (!AreAxisPointsAssigned() && !AreJointsAssigned())
                return false;

            if (AreAxisPointsAssigned())
                MeasureLinkLengthsFromHierarchy();

            if (!AreJointsAssigned())
                return false;

            if (captureRest)
            {
                CaptureRestPoseFromCurrent();
                CaptureHomeFromCurrent();
            }
            else
            {
                EnsureRestPose();
            }

            return true;
        }

        public bool TrySolveIkToTarget()
        {
            if (m_ikTarget == null || !AreJointsAssigned())
                return false;

            EnsureRestPose();
            int n = JointCount;
            var angles = new float[n];
            ReadCurrentAngles(angles);

            bool ok = RobotArmIk.SolveToTarget(
                this,
                m_ikTarget.position,
                angles,
                angles,
                m_keepTipDown,
                downWorldAxis: DownWorldAxis);
            ApplyAngles(angles);
            return ok;
        }

        public void EnsureRestPose()
        {
            if (!m_hasRestPose)
                CaptureRestPoseFromCurrent();
        }

        public void CopyHomeAnglesTo(float[] dst)
        {
            EnsureAngleBuffer(dst);
            int n = JointCount;
            if (m_hasHome && m_homeAngles != null)
            {
                for (int i = 0; i < n; i++)
                    dst[i] = i < m_homeAngles.Length ? m_homeAngles[i] : 0f;
                return;
            }

            // 未配置 Home：确定性全零，不读当前关节角（避免 scrub 不确定）
            for (int i = 0; i < n; i++)
                dst[i] = 0f;
        }

        /// <summary>将各关节恢复到 Home 初始角（相对 Rest）。</summary>
        public void ApplyHomePose()
        {
            EnsureJointArray();
            if (JointCount != JointCountFixed)
                return;

            EnsureRestPose();
            var angles = new float[JointCountFixed];
            CopyHomeAnglesTo(angles);
            ApplyAngles(angles);
        }

        public void EnsureAngleBuffer(float[] angles)
        {
            if (angles == null || angles.Length < JointCountFixed)
                throw new ArgumentException($"angles 长度须 >= {JointCountFixed}", nameof(angles));
        }

        public static float NormalizeSignedAngle(float angle)
        {
            angle %= 360f;
            if (angle > 180f) angle -= 360f;
            if (angle < -180f) angle += 360f;
            return angle;
        }

        /// <summary>
        /// 轴点引用齐全且相对上次测量有变更时，自动测连杆（编辑器 OnValidate 调用）。
    /// </summary>
        public bool TryAutoMeasureLinksIfNeeded()
        {
            if (!m_autoMeasureLinks || (!AreAxisPointsAssigned() && !AreJointsAssigned()))
                return false;

            bool firstTime = m_linkMeasureJointIds == null || m_linkMeasureJointIds.Length == 0;
            if (!firstTime && !LinkMeasureFingerprintChanged())
                return false;

            if (!m_hasRestPose)
                CaptureRestPoseFromCurrent();

            return MeasureLinkLengthsFromHierarchy() > 0;
        }

        private void Awake()
        {
            if (Application.isPlaying)
                EnsureRuntimeSetup(captureRest: false);
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying || !m_followIkTargetInPlayMode || m_ikTarget == null)
                return;

            TrySolveIkToTarget();
        }

        private void EnsureJointArray()
        {
            if (m_joints == null || m_joints.Length != JointCountFixed)
            {
                var resized = new JointSettings[JointCountFixed];
                for (int i = 0; i < JointCountFixed; i++)
                {
                    resized[i] = m_joints != null && i < m_joints.Length && m_joints[i] != null
                        ? m_joints[i]
                        : CreateDefaultJoint(i);
                }

                m_joints = resized;
            }

            for (int i = 0; i < m_joints.Length; i++)
            {
                if (m_joints[i] == null)
                    m_joints[i] = CreateDefaultJoint(i);
            }

            SyncAuxiliaryBuffers();
        }

        private void SyncAuxiliaryBuffers()
        {
            int n = JointCountFixed;
            if (m_homeAngles == null || m_homeAngles.Length != n)
            {
                var next = new float[n];
                if (m_homeAngles != null)
                {
                    int copy = Mathf.Min(n, m_homeAngles.Length);
                    for (int i = 0; i < copy; i++)
                        next[i] = m_homeAngles[i];
                }

                m_homeAngles = next;
            }

            if (m_restLocalRotations == null || m_restLocalRotations.Length != n)
            {
                var next = new Quaternion[n];
                for (int i = 0; i < n; i++)
                    next[i] = Quaternion.identity;
                if (m_restLocalRotations != null)
                {
                    int copy = Mathf.Min(n, m_restLocalRotations.Length);
                    for (int i = 0; i < copy; i++)
                        next[i] = m_restLocalRotations[i];
                }

                m_restLocalRotations = next;
            }
        }

        private static JointSettings[] CreateDefaultJoints()
        {
            var joints = new JointSettings[JointCountFixed];
            for (int i = 0; i < JointCountFixed; i++)
                joints[i] = CreateDefaultJoint(i);
            return joints;
        }

        private static JointSettings CreateDefaultJoint(int index)
        {
            Vector3 axis = index switch
            {
                0 => Vector3.up,
                1 => Vector3.forward,
                2 => Vector3.forward,
                3 => Vector3.up,
                4 => Vector3.forward,
                _ => Vector3.up
            };
            float length = index switch
            {
                0 => 0.35f,
                1 => 0.55f,
                2 => 0.45f,
                3 => 0.12f,
                4 => 0.12f,
                _ => 0.08f
            };
            return new JointSettings
            {
                AxisLocal = axis,
                LinkLength = length,
                LinkDirectionLocal = Vector3.up,
                MinAngle = -175f,
                MaxAngle = 175f
            };
        }

        private bool ApplyLinkFromWorldDelta(int jointIndex, Transform joint, Vector3 worldDelta)
        {
            float mag = worldDelta.magnitude;
            if (mag < 1e-8f || joint == null || m_joints == null ||
                jointIndex < 0 || jointIndex >= m_joints.Length || m_joints[jointIndex] == null)
                return false;

            m_joints[jointIndex].LinkLength = mag;
            Vector3 local = joint.InverseTransformDirection(worldDelta);
            if (local.sqrMagnitude > 1e-8f)
                m_joints[jointIndex].LinkDirectionLocal = local.normalized;
            return true;
        }

        private Transform ResolveToolTip()
        {
            if (m_toolTip != null)
                return m_toolTip;

            Transform last = GetAxisPoint(JointCount - 1) ?? GetJoint(JointCount - 1);
            if (last == null)
                return null;

            return last.Find("Tip") ?? last.Find("Tip_AxisPoint");
        }

        private int[] BuildLinkMeasureFingerprint()
        {
            int n = JointCount;
            var ids = new int[n * 2 + 1];
            for (int i = 0; i < n; i++)
            {
                Transform axis = GetAxisPoint(i);
                Transform drive = GetDriveJoint(i);
                ids[i * 2] = axis != null ? axis.GetInstanceID() : 0;
                ids[i * 2 + 1] = drive != null ? drive.GetInstanceID() : 0;
            }

            Transform tip = ResolveToolTip();
            ids[n * 2] = tip != null ? tip.GetInstanceID() : 0;
            return ids;
        }

        private bool LinkMeasureFingerprintChanged()
        {
            int[] current = BuildLinkMeasureFingerprint();
            if (m_linkMeasureJointIds == null || m_linkMeasureJointIds.Length != current.Length)
                return true;
            for (int i = 0; i < current.Length; i++)
            {
                if (m_linkMeasureJointIds[i] != current[i])
                    return true;
            }

            return false;
        }

        private void StoreLinkMeasureFingerprint()
        {
            m_linkMeasureJointIds = BuildLinkMeasureFingerprint();
        }

        private static bool HasNonTransformComponents(Transform t)
        {
            if (t == null)
                return false;
            var go = t.gameObject;
            return go.GetComponent<Renderer>() != null ||
                   go.GetComponent<Collider>() != null ||
                   go.GetComponent<MeshFilter>() != null;
        }

#if UNITY_EDITOR
        private void Reset()
        {
            EnsureJointArray();
            CaptureRestPoseFromCurrent();
            CaptureHomeFromCurrent();
        }

        private void OnValidate()
        {
            EnsureJointArray();
            m_jointSpeed = Mathf.Max(0.01f, m_jointSpeed);
            m_jointAcceleration = Mathf.Max(0.01f, m_jointAcceleration);
            m_ikIterations = Mathf.Max(1, m_ikIterations);
            TryAutoMeasureLinksIfNeeded();
        }

        private void OnDrawGizmosSelected()
        {
            int n = JointCount;
            if (m_joints != null)
            {
                Gizmos.color = new Color(0.3f, 1f, 0.5f, 0.95f);
                for (int i = 0; i < n; i++)
                {
                    Transform p = GetAxisPoint(i);
                    if (p == null)
                        continue;

                    Gizmos.DrawWireSphere(p.position, 0.025f);

                    Transform next = GetLinkChild(i);
                    if (next != null)
                        Gizmos.DrawLine(p.position, next.position);

                    Transform drive = GetDriveJoint(i);
                    if (drive != null)
                    {
                        Gizmos.color = Color.cyan;
                        Gizmos.DrawRay(p.position, drive.TransformDirection(GetAxisLocal(i)) * 0.08f);
                        Gizmos.color = new Color(0.3f, 1f, 0.5f, 0.95f);
                    }
                }

                if (m_toolTip != null)
                {
                    Gizmos.color = Color.yellow;
                    Gizmos.DrawWireSphere(m_toolTip.position, 0.035f);
                }

                if (m_ikTarget != null)
                {
                    Gizmos.color = new Color(1f, 0.45f, 0.1f, 0.9f);
                    Gizmos.DrawWireSphere(m_ikTarget.position, 0.04f);
                    Transform last = m_toolTip != null
                        ? m_toolTip
                        : (n > 0 ? GetAxisPoint(n - 1) : transform);
                    if (last != null)
                        Gizmos.DrawLine(last.position, m_ikTarget.position);
                }

                if (m_keepTipDown)
                {
                    Gizmos.color = new Color(0.35f, 0.55f, 1f, 0.85f);
                    Vector3 down = DownWorldAxis * 0.18f;
                    Transform tipDownJoint = GetAxisPoint(TipDownJointIndex);
                    Vector3 origin = tipDownJoint != null
                        ? tipDownJoint.position
                        : (m_toolTip != null ? m_toolTip.position : transform.position);
                    Gizmos.DrawRay(origin, down);
                    Gizmos.DrawWireSphere(origin + down, 0.02f);

                    Vector3 actual = tipDownJoint != null && m_toolTip != null
                        ? m_toolTip.position - origin
                        : Vector3.zero;
                    if (actual.sqrMagnitude > 1e-8f)
                    {
                        actual.Normalize();
                        Gizmos.color = Vector3.Dot(actual, DownWorldAxis) >= RobotArmIk.TipDownAcceptDot
                            ? new Color(0.2f, 1f, 0.4f, 0.95f)
                            : new Color(1f, 0.25f, 0.2f, 0.95f);
                        Gizmos.DrawRay(origin, actual * 0.18f);
                    }
                }
            }

            if (n != JointCountFixed)
                return;

            var angles = new float[n];
            if (m_hasHome && m_homeAngles != null)
            {
                for (int i = 0; i < n; i++)
                    angles[i] = i < m_homeAngles.Length ? m_homeAngles[i] : 0f;
            }
            else
            {
                ReadCurrentAngles(angles);
            }

            RobotArmIk.ForwardPositions(this, angles, out Vector3[] jointPos, out Vector3 tip);
            if (jointPos == null)
                return;

            Gizmos.color = new Color(0.2f, 0.85f, 0.95f, 0.35f);
            Vector3 prev = transform.position;
            for (int i = 0; i < jointPos.Length; i++)
            {
                Gizmos.DrawLine(prev, jointPos[i]);
                Gizmos.DrawSphere(jointPos[i], 0.02f);
                prev = jointPos[i];
            }

            Gizmos.color = new Color(1f, 0.92f, 0.2f, 0.45f);
            Gizmos.DrawLine(prev, tip);
            Gizmos.DrawWireSphere(tip, 0.03f);
        }
#endif
    }
}
