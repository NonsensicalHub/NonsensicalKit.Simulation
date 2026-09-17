using UnityEngine;
using UnityEngine.Serialization;

namespace NonsensicalKit.ScriptAnimation
{
    public enum ForkliftMode
    {
        [InspectorName("取货")]
        PickUp = 0,
        [InspectorName("放货")]
        PutDown = 1
    }

    /// <summary>
    /// 叉车脚本动画组件：在 <see cref="PathMoveActor"/> 上增加货叉与原地转向。
    /// 潜伏车（举升 AGV）请用 <see cref="LatentAgvAnim"/>。
    /// CTU（机构：车→举升→旋转→夹爪→拨爪）请用 <see cref="CtuAnim"/>。
    /// 穿梭车（无举升旋转，带夹紧机构）请用 <see cref="ShuttleAnim"/>。
    /// 转弯时锁定目标世界旋转请用 <see cref="WorldRotationLockAnim"/>（可挂任意物体）+ 另建绑定它的 <see cref="ScriptDedicatedTrack"/>。
    /// 朝向走基类模型本地上/前方轴；取放货高度默认值在本组件配置，新增 <see cref="ForkliftClip"/> 时写入 Clip（可再改）。
    /// 车体旋转速度见基类 <see cref="PathMoveActor.RotateSpeed"/>。
    /// 货物显隐等请用 Timeline Activation / 其它轨控制。
    /// </summary>
    [AddComponentMenu("ScriptAnimation/叉车 (ForkliftAnim)")]
    public class ForkliftAnim : PathMoveActor
    {
        [Header("货叉")]
        [InspectorLabel("货叉")]
        [SerializeField] private Transform m_fork;
        [Tooltip("相对货叉节点自身本地坐标的抬升方向（通常 +Y），用于货叉高度。")]
        [InspectorLabel("抬升轴（货叉本地）")]
        [SerializeField] private Vector3 m_liftAxisLocal = Vector3.up;

        [Header("新增 Clip 默认值（写入 Clip，可再改）")]
        [InspectorLabel("货叉速度")]
        [SerializeField] private float m_forkSpeed = 0.8f;
        [InspectorLabel("接近距离")]
        [SerializeField] private float m_approachDistance = 1.2f;

        [Tooltip("取放货时是否倒车朝向（车尾朝前进方向）。")]
        [InspectorLabel("倒车朝向")]
        [SerializeField] private bool m_defaultReverseFacing;
        [Tooltip("空载时的货叉高度（取货开场 / 放货结束）。")]
        [InspectorLabel("空载高度")]
        [FormerlySerializedAs("m_defaultForkStartTravelHeight")]
        [SerializeField] private float m_defaultForkEmptyHeight = 0.15f;
        [Tooltip("载货行驶时的货叉高度（取货结束 / 放货开场）。")]
        [InspectorLabel("载货行驶高度")]
        [FormerlySerializedAs("m_defaultForkEndTravelHeight")]
        [SerializeField] private float m_defaultForkLoadedTravelHeight = 0.15f;
        [Tooltip("插入货架 / 放货落地时的货叉高度。")]
        [InspectorLabel("放货/插入高度")]
        [SerializeField] private float m_defaultForkPlaceHeight;
        [Tooltip("载货抬起后的货叉高度。")]
        [InspectorLabel("载货抬起高度")]
        [SerializeField] private float m_defaultForkLiftHeight = 0.5f;
        [Tooltip("货叉与货位重叠时的停顿帧数（按 60fps）。")]
        [InspectorLabel("货物切换停顿帧数")]
        [SerializeField] private int m_defaultCargoSwapHoldFrames = 5;

        public Transform Fork => m_fork;
        /// <summary>相对货叉节点自身的本地抬升轴（配置值）。</summary>
        public Vector3 LiftAxisLocal => m_liftAxisLocal.sqrMagnitude > 1e-6f
            ? m_liftAxisLocal.normalized
            : Vector3.up;

        /// <summary>
        /// 抬升轴在货叉父节点空间中的方向（用于改 <see cref="Transform.localPosition"/>）。
        /// 以货叉当前本地旋转变换，使配置轴始终相对货叉节点本地坐标。
        /// </summary>
        public Vector3 LiftAxisInParent
        {
            get
            {
                if (m_fork == null)
                    return LiftAxisLocal;

                Vector3 dir = m_fork.localRotation * LiftAxisLocal;
                return dir.sqrMagnitude > 1e-8f ? dir.normalized : Vector3.up;
            }
        }

        public float ForkSpeed => m_forkSpeed;
        public float ApproachDistance => m_approachDistance;

        public bool DefaultReverseFacing => m_defaultReverseFacing;
        public float DefaultForkEmptyHeight => m_defaultForkEmptyHeight;
        public float DefaultForkLoadedTravelHeight => m_defaultForkLoadedTravelHeight;
        public float DefaultForkPlaceHeight => m_defaultForkPlaceHeight;
        public float DefaultForkLiftHeight => m_defaultForkLiftHeight;
        public int DefaultCargoSwapHoldFrames => Mathf.Max(0, m_defaultCargoSwapHoldFrames);

        /// <summary>按抬升轴写入货叉高度（父空间 localPosition）。</summary>
        public void ApplyForkHeight(float height)
        {
            if (m_fork == null)
                return;

            Vector3 axis = LiftAxisInParent;
            Vector3 local = m_fork.localPosition;
            local -= axis * Vector3.Dot(local, axis);
            local += axis * height;
            m_fork.localPosition = local;
        }

        /// <summary>首 Clip 之前：车体 Home + 空载货叉高度。</summary>
        public void ApplyDefaultTravelPose()
        {
            ApplyHomePose("叉车首 Clip 之前");
            ApplyForkHeight(m_defaultForkEmptyHeight);
        }

        /// <summary>将本组件默认值写入 Clip（新增 Clip 时调用）。</summary>
        public void ApplyClipDefaults(ForkliftClipData data)
        {
            if (data == null)
                return;

            data.ReverseFacing = m_defaultReverseFacing;
            data.ForkEmptyHeight = m_defaultForkEmptyHeight;
            data.ForkLoadedTravelHeight = m_defaultForkLoadedTravelHeight;
            data.ForkPlaceHeight = m_defaultForkPlaceHeight;
            data.ForkLiftHeight = m_defaultForkLiftHeight;
            data.CargoSwapHoldFrames = DefaultCargoSwapHoldFrames;
            data.MoveSpeed = MoveSpeed;
            data.ForkSpeed = m_forkSpeed;
            data.RotateSpeed = RotateSpeed;
            data.ApproachDistance = m_approachDistance;
        }
    }
}
