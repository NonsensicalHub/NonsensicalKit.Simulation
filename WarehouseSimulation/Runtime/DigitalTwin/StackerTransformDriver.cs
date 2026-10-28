using System;
using NaughtyAttributes;
using NonsensicalKit.DigitalTwin;
using UnityEngine;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Runtime
{
    /// <summary>堆垛机自由度掩码：只允许掩码内的轴沿自身滑轨移动。</summary>
    [Flags]
    public enum StackerAxisMask
    {
        None = 0,
        Row = 1 << 0,
        Level = 1 << 1,
        Fork = 1 << 2,
        Carriage = Row | Level,
        All = Row | Level | Fork,
    }

    /// <summary>
    /// 堆垛机三轴滑轨。节点按父子链依附：走行是父，层向挂在走行下，货叉挂在层向下。
    /// <see cref="SetChainScalars"/> 只给每轴一个相对静止位的标量，沿自己的 Dir 滑动。
    /// 子轴的局部坐标不包含父轴行程，父轴一动，子轴跟着走。不移动堆垛机根节点。
    /// </summary>
    public sealed class StackerTransformDriver : MonoBehaviour
    {
        [Header("三轴节点（显式优先）")]
        [Label("排向轴")]
        [SerializeField]
        private Transform m_rowAxis;

        [Label("层向轴")]
        [SerializeField]
        private Transform m_levelAxis;

        [Label("货叉轴（插）")]
        [SerializeField]
        private Transform m_forkAxis;

        [Header("各轴运动的局部方向（只改 localPosition 的这一分量）")]
        [Label("排向 Dir")]
        [SerializeField]
        private JointDirType m_rowDir = JointDirType.X;

        [Label("层向 Dir")]
        [SerializeField]
        private JointDirType m_levelDir = JointDirType.Z;

        [Label("货叉 Dir")]
        [SerializeField]
        private JointDirType m_forkDir = JointDirType.Y;

        [Header("对齐参考点")]
        [Label("对齐参考（空=货叉轴）")]
        [Tooltip("应对准货位中心的点，须是对应轴节点自身或其子节点，轴滑动才会带动它。空则用货叉轴。")]
        [SerializeField]
        private Transform m_alignReference;

        [Header("可选：从旧 JointController 自动填节点")]
        [Label("旧 JointController（仅迁移用）")]
        [Tooltip("三轴引用为空时，按 Joints[0]=排、[1]=层、[2]=插 取节点、Dir 和 ZeroState。运行时不调用 ChangeState。")]
        [SerializeField]
        private JointController m_legacyJointController;

        private bool _legacyTried;
        private bool _restReady;
        private Vector3 _rowRest;
        private Vector3 _levelRest;
        private Vector3 _forkRest;

        private Vector3 _rowFromLocal;
        private Vector3 _rowToLocal;
        private Vector3 _levelFromLocal;
        private Vector3 _levelToLocal;
        private Vector3 _forkFromLocal;
        private Vector3 _forkToLocal;
        private bool _rowFree;
        private bool _levelFree;
        private bool _forkFree;
        private float _moveDuration;
        private float _moveElapsed;
        private bool _moving;

        public Transform RowAxis => m_rowAxis;
        public Transform LevelAxis => m_levelAxis;
        public Transform ForkAxis => m_forkAxis;
        public JointDirType RowDir => m_rowDir;
        public JointDirType LevelDir => m_levelDir;
        public JointDirType ForkDir => m_forkDir;

        public Transform AlignReference
        {
            get
            {
                if (m_alignReference != null)
                {
                    return m_alignReference;
                }

                return m_forkAxis != null ? m_forkAxis : transform;
            }
        }

        public bool IsReady
        {
            get
            {
                EnsureNodesFromLegacy();
                return m_rowAxis != null && m_levelAxis != null && m_forkAxis != null;
            }
        }

        /// <summary>最近一次 MoveAlignToWorld 算出的目标 local 与 delta（供日志）。</summary>
        public Vector3 LastRowTargetLocal { get; private set; }
        public Vector3 LastLevelTargetLocal { get; private set; }
        public Vector3 LastForkTargetLocal { get; private set; }
        public Vector3 LastRowDeltaLocal { get; private set; }
        public Vector3 LastLevelDeltaLocal { get; private set; }
        public Vector3 LastForkDeltaLocal { get; private set; }
        public Vector3 LastTargetWorld { get; private set; }
        public StackerAxisMask LastMask { get; private set; }

        private void Awake()
        {
            EnsureNodesFromLegacy();
            EnsureRest();
        }

        /// <summary>回放可把场景里旧 JointController 交给驱动做节点 fallback。</summary>
        public void SetLegacyJointController(JointController controller)
        {
            if (controller == null)
            {
                return;
            }

            m_legacyJointController = controller;
            _legacyTried = false;
            EnsureNodesFromLegacy();
            EnsureRest();
        }

        public void EnsureNodesFromLegacy()
        {
            if (_legacyTried
                && m_rowAxis != null
                && m_levelAxis != null
                && m_forkAxis != null
                && m_legacyJointController == null)
            {
                return;
            }

            _legacyTried = true;
            if (m_legacyJointController == null)
            {
                m_legacyJointController = GetComponent<JointController>();
                if (m_legacyJointController == null)
                {
                    m_legacyJointController = GetComponentInChildren<JointController>(true);
                }
            }

            if (m_legacyJointController == null
                || m_legacyJointController.Joints == null
                || m_legacyJointController.Joints.Length < 3)
            {
                return;
            }

            var joints = m_legacyJointController.Joints;
            if (m_rowAxis == null && joints[0] != null)
            {
                m_rowAxis = joints[0].JointsNode;
            }

            if (m_levelAxis == null && joints[1] != null)
            {
                m_levelAxis = joints[1].JointsNode;
            }

            if (m_forkAxis == null && joints[2] != null)
            {
                m_forkAxis = joints[2].JointsNode;
            }

            if (joints[0] != null)
            {
                m_rowDir = joints[0].DirType;
            }

            if (joints[1] != null)
            {
                m_levelDir = joints[1].DirType;
            }

            if (joints[2] != null)
            {
                m_forkDir = joints[2].DirType;
            }
        }

        /// <summary>
        /// 把对齐参考点挪到 targetWorld。掩码决定哪些轴参与。
        /// 先走行、再层、再叉。每轴只补自己方向上的误差，货叉 Z 不跟着层向的垂直位移写到机体上。
        /// duration≤0 则瞬间到位。
        /// </summary>
        public bool MoveAlignToWorld(Vector3 targetWorld, float duration) =>
            MoveAlignToWorld(targetWorld, duration, StackerAxisMask.All);

        public bool MoveAlignToWorld(Vector3 targetWorld, float duration, StackerAxisMask mask)
        {
            if (mask == StackerAxisMask.None)
            {
                mask = StackerAxisMask.All;
            }

            if (!IsReady || !TryComputeLocalTargets(targetWorld, mask, out var rowTarget, out var levelTarget, out var forkTarget))
            {
                return false;
            }

            LastTargetWorld = targetWorld;
            LastMask = mask;
            LastRowTargetLocal = rowTarget;
            LastLevelTargetLocal = levelTarget;
            LastForkTargetLocal = forkTarget;
            LastRowDeltaLocal = rowTarget - m_rowAxis.localPosition;
            LastLevelDeltaLocal = levelTarget - m_levelAxis.localPosition;
            LastForkDeltaLocal = forkTarget - m_forkAxis.localPosition;

            _rowFromLocal = m_rowAxis.localPosition;
            _levelFromLocal = m_levelAxis.localPosition;
            _forkFromLocal = m_forkAxis.localPosition;
            _rowToLocal = rowTarget;
            _levelToLocal = levelTarget;
            _forkToLocal = forkTarget;
            _rowFree = (_rowToLocal - _rowFromLocal).sqrMagnitude > 1e-12f;
            _levelFree = (_levelToLocal - _levelFromLocal).sqrMagnitude > 1e-12f;
            _forkFree = (_forkToLocal - _forkFromLocal).sqrMagnitude > 1e-12f;

            if (duration <= 1e-6f)
            {
                ApplyLocals(_rowToLocal, _levelToLocal, _forkToLocal);
                _moving = false;
                return true;
            }

            _moveDuration = duration;
            _moveElapsed = 0f;
            _moving = true;
            return true;
        }

        /// <summary>停在当前位置（打断插值）。</summary>
        public void StopMotion()
        {
            _moving = false;
        }

        /// <summary>立刻对齐到 targetWorld。</summary>
        public bool SnapAlignToWorld(Vector3 targetWorld) =>
            MoveAlignToWorld(targetWorld, 0f, StackerAxisMask.All);

        public bool SnapAlignToWorld(Vector3 targetWorld, StackerAxisMask mask) =>
            MoveAlignToWorld(targetWorld, 0f, mask);

        public Vector3 GetAlignWorldPosition() => AlignReference.position;

        /// <summary>
        /// 按父子链写入三轴标量。0 表示该轴停在模型静止位。
        /// 只改各轴自己的 Dir 分量，货叉标量是相对层向轴的伸缩，不是世界坐标。
        /// </summary>
        public void SetChainScalars(float row, float level, float fork) =>
            SetChainScalars(row, level, fork, StackerAxisMask.All);

        /// <summary>
        /// 按掩码写入标量。走行/升降时不要写货叉，货叉才会只跟着父轴走；
        /// 取放时只写货叉，避免把列向再灌进排/层。
        /// </summary>
        public void SetChainScalars(float row, float level, float fork, StackerAxisMask mask)
        {
            if (!IsReady)
            {
                return;
            }

            if (mask == StackerAxisMask.None)
            {
                mask = StackerAxisMask.All;
            }

            EnsureRest();
            _moving = false;

            var rowLocal = (mask & StackerAxisMask.Row) != 0
                ? OnRail(_rowRest, m_rowDir, row)
                : m_rowAxis.localPosition;
            var levelLocal = (mask & StackerAxisMask.Level) != 0
                ? OnRail(_levelRest, m_levelDir, level)
                : m_levelAxis.localPosition;
            var forkLocal = (mask & StackerAxisMask.Fork) != 0
                ? OnRail(_forkRest, m_forkDir, fork)
                : m_forkAxis.localPosition;
            ApplyLocals(rowLocal, levelLocal, forkLocal);
        }

        public float GetRowScalar()
        {
            EnsureRest();
            return ReadScalar(m_rowAxis, _rowRest, m_rowDir);
        }

        public float GetLevelScalar()
        {
            EnsureRest();
            return ReadScalar(m_levelAxis, _levelRest, m_levelDir);
        }

        /// <summary>货叉相对静止位的伸缩标量。0 表示收回。</summary>
        public float GetForkScalar()
        {
            EnsureRest();
            return ReadScalar(m_forkAxis, _forkRest, m_forkDir);
        }

        /// <summary>该轴标量 +1 时，节点在世界上的位移（含父级缩放和旋转）。</summary>
        public Vector3 GetAxisWorldStep(StackerAxisMask axis)
        {
            EnsureRest();
            Transform node;
            JointDirType dir;
            if (axis == StackerAxisMask.Level)
            {
                node = m_levelAxis;
                dir = m_levelDir;
            }
            else if (axis == StackerAxisMask.Fork)
            {
                node = m_forkAxis;
                dir = m_forkDir;
            }
            else
            {
                node = m_rowAxis;
                dir = m_rowDir;
            }

            if (node == null || !TryLocalBasis(dir, out var basis))
            {
                return Vector3.zero;
            }

            var parent = node.parent;
            return parent != null ? parent.TransformVector(basis) : basis;
        }

        /// <summary>
        /// 只改货叉伸缩。不改排、层。进行中的插值也不会把货叉写回去。
        /// </summary>
        public void SetForkScalar(float scalar)
        {
            if (!IsReady)
            {
                return;
            }

            EnsureRest();
            var local = OnRail(_forkRest, m_forkDir, scalar);
            _forkFromLocal = local;
            _forkToLocal = local;
            _forkFree = false;
            m_forkAxis.localPosition = local;
        }

        /// <summary>货叉关节每增加 1 个标量，对齐点在世界上的位移。</summary>
        public bool TryGetForkAxisWorldVector(out Vector3 worldPerUnit)
        {
            worldPerUnit = default;
            if (!IsReady || !TryLocalBasis(m_forkDir, out var basis))
            {
                return false;
            }

            var parent = m_forkAxis.parent;
            worldPerUnit = parent != null ? parent.TransformVector(basis) : basis;
            return worldPerUnit.sqrMagnitude > 1e-10f;
        }

        private void Update()
        {
            if (!_moving)
            {
                return;
            }

            _moveElapsed += Time.deltaTime;
            var t = _moveDuration <= 1e-6f ? 1f : Mathf.Clamp01(_moveElapsed / _moveDuration);
            var row = _rowFree ? Vector3.Lerp(_rowFromLocal, _rowToLocal, t) : _rowFromLocal;
            var level = _levelFree ? Vector3.Lerp(_levelFromLocal, _levelToLocal, t) : _levelFromLocal;
            var fork = _forkFree ? Vector3.Lerp(_forkFromLocal, _forkToLocal, t) : _forkFromLocal;
            ApplyLocals(row, level, fork);
            if (t >= 1f)
            {
                _moving = false;
            }
        }

        /// <summary>
        /// 求各轴目标 local。掩码外的轴保持当前姿态。不改场景。
        /// 货叉只沿自己的 Dir 相对层向轴伸出，层向只沿自己的 Dir 相对走行轴升降。
        /// </summary>
        public bool TryComputeLocalTargets(
            Vector3 targetWorld,
            out Vector3 rowTarget,
            out Vector3 levelTarget,
            out Vector3 forkTarget) =>
            TryComputeLocalTargets(targetWorld, StackerAxisMask.All,
                out rowTarget, out levelTarget, out forkTarget);

        public bool TryComputeLocalTargets(
            Vector3 targetWorld,
            StackerAxisMask mask,
            out Vector3 rowTarget,
            out Vector3 levelTarget,
            out Vector3 forkTarget)
        {
            rowTarget = default;
            levelTarget = default;
            forkTarget = default;
            if (!IsReady)
            {
                return false;
            }

            EnsureRest();
            if (mask == StackerAxisMask.None)
            {
                mask = StackerAxisMask.All;
            }

            var moveRow = (mask & StackerAxisMask.Row) != 0;
            var moveLevel = (mask & StackerAxisMask.Level) != 0;
            var moveFork = (mask & StackerAxisMask.Fork) != 0;

            var row = ReadScalar(m_rowAxis, _rowRest, m_rowDir);
            var level = ReadScalar(m_levelAxis, _levelRest, m_levelDir);
            var fork = ReadScalar(m_forkAxis, _forkRest, m_forkDir);
            // 先收到各自滑轨上：层向不保留货叉的 Z，货叉也不替机体做垂直位移。
            var rowLocal = moveRow ? OnRail(_rowRest, m_rowDir, row) : m_rowAxis.localPosition;
            var levelLocal = moveLevel ? OnRail(_levelRest, m_levelDir, level) : m_levelAxis.localPosition;
            var forkLocal = moveFork ? OnRail(_forkRest, m_forkDir, fork) : m_forkAxis.localPosition;

            var error = targetWorld - AlignReference.position;
            if (moveRow)
            {
                error -= WorldDeltaOfLocal(m_rowAxis, m_rowAxis.localPosition, rowLocal);
            }

            if (moveLevel)
            {
                error -= WorldDeltaOfLocal(m_levelAxis, m_levelAxis.localPosition, levelLocal);
            }

            if (moveFork)
            {
                error -= WorldDeltaOfLocal(m_forkAxis, m_forkAxis.localPosition, forkLocal);
            }

            if (moveRow)
            {
                rowLocal = SlideLocal(m_rowAxis, m_rowDir, rowLocal, ref error);
            }

            if (moveLevel)
            {
                levelLocal = SlideLocal(m_levelAxis, m_levelDir, levelLocal, ref error);
            }

            if (moveFork)
            {
                forkLocal = SlideLocal(m_forkAxis, m_forkDir, forkLocal, ref error);
            }

            rowTarget = rowLocal;
            levelTarget = levelLocal;
            forkTarget = forkLocal;
            return true;
        }

        private void ApplyLocals(Vector3 row, Vector3 level, Vector3 fork)
        {
            if (m_rowAxis != null)
            {
                m_rowAxis.localPosition = row;
            }

            if (m_levelAxis != null)
            {
                m_levelAxis.localPosition = level;
            }

            if (m_forkAxis != null)
            {
                m_forkAxis.localPosition = fork;
            }
        }

        private void EnsureRest()
        {
            if (m_rowAxis == null || m_levelAxis == null || m_forkAxis == null)
            {
                return;
            }

            if (!_restReady)
            {
                _rowRest = m_rowAxis.localPosition;
                _levelRest = m_levelAxis.localPosition;
                _forkRest = m_forkAxis.localPosition;
                _restReady = true;
            }

            var joints = m_legacyJointController != null ? m_legacyJointController.Joints : null;
            if (joints == null || joints.Length < 3)
            {
                return;
            }

            if (joints[0] != null && joints[0].JointsNode == m_rowAxis)
            {
                _rowRest = joints[0].ZeroState;
            }

            if (joints[1] != null && joints[1].JointsNode == m_levelAxis)
            {
                _levelRest = joints[1].ZeroState;
            }

            if (joints[2] != null && joints[2].JointsNode == m_forkAxis)
            {
                _forkRest = joints[2].ZeroState;
            }
        }

        /// <summary>把本轴 local 从 from 改到 to 时，对齐点在世界上的位移。轴向不动对齐点时为 0。</summary>
        private Vector3 WorldDeltaOfLocal(Transform axis, Vector3 fromLocal, Vector3 toLocal)
        {
            if (!MovesAlign(axis))
            {
                return Vector3.zero;
            }

            var delta = toLocal - fromLocal;
            if (delta.sqrMagnitude < 1e-12f)
            {
                return Vector3.zero;
            }

            var parent = axis.parent;
            return parent != null ? parent.TransformVector(delta) : delta;
        }

        /// <summary>沿 dir 再滑一截，local 的其余分量不动。返回滑完后的 local。</summary>
        private Vector3 SlideLocal(Transform axis, JointDirType dir, Vector3 local, ref Vector3 error)
        {
            var delta = SlideAlong(axis, dir, ref error);
            if (!TryLocalBasis(dir, out var basis) || Mathf.Abs(delta) < 1e-8f)
            {
                return local;
            }

            var index = AxisIndex(basis);
            local[index] += delta * basis[index];
            return local;
        }

        /// <summary>
        /// 用本轴世界方向吃掉剩余误差上的投影，返回要追加的标量。轴向不动对齐点时返回 0。
        /// </summary>
        private float SlideAlong(Transform axis, JointDirType dir, ref Vector3 error)
        {
            if (!MovesAlign(axis) || !TryLocalBasis(dir, out var basis))
            {
                return 0f;
            }

            var parent = axis.parent;
            var world = parent != null ? parent.TransformVector(basis) : basis;
            var denom = world.sqrMagnitude;
            if (denom < 1e-10f)
            {
                return 0f;
            }

            var delta = Vector3.Dot(error, world) / denom;
            if (float.IsNaN(delta) || float.IsInfinity(delta))
            {
                return 0f;
            }

            error -= world * delta;
            return delta;
        }

        private bool MovesAlign(Transform axis)
        {
            if (axis == null)
            {
                return false;
            }

            var align = AlignReference;
            return align == axis || align.IsChildOf(axis);
        }

        private static float ReadScalar(Transform axis, Vector3 rest, JointDirType dir)
        {
            if (axis == null)
            {
                return 0f;
            }

            return ReadScalar(axis.localPosition, rest, dir);
        }

        private static float ReadScalar(Vector3 local, Vector3 rest, JointDirType dir)
        {
            if (!TryLocalBasis(dir, out var basis))
            {
                return 0f;
            }

            var index = AxisIndex(basis);
            return (local[index] - rest[index]) * basis[index];
        }

        private static Vector3 OnRail(Vector3 rest, JointDirType dir, float scalar)
        {
            if (!TryLocalBasis(dir, out var basis))
            {
                return rest;
            }

            var index = AxisIndex(basis);
            rest[index] += scalar * basis[index];
            return rest;
        }

        private static void WriteScalar(Transform axis, Vector3 rest, JointDirType dir, float scalar)
        {
            if (axis == null)
            {
                return;
            }

            axis.localPosition = OnRail(rest, dir, scalar);
        }

        /// <summary>正方向为单位轴，反方向（IX/IY/IZ）为单位轴取反。分量值就是标量的符号。</summary>
        private static bool TryLocalBasis(JointDirType dir, out Vector3 basis)
        {
            switch (dir)
            {
                case JointDirType.X:
                    basis = Vector3.right;
                    return true;
                case JointDirType.IX:
                    basis = Vector3.left;
                    return true;
                case JointDirType.Y:
                    basis = Vector3.up;
                    return true;
                case JointDirType.IY:
                    basis = Vector3.down;
                    return true;
                case JointDirType.Z:
                    basis = Vector3.forward;
                    return true;
                case JointDirType.IZ:
                    basis = Vector3.back;
                    return true;
                default:
                    basis = default;
                    return false;
            }
        }

        private static int AxisIndex(Vector3 basis)
        {
            if (basis.x != 0f)
            {
                return 0;
            }

            if (basis.y != 0f)
            {
                return 1;
            }

            return 2;
        }
    }
}
