using System;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>原地旋转方向（绕世界 +Y，俯视）。</summary>
    public enum RotateDirection
    {
        /// <summary>俯视顺时针（Unity 正 yaw）。</summary>
        [InspectorName("顺时针")]
        Clockwise = 0,

        /// <summary>俯视逆时针。</summary>
        [InspectorName("逆时针")]
        CounterClockwise = 1
    }

    /// <summary>原地旋转时的位置处理方式。</summary>
    public enum RotatePositionMode
    {
        /// <summary>维持开场落点位置（当前默认行为）。</summary>
        [InspectorName("原地旋转（维持位置）")]
        HoldPosition = 0,

        /// <summary>不写入位置，仅更新旋转。</summary>
        [InspectorName("仅旋转（不写入位置）")]
        RotationOnly = 1
    }

    /// <summary>
    /// 原地旋转 Clip 参数：默认维持开场落点；也可仅写入旋转。
    /// 当前 yaw = 起始偏航角 + 方向×角度 × clip 进度（确定性，不读 Body）。
    /// </summary>
    [Serializable]
    public class RotateClipData
    {
        [Tooltip("原地旋转：每帧写回开场落点；仅旋转：不写位置，只改朝向。")]
        [InspectorLabel("位置模式")]
        public RotatePositionMode PositionMode = RotatePositionMode.HoldPosition;

        [Tooltip("开场偏航角（度，绕世界 +Y）。当前角度 = 起始 + 方向×角度 × 进度。")]
        [InspectorLabel("起始偏航角")]
        public float StartYawDegrees;

        [Tooltip("绕世界上方向旋转的角度（度）。360 为一圈，可大于360。")]
        [InspectorLabel("旋转角度")]
        public float AngleDegrees = 360f;

        [InspectorLabel("旋转方向")]
        public RotateDirection Direction = RotateDirection.Clockwise;

        [Tooltip("度/秒。")]
        [InspectorLabel("旋转速度")]
        public float RotateSpeed = 90f;

        [InspectorLabel("旋转曲线")]
        public AnimationCurve RotateCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [InspectorLabel("自动同步时长")]
        public bool AutoSyncDuration = true;
    }
}
