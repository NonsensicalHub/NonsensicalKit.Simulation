using UnityEngine;
using UnityEngine.Serialization;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 潜伏车（举升 AGV）脚本动画：在 <see cref="PathMoveActor"/> 上增加举升平台。
    /// 取放货见 <see cref="LatentAgvClip"/>：绑定移动点；取货抬货后驶到移动点，放货先转向再驶入移动点放下。
    /// 普通叉车请用 <see cref="ForkliftAnim"/>。
    /// CTU / 穿梭车请用 <see cref="CtuAnim"/> / <see cref="ShuttleAnim"/>。
    /// 转弯时锁定目标世界旋转请用 <see cref="WorldRotationLockAnim"/>（可挂任意物体）+ 另建绑定它的 <see cref="ScriptDedicatedTrack"/>。
    /// 车体旋转速度见基类 <see cref="PathMoveActor.RotateSpeed"/>。
    /// </summary>
    [AddComponentMenu("ScriptAnimation/潜伏车 (LatentAgvAnim)")]
    public class LatentAgvAnim : PathMoveActor
    {
        [Header("举升平台")]
        [InspectorLabel("举升平台")]
        [SerializeField] private Transform m_platform;
        [Tooltip("相对平台节点自身本地坐标的抬升方向（通常 +Y），用于平台高度。")]
        [InspectorLabel("抬升轴（平台本地）")]
        [SerializeField] private Vector3 m_liftAxisLocal = Vector3.up;

        [Header("新增 Clip 默认值（写入 Clip，可再改）")]
        [InspectorLabel("平台速度")]
        [SerializeField] private float m_platformSpeed = 0.8f;

        [Tooltip("空载时的平台高度（取货开场 / 放货结束）。")]
        [InspectorLabel("空载高度")]
        [FormerlySerializedAs("m_defaultPlatformStartTravelHeight")]
        [SerializeField] private float m_defaultPlatformEmptyHeight = 0.15f;
        [Tooltip("载货行驶时的平台高度（取货结束 / 放货开场）。")]
        [InspectorLabel("载货行驶高度")]
        [FormerlySerializedAs("m_defaultPlatformEndTravelHeight")]
        [SerializeField] private float m_defaultPlatformLoadedTravelHeight = 0.15f;
        [Tooltip("货物放置 / 插入货位时的平台高度。")]
        [InspectorLabel("货物放置高度")]
        [SerializeField] private float m_defaultPlatformPlaceHeight;
        [Tooltip("货物抬起后的平台高度。")]
        [InspectorLabel("货物抬起高度")]
        [SerializeField] private float m_defaultPlatformLiftHeight = 0.5f;
        [Tooltip("平台与货位重叠时的停顿帧数（按 60fps）。")]
        [InspectorLabel("货物切换停顿帧数")]
        [SerializeField] private int m_defaultCargoSwapHoldFrames = 5;

        public Transform Platform => m_platform;
        /// <summary>相对平台节点自身的本地抬升轴（配置值）。</summary>
        public Vector3 LiftAxisLocal => m_liftAxisLocal.sqrMagnitude > 1e-6f
            ? m_liftAxisLocal.normalized
            : Vector3.up;

        /// <summary>
        /// 抬升轴在平台父节点空间中的方向（用于改 <see cref="Transform.localPosition"/>）。
        /// 以平台当前本地旋转变换，使配置轴始终相对平台节点本地坐标。
        /// </summary>
        public Vector3 LiftAxisInParent
        {
            get
            {
                if (m_platform == null)
                    return LiftAxisLocal;

                Vector3 dir = m_platform.localRotation * LiftAxisLocal;
                return dir.sqrMagnitude > 1e-8f ? dir.normalized : Vector3.up;
            }
        }

        public float PlatformSpeed => m_platformSpeed;

        public float DefaultPlatformEmptyHeight => m_defaultPlatformEmptyHeight;
        public float DefaultPlatformLoadedTravelHeight => m_defaultPlatformLoadedTravelHeight;
        public float DefaultPlatformPlaceHeight => m_defaultPlatformPlaceHeight;
        public float DefaultPlatformLiftHeight => m_defaultPlatformLiftHeight;
        public int DefaultCargoSwapHoldFrames => Mathf.Max(0, m_defaultCargoSwapHoldFrames);

        /// <summary>按抬升轴写入平台高度。</summary>
        public void ApplyPlatformHeight(float height)
        {
            if (m_platform == null)
                return;

            Vector3 axis = LiftAxisInParent;
            Vector3 local = m_platform.localPosition;
            local -= axis * Vector3.Dot(local, axis);
            local += axis * height;
            m_platform.localPosition = local;
        }

        /// <summary>首 Clip 之前：车体 Home + 空载平台高度。</summary>
        public void ApplyDefaultTravelPose()
        {
            ApplyHomePose("潜伏车首 Clip 之前");
            ApplyPlatformHeight(m_defaultPlatformEmptyHeight);
        }

        /// <summary>将本组件默认值写入 Clip（新增 Clip 时调用）。</summary>
        public void ApplyClipDefaults(LatentAgvClipData data)
        {
            if (data == null)
                return;

            data.PlatformEmptyHeight = m_defaultPlatformEmptyHeight;
            data.PlatformLoadedTravelHeight = m_defaultPlatformLoadedTravelHeight;
            data.PlatformPlaceHeight = m_defaultPlatformPlaceHeight;
            data.PlatformLiftHeight = m_defaultPlatformLiftHeight;
            data.CargoSwapHoldFrames = DefaultCargoSwapHoldFrames;
            data.PlatformSpeed = m_platformSpeed;
            data.MoveSpeed = MoveSpeed;
            data.RotateSpeed = RotateSpeed;
        }
    }
}
