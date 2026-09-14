using NonsensicalKit.Core;
using NonsensicalKit.DigitalTwin.Warehouse;
using UnityEngine;
using UnityEngine.Serialization;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 堆垛机脚本动画：按货位坐标（层/列/排/深）取世界位置，
    /// 双轴（行走 + 升降）各沿一个可配置世界轴向同时梯形加减速移动。
    /// 位置解析：优先 <see cref="WarehouseManager"/>，否则用 <see cref="CellPositionTable"/>。
    /// </summary>
    [AddComponentMenu("ScriptAnimation/堆垛机 (StackerAnim)")]
    public class StackerAnim : ScriptAnimActor
    {
        [Header("货位坐标解析")]
        [Tooltip("有仓库时：通过 WarehouseManager.GetRuntimeBinData 解析")]
        [InspectorLabel("仓库管理器")]
        [SerializeField] private WarehouseManager m_warehouse;

        [Tooltip("无仓库时使用：货位坐标 → 世界位置表")]
        [InspectorLabel("货位坐标表")]
        [SerializeField] private CellPositionTable m_cellTable;

        [Header("轴引用（可空）")]
        [Tooltip("行走轴 Transform；为空则移动本物体。")]
        [InspectorLabel("行走轴")]
        [SerializeField] private Transform m_travelAxis;

        [Tooltip("升降轴 Transform；为空则与行走轴相同（或本物体）。")]
        [InspectorLabel("升降轴")]
        [SerializeField] private Transform m_liftAxis;

        [Header("轴向方向（每轴仅一个方向）")]
        [Tooltip("行走轴世界方向（例如巷道 +Z）")]
        [InspectorLabel("行走方向")]
        [SerializeField] private SignedAxis m_travelDirection = SignedAxis.PositiveZ;

        [Tooltip("升降轴世界方向（通常 +Y）")]
        [InspectorLabel("升降方向")]
        [SerializeField] private SignedAxis m_liftDirection = SignedAxis.PositiveY;

        [Header("新增 Clip 默认值（写入 Clip，可再改）")]
        [Tooltip("最大行走速度（米/秒）")]
        [InspectorLabel("行走速度")]
        [SerializeField] private float m_travelSpeed = 2f;

        [Tooltip("行走加速度（米/秒²），减速与加速相同")]
        [InspectorLabel("行走加速度")]
        [SerializeField] private float m_travelAcceleration = 1.5f;

        [Tooltip("最大升降速度（米/秒）")]
        [InspectorLabel("升降速度")]
        [SerializeField] private float m_liftSpeed = 1.2f;

        [Tooltip("升降加速度（米/秒²），减速与加速相同")]
        [InspectorLabel("升降加速度")]
        [SerializeField] private float m_liftAcceleration = 1f;

        [Header("货叉（取放货时停在原地，仅货叉运动）")]
        [Tooltip("一级货叉 Transform")]
        [InspectorLabel("一级货叉")]
        [FormerlySerializedAs("m_fork")]
        [SerializeField] private Transform m_primaryFork;

        [Tooltip("一级货叉本地运动轴（伸叉多为水平，抬升多为 +Y）")]
        [InspectorLabel("一级货叉运动轴")]
        [FormerlySerializedAs("m_forkAxisLocal")]
        [SerializeField] private Vector3 m_primaryForkAxisLocal = Vector3.forward;

        [Tooltip("二级货叉 Transform；通常是一级货叉子节点，可为空")]
        [InspectorLabel("二级货叉")]
        [SerializeField] private Transform m_secondaryFork;

        [Tooltip("二级货叉本地运动轴")]
        [InspectorLabel("二级货叉运动轴")]
        [SerializeField] private Vector3 m_secondaryForkAxisLocal = Vector3.forward;

        [Header("新增货叉 Clip 默认值 - 一级货叉")]
        [InspectorLabel("一级货叉速度")]
        [FormerlySerializedAs("m_forkSpeed")]
        [SerializeField] private float m_primaryForkSpeed = 0.8f;

        [Tooltip("待机/行驶时的货叉偏移")]
        [InspectorLabel("一级行驶偏移")]
        [FormerlySerializedAs("m_defaultForkTravelOffset")]
        [SerializeField] private float m_defaultPrimaryForkTravelOffset;

        [Tooltip("插入货架 / 放货时的货叉偏移")]
        [InspectorLabel("一级放货/插入偏移")]
        [FormerlySerializedAs("m_defaultForkPlaceOffset")]
        [SerializeField] private float m_defaultPrimaryForkPlaceOffset = 0.8f;

        [Tooltip("载货抬起后的货叉偏移")]
        [InspectorLabel("一级载货抬起偏移")]
        [FormerlySerializedAs("m_defaultForkLiftOffset")]
        [SerializeField] private float m_defaultPrimaryForkLiftOffset = 1f;

        [Header("新增货叉 Clip 默认值 - 二级货叉")]
        [InspectorLabel("二级货叉速度")]
        [SerializeField] private float m_secondaryForkSpeed = 0.8f;

        [Tooltip("待机/行驶时的二级货叉偏移")]
        [InspectorLabel("二级行驶偏移")]
        [SerializeField] private float m_defaultSecondaryForkTravelOffset;

        [Tooltip("插入货架 / 放货时的二级货叉偏移")]
        [InspectorLabel("二级放货/插入偏移")]
        [SerializeField] private float m_defaultSecondaryForkPlaceOffset = 0.8f;

        [Tooltip("载货抬起后的二级货叉偏移")]
        [InspectorLabel("二级载货抬起偏移")]
        [SerializeField] private float m_defaultSecondaryForkLiftOffset = 1f;

        [Header("Home（无前序 / 未指定起点坐标时的回退）")]
        [InspectorLabel("Home 位置")]
        [SerializeField] private Vector3 m_homePosition;
        [InspectorLabel("已设置 Home")]
        [SerializeField] private bool m_hasHome;

        public WarehouseManager Warehouse => m_warehouse;
        public CellPositionTable CellTable => m_cellTable;

        public Transform TravelAxis => m_travelAxis != null ? m_travelAxis : transform;
        public Transform LiftAxis => m_liftAxis != null ? m_liftAxis : TravelAxis;

        public SignedAxis TravelDirection => m_travelDirection;
        public SignedAxis LiftDirection => m_liftDirection;

        public float TravelSpeed => m_travelSpeed;
        public float TravelAcceleration => m_travelAcceleration;
        public float LiftSpeed => m_liftSpeed;
        public float LiftAcceleration => m_liftAcceleration;

        public Transform PrimaryFork => m_primaryFork;
        public Transform SecondaryFork => m_secondaryFork;

        /// <summary>一级货叉（兼容旧 API）。</summary>
        public Transform Fork => m_primaryFork;

        public Vector3 PrimaryForkAxisLocal => NormalizeAxis(m_primaryForkAxisLocal);
        public Vector3 SecondaryForkAxisLocal => NormalizeAxis(m_secondaryForkAxisLocal);

        /// <summary>一级货叉运动轴（兼容旧 API）。</summary>
        public Vector3 ForkAxisLocal => PrimaryForkAxisLocal;

        public float PrimaryForkSpeed => m_primaryForkSpeed;
        public float SecondaryForkSpeed => m_secondaryForkSpeed;

        /// <summary>一级货叉速度（兼容旧 API）。</summary>
        public float ForkSpeed => m_primaryForkSpeed;

        public float DefaultPrimaryForkTravelOffset => m_defaultPrimaryForkTravelOffset;
        public float DefaultPrimaryForkPlaceOffset => m_defaultPrimaryForkPlaceOffset;
        public float DefaultPrimaryForkLiftOffset => m_defaultPrimaryForkLiftOffset;

        public float DefaultSecondaryForkTravelOffset => m_defaultSecondaryForkTravelOffset;
        public float DefaultSecondaryForkPlaceOffset => m_defaultSecondaryForkPlaceOffset;
        public float DefaultSecondaryForkLiftOffset => m_defaultSecondaryForkLiftOffset;

        /// <summary>兼容旧 API。</summary>
        public float DefaultForkTravelOffset => m_defaultPrimaryForkTravelOffset;
        public float DefaultForkPlaceOffset => m_defaultPrimaryForkPlaceOffset;
        public float DefaultForkLiftOffset => m_defaultPrimaryForkLiftOffset;

        static Vector3 NormalizeAxis(Vector3 axis)
            => axis.sqrMagnitude > 1e-6f ? axis.normalized : Vector3.forward;

        public bool HasHome => m_hasHome;
        public Vector3 HomePosition => m_homePosition;

        /// <summary>将本组件默认值写入堆垛机行走 Clip（新增 Clip 时调用）。</summary>
        public void ApplyClipDefaults(StackerClipData data)
        {
            if (data == null)
                return;

            data.TravelSpeed = m_travelSpeed;
            data.TravelAcceleration = m_travelAcceleration;
            data.LiftSpeed = m_liftSpeed;
            data.LiftAcceleration = m_liftAcceleration;
        }

        /// <summary>将本组件默认值写入堆垛机货叉 Clip（新增 Clip 时调用）。</summary>
        public void ApplyClipDefaults(StackerForkClipData data)
        {
            if (data == null)
                return;

            data.ForkTravelOffset = m_defaultPrimaryForkTravelOffset;
            data.ForkPlaceOffset = m_defaultPrimaryForkPlaceOffset;
            data.ForkLiftOffset = m_defaultPrimaryForkLiftOffset;
            data.ForkSpeed = m_primaryForkSpeed;

            data.SecondaryForkTravelOffset = m_defaultSecondaryForkTravelOffset;
            data.SecondaryForkPlaceOffset = m_defaultSecondaryForkPlaceOffset;
            data.SecondaryForkLiftOffset = m_defaultSecondaryForkLiftOffset;
            data.SecondaryForkSpeed = m_secondaryForkSpeed;
        }

        /// <summary>当前货位参考点：行走轴位置上，用升降轴覆盖其轴向分量。</summary>
        public Vector3 CurrentSlotPosition
        {
            get
            {
                Transform travel = TravelAxis;
                Transform lift = LiftAxis;
                if (travel == lift)
                    return travel.position;

                Vector3 p = travel.position;
                return SignedAxisUtil.WithComponent(
                    p, m_liftDirection, SignedAxisUtil.GetComponent(lift.position, m_liftDirection));
            }
        }

        /// <summary>
        /// 货位坐标 → 世界位置。优先 WarehouseManager，否则 CellPositionTable。
    /// </summary>
        public bool TryGetCellWorldPosition(Int4 cell, out Vector3 worldPos)
        {
            if (WarehouseCellResolver.TryGetWorldPosition(m_warehouse, cell, out worldPos))
                return true;

            if (m_cellTable != null && m_cellTable.TryGet(cell, out worldPos))
                return true;

            worldPos = default;
            return false;
        }

        [ContextMenu("从当前位置捕获 Home")]
        public void CaptureHomeFromCurrent()
        {
            m_homePosition = CurrentSlotPosition;
            m_hasHome = true;
        }

        /// <summary>
        /// 将行起升降轴放到目标货位：各轴只写各自配置方向上的分量。
    /// </summary>
        public void ApplySlotPosition(Vector3 slotWorld)
        {
            Transform travel = TravelAxis;
            Transform lift = LiftAxis;

            if (travel == lift)
            {
                Vector3 p = travel.position;
                p = SignedAxisUtil.WithComponent(
                    p, m_travelDirection, SignedAxisUtil.GetComponent(slotWorld, m_travelDirection));
                p = SignedAxisUtil.WithComponent(
                    p, m_liftDirection, SignedAxisUtil.GetComponent(slotWorld, m_liftDirection));
                travel.position = p;
                return;
            }

            travel.position = SignedAxisUtil.WithComponent(
                travel.position,
                m_travelDirection,
                SignedAxisUtil.GetComponent(slotWorld, m_travelDirection));

            lift.position = SignedAxisUtil.WithComponent(
                lift.position,
                m_liftDirection,
                SignedAxisUtil.GetComponent(slotWorld, m_liftDirection));
        }

#if UNITY_EDITOR
        private void Reset()
        {
            CaptureHomeFromCurrent();
        }

        private void OnValidate()
        {
            if (!m_hasHome)
                CaptureHomeFromCurrent();

            if (SignedAxisUtil.ToComponentIndex(m_travelDirection)
                == SignedAxisUtil.ToComponentIndex(m_liftDirection))
            {
                Debug.LogWarning(
                    "[StackerAnim] 行走轴与升降轴配置为同一世界分量，运动将互相覆盖。",
                    this);
            }
        }
#endif
    }
}
