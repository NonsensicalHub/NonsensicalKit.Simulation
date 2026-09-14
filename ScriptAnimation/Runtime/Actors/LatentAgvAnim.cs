using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 潜伏车（举升 AGV）脚本动画：在 <see cref="PathMoveActor"/> 上增加举升平台。
    /// 取放货见 <see cref="LatentAgvClip"/>：车体先用 PathMove 移到货下方，本 Clip 只做平台升降（无货点 / 接近距离）。
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

        [Tooltip("开场时的平台行驶高度。")]
        [InspectorLabel("开始行驶平台高度")]
        [SerializeField] private float m_defaultPlatformStartTravelHeight = 0.15f;
        [Tooltip("动作结束后回到的平台行驶高度。")]
        [InspectorLabel("结束行驶平台高度")]
        [SerializeField] private float m_defaultPlatformEndTravelHeight = 0.15f;
        [Tooltip("插入货架 / 放货落地时的平台高度。")]
        [InspectorLabel("放货/插入高度")]
        [SerializeField] private float m_defaultPlatformPlaceHeight;
        [Tooltip("载货抬起后的平台高度。")]
        [InspectorLabel("载货抬起高度")]
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

        public float DefaultPlatformStartTravelHeight => m_defaultPlatformStartTravelHeight;
        public float DefaultPlatformEndTravelHeight => m_defaultPlatformEndTravelHeight;
        public float DefaultPlatformPlaceHeight => m_defaultPlatformPlaceHeight;
        public float DefaultPlatformLiftHeight => m_defaultPlatformLiftHeight;
        public int DefaultCargoSwapHoldFrames => Mathf.Max(0, m_defaultCargoSwapHoldFrames);

        /// <summary>将本组件默认值写入 Clip（新增 Clip 时调用）。</summary>
        public void ApplyClipDefaults(LatentAgvClipData data)
        {
            if (data == null)
                return;

            data.PlatformStartTravelHeight = m_defaultPlatformStartTravelHeight;
            data.PlatformEndTravelHeight = m_defaultPlatformEndTravelHeight;
            data.PlatformPlaceHeight = m_defaultPlatformPlaceHeight;
            data.PlatformLiftHeight = m_defaultPlatformLiftHeight;
            data.CargoSwapHoldFrames = DefaultCargoSwapHoldFrames;
            data.PlatformSpeed = m_platformSpeed;
        }
    }
}
