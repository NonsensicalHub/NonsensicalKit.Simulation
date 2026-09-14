using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 世界旋转锁定：将目标 Transform 的世界旋转维持为 Lock Clip 上配置的固定欧拉角。
    /// 与车体组件无关——可挂在任意物体上；Timeline 用 <see cref="ScriptDedicatedTrack"/> 绑定本组件，
    /// 以 <see cref="WorldRotationLockClip"/> 标定生效区间。未覆盖区间不写入。
    /// 若 Target 位于某移动 Actor 层级下，该移动轨在位移后会再写一次，避免父节点转动带偏。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("ScriptAnimation/世界旋转锁定 (WorldRotationLockAnim)")]
    public class WorldRotationLockAnim : ScriptAnimActor
    {
        [Tooltip("需要锁定世界旋转的节点（如载物平台、货叉等）。")]
        [InspectorLabel("目标")]
        [SerializeField] private Transform m_target;

        public Transform Target => m_target;

        /// <summary>直接写入世界旋转；仅在 Lock Clip 覆盖且已捕获保持值时由 Mixer 调用。</summary>
        public void ApplyWorldRotation(Quaternion worldRotation)
        {
            if (m_target == null)
                return;
            m_target.rotation = worldRotation;
        }

        /// <summary>读取目标当前世界欧拉角，供 Clip / Inspector 预览。</summary>
        public bool TryGetCurrentWorldEuler(out Vector3 eulerAngles)
        {
            if (m_target == null)
            {
                eulerAngles = default;
                return false;
            }

            eulerAngles = m_target.eulerAngles;
            return true;
        }
    }
}
