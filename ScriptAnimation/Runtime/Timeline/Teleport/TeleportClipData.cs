using System;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>瞬移落点后的水平朝向策略。</summary>
    public enum TeleportFacingMode
    {
        /// <summary>对齐目标 PathNode 的水平 yaw。</summary>
        [InspectorName("对齐目标节点")]
        FaceNode = 0,

        /// <summary>保持前序 Clip 结束朝向（无前序则用当前 Body）。</summary>
        [InspectorName("保持前序朝向")]
        KeepPrevious = 1,

        /// <summary>使用自定义绕上方轴的 yaw（度）。</summary>
        [InspectorName("自定义偏航角")]
        CustomYaw = 2
    }

    /// <summary>
    /// 瞬移 Clip 参数：瞬间落到目标节点或世界坐标，便于同一对象循环复用动画。
    /// 场景对象请用 TeleportClip 上的 ExposedReference；也可改用下方世界坐标。
    /// </summary>
    [Serializable]
    public class TeleportClipData
    {
        [Tooltip("开启后忽略目标节点，直接使用世界坐标作为落点")]
        [InspectorLabel("使用世界坐标")]
        public bool UseWorldPosition;

        [Tooltip("作为瞬移落点的世界坐标")]
        [InspectorLabel("世界坐标")]
        public Vector3 WorldPosition;

        [Tooltip("相对目标节点或世界坐标的偏移")]
        [InspectorLabel("落点偏移")]
        public Vector3 DestinationOffset;

        [Tooltip("瞬移后朝向（由 PathMoveActor 上方/前方轴决定水平面）")]
        [InspectorLabel("朝向模式")]
        public TeleportFacingMode FacingMode = TeleportFacingMode.FaceNode;

        [Tooltip("朝向模式为「自定义偏航角」时，绕上方轴的 yaw（度）")]
        [InspectorLabel("自定义偏航角")]
        public float CustomYawDegrees;

        [Tooltip("开启后「按速度刷新全轨时长」会把本 Clip 重置为默认占位帧数；默认关闭以便自由拖拽时长")]
        [InspectorLabel("自动同步时长")]
        public bool AutoSyncDuration;
    }
}
