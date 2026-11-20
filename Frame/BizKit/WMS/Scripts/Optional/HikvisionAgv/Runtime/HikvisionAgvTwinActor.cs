using System;
using System.Collections.Generic;
using System.Globalization;
using HikvisionAgv;
using NaughtyAttributes;
using NonsensicalKit.Core;
using NonsensicalKit.DigitalTwin.Motion;
using UnityEngine;

namespace HikvisionAgv
{
/// <summary>
/// 海康举升 AGV 孪生车体：经 MotionPartUpdate 收状态点位，按本层地图框换算位姿。
/// 车体跟 RobotDir；举升平台跟 PodDir（可与车体朝向不同）。
/// Status=6 进入时，在货叉搜寻半径内找已「启用货叉升降」的点位行为，切到配置挡位并平滑升降；启动默认挡位 0。
/// 开启「先到可旋转点再转」时：用 AGV 搜寻距离找旋转点；用该点「旋转钳制距离」判断是否解除锁定并并行跟随。
/// m_partID = 车号。点位按下标与 PartConfig.pointConfigs 对齐。
/// </summary>
[AddComponentMenu("海康AGV/孪生车体 (HikvisionAgvTwinActor)")]
public sealed class HikvisionAgvTwinActor : PartMotionBase
{
    enum BodyRotatePhase
    {
        Follow = 0,
        ApproachRotateNode = 1,
        RotateInPlace = 2
    }
    public enum RobotDirMode
    {
        [InspectorName("角度（度）")]
        Degrees = 0,

        [InspectorName("四向编号 0～3")]
        CardinalIndex = 1
    }

    /// <summary>模型本地车头轴（Unity 默认 forward 为 +Z；Y 为竖直轴，纯 Yaw 下偏置为 0）。</summary>
    public enum LocalHeadAxis
    {
        [InspectorName("本地 +Z")]
        PlusZ = 0,

        [InspectorName("本地 -Z")]
        MinusZ = 1,

        [InspectorName("本地 +X")]
        PlusX = 2,

        [InspectorName("本地 -X")]
        MinusX = 3,

        [InspectorName("本地 +Y")]
        PlusY = 4,

        [InspectorName("本地 -Y")]
        MinusY = 5
    }

    /// <summary>与现场角度值对齐的世界方向（Y 为竖直轴，纯 Yaw 下参考角为 0）。</summary>
    public enum WorldRefAxis
    {
        [InspectorName("世界 +Z")]
        PlusZ = 0,

        [InspectorName("世界 -Z")]
        MinusZ = 1,

        [InspectorName("世界 +X")]
        PlusX = 2,

        [InspectorName("世界 -X")]
        MinusX = 3,

        [InspectorName("世界 +Y")]
        PlusY = 4,

        [InspectorName("世界 -Y")]
        MinusY = 5
    }

    /// <summary>俯视（+Y 向下看）时，现场角度增大对应的转向。</summary>
    public enum AngleIncreaseDir
    {
        [InspectorName("顺时针（与 Unity Y 轴正旋一致）")]
        ClockwiseLookingDown = 0,

        [InspectorName("逆时针")]
        CounterClockwiseLookingDown = 1
    }

    /// <summary>平台世界朝向相对车体 / 货架角的策略。</summary>
    public enum PlatformYawMode
    {
        [InspectorName("空载跟车体，抬升跟货架角")]
        FollowPodWhenLoaded = 0,

        [InspectorName("始终跟货架角 PodDir")]
        AlwaysFollowPod = 1,

        [InspectorName("始终跟车体（不单独旋转）")]
        LockToBody = 2
    }

    [Header("地图")]
    [SerializeField, Label("地图框")]
    private HikvisionMapFrame m_mapFrame;

    [SerializeField, Label("坐标倍率"), Tooltip("PosX/PosY 相对地图坐标放大倍率；毫米填 1000；1=不缩放")]
    private float m_coordScale = 1f;

    [Header("平滑")]
    [SerializeField, Label("位移平滑(秒)")]
    private float m_positionSmooth = 0.15f;

    [SerializeField, Label("车体朝向平滑(秒)")]
    private float m_rotationSmooth = 0.12f;

    [SerializeField, Label("平台朝向平滑(秒)"), Tooltip("<0 则沿用车体朝向平滑")]
    private float m_platformRotationSmooth = -1f;

    [SerializeField, Label("升降平滑(秒)"), Tooltip("≤0 为瞬间到位")]
    private float m_liftSmooth = 0.35f;

    [Header("现场角度 → Unity 朝向")]
    [SerializeField, Label("方向模式")]
    private RobotDirMode m_dirMode = RobotDirMode.Degrees;

    [SerializeField, Label("车体车头轴")]
    private LocalHeadAxis m_headAxis = LocalHeadAxis.PlusZ;

    [SerializeField, Label("世界参考轴")]
    private WorldRefAxis m_worldRefAxis = WorldRefAxis.PlusZ;

    [SerializeField, Label("参考轴对应角度")]
    private float m_angleAtWorldRef;

    [SerializeField, Label("角度增大方向")]
    private AngleIncreaseDir m_angleIncrease = AngleIncreaseDir.ClockwiseLookingDown;

    [SerializeField, Label("Yaw 微调(度)")]
    private float m_yawOffset;

    [Header("举升平台")]
    [SerializeField, Label("平台节点")]
    private Transform m_platform;

    [SerializeField, Label("抬升轴(本地)")]
    private Vector3 m_liftAxisLocal = Vector3.up;

    [SerializeField, Label("货叉挡位高度"), Tooltip("下标=挡位号，值=抬升轴本地坐标；启动默认挡位 0")]
    private float[] m_liftGears = { 0f, 0.05f };

    [SerializeField, Label("平台朝向策略")]
    private PlatformYawMode m_platformYawMode = PlatformYawMode.FollowPodWhenLoaded;

    [SerializeField, Label("等平台转完再位移")]
    private bool m_waitPlatformRotateBeforeMove = true;

    [SerializeField, Label("平台到位公差(度)")]
    private float m_platformAlignTolerance = 1f;

    [SerializeField, Label("平台自用车头轴")]
    private bool m_platformUseOwnHeadAxis;

    [SerializeField, Label("平台车头轴"), ShowIf(nameof(m_platformUseOwnHeadAxis))]
    private LocalHeadAxis m_platformHeadAxis = LocalHeadAxis.PlusZ;

    [Header("点位行为")]
    [SerializeField, Label("启用先到可旋转点再转"), Tooltip("朝向变化时，先移到搜寻半径内最近「可原地旋转」点，转完再跟 Status 目标")]
    private bool m_rotateAtRotatableNode = true;

    [SerializeField, Label("可旋转点搜寻距离"), Tooltip("仅用于在路网中搜寻「可原地旋转」点的 XZ 半径（米）。≤0 关闭转向逻辑。是否解除旋转锁定改由点位行为上的「旋转钳制距离」决定")]
    private float m_rotatableSearchDistance = 2f;

    [SerializeField, Label("货叉行为搜寻距离"), Tooltip("进入 Status=6 时，在此 XZ 半径（米）内查找已「启用货叉升降」的点位行为，并按该点空载/载货挡位配置切换货叉。≤0 则沿用可旋转点搜寻距离；两者都≤0 时用 2 米")]
    private float m_forkBehaviorSearchDistance = -1f;

    [SerializeField, Label("到达可旋转点公差"), Tooltip("判定已到达旋转点的 XZ 距离（米）")]
    private float m_atRotatableTolerance = 0.08f;

    [SerializeField, Label("车体朝向到位公差(度)"), Tooltip("判定车体已转到目标朝向的角度差")]
    private float m_bodyAlignTolerance = 1f;

    [SerializeField, Label("判定需转向的角度差(度)"), Tooltip("Status 朝向与当前车体朝向差超过此值时，才触发先到可旋转点再转")]
    private float m_headingChangeThreshold = 1f;

    [SerializeField, Label("赶往旋转点位移平滑(秒)"), Tooltip("未到旋转点却需转向时，快速贴到旋转点；须 >0，过小接近闪现")]
    private float m_catchUpPositionSmooth = 0.05f;

    [SerializeField, Label("赶往旋转点朝向平滑(秒)"), Tooltip("同上阶段稍快转到目标角；须 >0")]
    private float m_catchUpRotationSmooth = 0.06f;

    [SerializeField, Label("显示搜寻距离圆"), Tooltip("选中时绘制可旋转点搜寻距离的 XZ 圆")]
    private bool m_drawRotatableSearchGizmo = true;

    [Header("货物")]
    [SerializeField, Label("货物模型"), Tooltip("PodCode 等于 0 时隐藏，其它情况显示")]
    private GameObject m_cargo;

    [Header("其它")]
    [SerializeField, Label("保持自身高度"), Tooltip("忽略路网节点高度，沿用车体当前 Y")]
    private bool m_keepOwnHeight;

    private Vector3 _targetPos;
    private Quaternion _targetBodyRot;
    private Quaternion _targetPlatformWorldRot;
    private float _targetLiftHeight;
    private bool _hasTarget;
    private bool _hasPlatformTarget;
    private bool _drivePlatformYaw;
    private bool _first = true;

    private Quaternion _platformLocalRot0;
    private bool _platformRotCaptured;

    private BodyRotatePhase _bodyRotatePhase = BodyRotatePhase.Follow;
    private Vector3 _pendingStatusPos;
    private Quaternion _pendingBodyRot;
    private Vector3 _rotateNodePos;
    private float _activeRotateClampDistance;

    [ShowNonSerializedField] private float _posX;
    [ShowNonSerializedField] private float _posY;
    [ShowNonSerializedField] private float _robotDir;
    [ShowNonSerializedField] private float _podDir;
    [ShowNonSerializedField] private string _podCode = "0";
    [ShowNonSerializedField] private string _status = string.Empty;
    private bool _loaded=false;
    private int _liftGear;
    private bool _lifting;
    private bool _platformAligned = true;
    private bool _bodyAligned = true;
    private int _stop;

    protected override void Awake()
    {
        base.Awake();
        CapturePlatformLocalRot();
        _liftGear = 0;
        _loaded = HasCargo(_podCode);
        SnapPlatformLift(ResolveLiftHeight(_liftGear));
        ApplyCargoVisibility(_loaded);
    }

    private void Update()
    {
        if (!_hasTarget)
            return;

        if (_hasPlatformTarget && m_platform != null)
            ApplyPlatformFrame();
        else
            _platformAligned = true;

        TickBodyRotatePhases();
        ApplyBodyMotion();
        _first = false;
    }

    private void ApplyBodyMotion()
    {
        Transform body = transform;
        float arriveTol = Mathf.Max(0.001f, m_atRotatableTolerance);
        bool catchingUpToRotateNode = m_rotateAtRotatableNode
                                      && _bodyRotatePhase != BodyRotatePhase.Follow
                                      && DistanceXZ(body.position, _rotateNodePos) > arriveTol;
        bool rotatingInPlace = m_rotateAtRotatableNode
                               && _bodyRotatePhase == BodyRotatePhase.RotateInPlace
                               && !catchingUpToRotateNode;

        bool canMove = !rotatingInPlace
                       && (_first || !m_waitPlatformRotateBeforeMove || !_drivePlatformYaw || _platformAligned);

        float posSmooth = catchingUpToRotateNode
            ? Mathf.Max(0.02f, m_catchUpPositionSmooth)
            : m_positionSmooth;
        float rotSmooth = catchingUpToRotateNode || _bodyRotatePhase == BodyRotatePhase.RotateInPlace
            ? Mathf.Max(0.02f, m_catchUpRotationSmooth)
            : m_rotationSmooth;

        if (canMove || catchingUpToRotateNode)
        {
            // 赶往旋转点时不受平台门控拖住，快速贴过去（平滑下限防闪现）
            if (posSmooth <= 0f || _first)
                body.position = catchingUpToRotateNode ? _rotateNodePos : _targetPos;
            else
            {
                Vector3 posTarget = catchingUpToRotateNode ? _rotateNodePos : _targetPos;
                body.position = Vector3.Lerp(
                    body.position, posTarget, 1f - Mathf.Exp(-Time.deltaTime / posSmooth));
            }
        }
        else if (rotatingInPlace)
        {
            body.position = _rotateNodePos;
        }

        if (rotSmooth <= 0f || _first)
            body.rotation = _targetBodyRot;
        else
            body.rotation = Quaternion.Slerp(
                body.rotation, _targetBodyRot, 1f - Mathf.Exp(-Time.deltaTime / rotSmooth));

        _bodyAligned = Quaternion.Angle(body.rotation, _targetBodyRot)
                       <= Mathf.Max(0.01f, m_bodyAlignTolerance);
    }

    protected override void OnReceiveData(List<PointData> part)
    {
        if (part == null || part.Count == 0)
            return;

        float posX = ReadFloat(part, HikvisionStatusPointCodes.PosX.Code, HikvisionStatusPointCodes.PosX.Index, _posX);
        float posY = ReadFloat(part, HikvisionStatusPointCodes.PosY.Code, HikvisionStatusPointCodes.PosY.Index, _posY);
        float robotDir = ReadFloat(part, HikvisionStatusPointCodes.RobotDir.Code, HikvisionStatusPointCodes.RobotDir.Index, _robotDir);
        float podDir = ReadFloat(part, HikvisionStatusPointCodes.PodDir.Code, HikvisionStatusPointCodes.PodDir.Index, _podDir);
        string podCode = ReadString(part, HikvisionStatusPointCodes.PodCode.Code, HikvisionStatusPointCodes.PodCode.Index, _podCode);
        string status = ReadString(part, HikvisionStatusPointCodes.Status.Code, HikvisionStatusPointCodes.Status.Index, _status);
        int stop = ReadInt(part, HikvisionStatusPointCodes.Stop.Code, HikvisionStatusPointCodes.Stop.Index, _stop);

        bool wasLifting = _lifting;

        _posX = posX;
        _posY = posY;
        _robotDir = robotDir;
        _podDir = podDir;
        _podCode = podCode;
        _status = status ?? string.Empty;
        _stop = stop;
        _loaded = HasCargo(_podCode);
        _lifting = IsLiftingStatus(_status);

        // 进入 Status=6：按附近点位行为切到配置挡位
        if (_lifting && !wasLifting)
            TryApplyForkCommandFromNearestNode();

        ApplyCargoVisibility(_loaded);
        ApplyStatus(posX, posY, robotDir, podDir);
    }

    protected override PartDataInfo GetInfo()
    {
        var defs = HikvisionStatusPointCodes.All;
        var infos = new List<PointDataInfo>(defs.Length);
        foreach (var def in defs)
        {
            infos.Add(new PointDataInfo(def.Name, ToPointDataType(def.ValueType), false));
        }

        return new PartDataInfo("海康AGV", m_partID, infos);
    }

    private void ApplyStatus(float posX, float posY, float robotDir, float podDir)
    {
        if (m_mapFrame == null)
        {
            Debug.LogWarning($"[HikActor:{m_partID}] 未绑定 HikvisionMapFrame", this);
            return;
        }

        float scale = m_coordScale > 1e-8f ? m_coordScale : 1f;
        Vector3 world = m_mapFrame.MapToWorld(posX / scale, posY / scale);
        if (m_keepOwnHeight)
            world.y = transform.position.y;

        float bodyYaw = DirToUnityYaw(robotDir, m_headAxis);
        ResolveBodyTargets(world, Quaternion.Euler(0f, bodyYaw, 0f));
        _hasTarget = true;

        UpdatePlatformTargets(robotDir, podDir);
    }

    private void UpdatePlatformTargets(float robotDir, float podDir)
    {
        if (m_platform == null)
        {
            _hasPlatformTarget = false;
            _drivePlatformYaw = false;
            _platformAligned = true;
            return;
        }

        CapturePlatformLocalRot();
        _targetLiftHeight = ResolveLiftHeight(_liftGear);
        _hasPlatformTarget = true;

        if (m_platformYawMode == PlatformYawMode.LockToBody)
        {
            _drivePlatformYaw = false;
            _platformAligned = true;
            return;
        }

        _drivePlatformYaw = true;
        bool raisedLike = _liftGear > 0;
        float platformDir = ResolvePlatformDir(robotDir, podDir, raisedLike);
        LocalHeadAxis platformHead = m_platformUseOwnHeadAxis ? m_platformHeadAxis : m_headAxis;
        float platformYaw = DirToUnityYaw(platformDir, platformHead);
        _targetPlatformWorldRot = Quaternion.Euler(0f, platformYaw, 0f);
    }

    /// <summary>
    /// 进入 Status=6 时，在搜寻半径内找已启用货叉升降的点位行为，按配置切换挡位。
    /// </summary>
    private void TryApplyForkCommandFromNearestNode()
    {
        var network = m_mapFrame != null ? m_mapFrame.Network : null;
        float search = m_forkBehaviorSearchDistance > 0f
            ? m_forkBehaviorSearchDistance
            : (m_rotatableSearchDistance > 0f ? m_rotatableSearchDistance : 2f);

        if (!HikvisionNodeBehaviorQuery.TryFindNearest(
                network, transform.position, search, out _, out var feature, out _,
                requireForkLift: true) ||
            feature == null)
            return;

        _liftGear = feature.ResolveForkGear(_loaded);
    }

    private void ResolveBodyTargets(Vector3 desiredPos, Quaternion desiredBodyRot)
    {
        _pendingStatusPos = desiredPos;
        _pendingBodyRot = desiredBodyRot;

        // 首帧或未启用：直接跟 Status（位移+旋转并行）
        if (_first || !m_rotateAtRotatableNode || m_rotatableSearchDistance <= 0f)
        {
            EnterFollow(desiredPos, desiredBodyRot);
            return;
        }

        float headingDelta = Quaternion.Angle(YawOnly(transform.rotation), desiredBodyRot);
        if (headingDelta < Mathf.Max(0.01f, m_headingChangeThreshold))
        {
            EnterFollow(desiredPos, desiredBodyRot);
            return;
        }

        var network = m_mapFrame != null ? m_mapFrame.Network : null;
        if (network == null ||
            !HikvisionNodeBehaviorQuery.TryFindNearest(
                network, transform.position, m_rotatableSearchDistance,
                out var rotateNode, out var feature, out _,
                requireInPlaceRotate: true) ||
            rotateNode == null)
        {
            EnterFollow(desiredPos, desiredBodyRot);
            return;
        }

        float clamp = feature != null ? feature.RotateClampDistance : 0f;
        // Status 超出该旋转点钳制距离：解除锁定，位移与旋转并行
        if (clamp > 0f && DistanceXZ(transform.position, desiredPos) > clamp)
        {
            EnterFollow(desiredPos, desiredBodyRot);
            return;
        }

        Vector3 rotatePos = rotateNode.Position;
        if (m_keepOwnHeight)
            rotatePos.y = transform.position.y;
        _rotateNodePos = rotatePos;
        _activeRotateClampDistance = clamp;

        float toNode = DistanceXZ(transform.position, rotatePos);
        float arriveTol = Mathf.Max(0.001f, m_atRotatableTolerance);

        if (toNode > arriveTol)
        {
            _bodyRotatePhase = BodyRotatePhase.ApproachRotateNode;
            _targetPos = rotatePos;
            // 边赶往旋转点边转到目标角（赶往阶段用更快的平滑）
            _targetBodyRot = desiredBodyRot;
            return;
        }

        _bodyRotatePhase = BodyRotatePhase.RotateInPlace;
        _targetPos = rotatePos;
        _targetBodyRot = desiredBodyRot;
    }

    private void TickBodyRotatePhases()
    {
        if (!m_rotateAtRotatableNode || _bodyRotatePhase == BodyRotatePhase.Follow)
            return;

        // 过程中 Status 超出当前旋转点钳制距离：立刻解锁，并行跟位移+朝向
        if (_activeRotateClampDistance > 0f &&
            DistanceXZ(transform.position, _pendingStatusPos) > _activeRotateClampDistance)
        {
            EnterFollow(_pendingStatusPos, _pendingBodyRot);
            return;
        }

        float arriveTol = Mathf.Max(0.001f, m_atRotatableTolerance);
        float alignTol = Mathf.Max(0.01f, m_bodyAlignTolerance);

        if (_bodyRotatePhase == BodyRotatePhase.ApproachRotateNode)
        {
            if (DistanceXZ(transform.position, _rotateNodePos) > arriveTol)
                return;

            _bodyRotatePhase = BodyRotatePhase.RotateInPlace;
            _targetPos = _rotateNodePos;
            _targetBodyRot = _pendingBodyRot;
            return;
        }

        if (_bodyRotatePhase == BodyRotatePhase.RotateInPlace)
        {
            if (Quaternion.Angle(transform.rotation, _targetBodyRot) > alignTol)
                return;

            EnterFollow(_pendingStatusPos, _pendingBodyRot);
        }
    }

    private void EnterFollow(Vector3 pos, Quaternion bodyRot)
    {
        _bodyRotatePhase = BodyRotatePhase.Follow;
        _targetPos = pos;
        _targetBodyRot = bodyRot;
        _pendingStatusPos = pos;
        _pendingBodyRot = bodyRot;
        _activeRotateClampDistance = 0f;
    }

    private static float DistanceXZ(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    private static Quaternion YawOnly(Quaternion rotation)
    {
        Vector3 euler = rotation.eulerAngles;
        return Quaternion.Euler(0f, euler.y, 0f);
    }

    private float ResolvePlatformDir(float robotDir, float podDir, bool raised)
    {
        switch (m_platformYawMode)
        {
            case PlatformYawMode.AlwaysFollowPod:
                return podDir;
            default:
                return raised ? podDir : robotDir;
        }
    }

    private void ApplyPlatformFrame()
    {
        float liftT = m_liftSmooth <= 0f || _first
            ? 1f
            : 1f - Mathf.Exp(-Time.deltaTime / m_liftSmooth);

        Vector3 cur = m_platform.localPosition;
        Vector3 target = cur;
        SetLiftAxisLocal(ref target, m_liftAxisLocal, _targetLiftHeight);
        m_platform.localPosition = Vector3.Lerp(cur, target, liftT);

        if (!_drivePlatformYaw)
        {
            m_platform.localRotation = _platformLocalRot0;
            _platformAligned = true;
            return;
        }

        float rotSmooth = m_platformRotationSmooth >= 0f ? m_platformRotationSmooth : m_rotationSmooth;
        float rotT = rotSmooth <= 0f || _first
            ? 1f
            : 1f - Mathf.Exp(-Time.deltaTime / rotSmooth);

        if (rotT >= 1f)
            m_platform.rotation = _targetPlatformWorldRot;
        else
            m_platform.rotation = Quaternion.Slerp(m_platform.rotation, _targetPlatformWorldRot, rotT);

        _platformAligned = Quaternion.Angle(m_platform.rotation, _targetPlatformWorldRot)
                           <= Mathf.Max(0.01f, m_platformAlignTolerance);
    }

    private void SnapPlatformLift(float height)
    {
        _targetLiftHeight = height;
        if (m_platform == null)
            return;

        Vector3 local = m_platform.localPosition;
        SetLiftAxisLocal(ref local, m_liftAxisLocal, height);
        m_platform.localPosition = local;
        _hasPlatformTarget = true;
    }

    private float ResolveLiftHeight(int gear)
    {
        if (m_liftGears == null || m_liftGears.Length == 0)
            return 0f;

        int i = Mathf.Clamp(gear, 0, m_liftGears.Length - 1);
        return m_liftGears[i];
    }

    /// <summary>按抬升轴主分量，只写入对应本地坐标轴。</summary>
    private static void SetLiftAxisLocal(ref Vector3 localPos, Vector3 liftAxis, float value)
    {
        Vector3 axis = liftAxis.sqrMagnitude > 1e-6f ? liftAxis : Vector3.up;
        float ax = Mathf.Abs(axis.x);
        float ay = Mathf.Abs(axis.y);
        float az = Mathf.Abs(axis.z);
        if (ay >= ax && ay >= az)
            localPos.y = value;
        else if (ax >= az)
            localPos.x = value;
        else
            localPos.z = value;
    }

    private void ApplyCargoVisibility(bool loaded)
    {
        if (m_cargo == null)
            return;
        if (m_cargo.activeSelf != loaded)
            m_cargo.SetActive(loaded);
    }

    /// <summary>PodCode 等于 "0" 为空载；空值及其它均为载货。</summary>
    private static bool HasCargo(string podCode)
    {
        if (podCode == null)
            return true;
        return !string.Equals(podCode.Trim(), "0", StringComparison.Ordinal);
    }

    /// <summary>Status=6：举升货架过程中。</summary>
    private static bool IsLiftingStatus(string status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return false;
        return string.Equals(status.Trim(), "6", StringComparison.Ordinal);
    }

    /// <summary>现场角度 → Unity 世界 Yaw（度）。</summary>
    private float DirToUnityYaw(float raw, LocalHeadAxis headAxis)
    {
        float siteAngle = NormalizeSiteAngle(raw);
        float sign = m_angleIncrease == AngleIncreaseDir.ClockwiseLookingDown ? 1f : -1f;
        float yaw = WorldRefToUnityYaw(m_worldRefAxis)
                    + (siteAngle - m_angleAtWorldRef) * sign
                    + HeadAxisToYawOffset(headAxis)
                    + m_yawOffset;
        return yaw;
    }

    private float NormalizeSiteAngle(float v)
    {
        if (m_dirMode == RobotDirMode.CardinalIndex)
            return Mathf.Round(v) * 90f;
        return v;
    }

    private static float WorldRefToUnityYaw(WorldRefAxis axis)
    {
        switch (axis)
        {
            case WorldRefAxis.PlusX: return 90f;
            case WorldRefAxis.MinusX: return -90f;
            case WorldRefAxis.MinusZ: return 180f;
            // +Y/-Y 为竖直轴，无法作为水平参考；与 +Z 同视为 0°
            case WorldRefAxis.PlusY:
            case WorldRefAxis.MinusY:
            default: return 0f;
        }
    }

    /// <summary>
    /// Unity 对 Transform 施加 Yaw 后，local +Z 对准该世界方向。
    /// 若模型车头不在 +Z，补偏置使车头对准同一世界方向。
    /// </summary>
    private static float HeadAxisToYawOffset(LocalHeadAxis axis)
    {
        switch (axis)
        {
            case LocalHeadAxis.PlusX: return -90f;
            case LocalHeadAxis.MinusX: return 90f;
            case LocalHeadAxis.MinusZ: return 180f;
            // +Y/-Y 为竖直轴，仅 Yaw 无法对准；偏置 0
            case LocalHeadAxis.PlusY:
            case LocalHeadAxis.MinusY:
            default: return 0f;
        }
    }

    private void CapturePlatformLocalRot()
    {
        if (_platformRotCaptured || m_platform == null)
            return;

        _platformLocalRot0 = m_platform.localRotation;
        _platformRotCaptured = true;
    }

    private static bool TryFind(List<PointData> part, string pointCode, out PointData point)
    {
        for (int i = 0; i < part.Count; i++)
        {
            var p = part[i];
            if (p == null)
                continue;

            if (p.Point == pointCode)
            {
                point = p;
                return true;
            }
        }

        point = null;
        return false;
    }

    private static float ReadFloat(List<PointData> part, string pointCode, int fallbackIndex, float fallbackValue)
    {
        if (TryFind(part, pointCode, out var point))
            return ParseFloat(point.Value, fallbackValue);

        if (fallbackIndex >= 0 && fallbackIndex < part.Count && part[fallbackIndex] != null)
            return ParseFloat(part[fallbackIndex].Value, fallbackValue);

        return fallbackValue;
    }

    private static int ReadInt(
        List<PointData> part, string pointCode, int fallbackIndex, int fallbackValue)
    {
        if (TryFind(part, pointCode, out var point))
            return ParseInt(point.Value, fallbackValue);

        if (fallbackIndex >= 0 && fallbackIndex < part.Count && part[fallbackIndex] != null)
            return ParseInt(part[fallbackIndex].Value, fallbackValue);

        return fallbackValue;
    }

    private static string ReadString(
        List<PointData> part, string pointCode, int fallbackIndex, string fallbackValue)
    {
        if (TryFind(part, pointCode, out var point) && point.Value != null)
            return point.Value;

        if (fallbackIndex >= 0 && fallbackIndex < part.Count && part[fallbackIndex] != null)
            return part[fallbackIndex].Value ?? fallbackValue;

        return fallbackValue;
    }

    private static float ParseFloat(string raw, float fallback)
    {
        if (string.IsNullOrEmpty(raw))
            return fallback;

        return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float v)
            ? v
            : fallback;
    }

    private static int ParseInt(string raw, int fallback)
    {
        if (string.IsNullOrEmpty(raw))
            return fallback;

        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)
            ? v
            : fallback;
    }

    private static PointDataType ToPointDataType(HikPointValueType valueType) =>
        (PointDataType)(int)valueType;

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (m_mapFrame == null)
            m_mapFrame = GetComponentInParent<HikvisionMapFrame>();
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 origin = transform.position + Vector3.up * 0.05f;
        float yaw = DirToUnityYaw(_hasTarget ? _robotDir : m_angleAtWorldRef, m_headAxis);
        Vector3 head = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;

        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(origin, head * 0.8f);

        Vector3 refDir = Quaternion.Euler(0f, WorldRefToUnityYaw(m_worldRefAxis), 0f) * Vector3.forward;
        Gizmos.color = Color.yellow;
        Gizmos.DrawRay(origin, refDir * 0.5f);

        if (m_platform != null)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(m_platform.position, 0.08f);
        }

        if (m_drawRotatableSearchGizmo && m_rotateAtRotatableNode && m_rotatableSearchDistance > 0f)
        {
            Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.9f);
            DrawXZCircle(transform.position, m_rotatableSearchDistance, 64);
        }

        if (_bodyRotatePhase != BodyRotatePhase.Follow)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(_rotateNodePos, Mathf.Max(0.05f, m_atRotatableTolerance));
            Gizmos.DrawLine(transform.position, _rotateNodePos);
        }
    }

    static void DrawXZCircle(Vector3 center, float radius, int segments)
    {
        if (radius <= 0f || segments < 3)
            return;

        Vector3 prev = center + new Vector3(radius, 0f, 0f);
        float step = Mathf.PI * 2f / segments;
        for (int i = 1; i <= segments; i++)
        {
            float a = step * i;
            Vector3 next = center + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
            Gizmos.DrawLine(prev, next);
            prev = next;
        }
    }
#endif
}
}
