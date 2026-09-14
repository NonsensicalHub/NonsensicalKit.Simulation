using System;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    [Serializable]
    public class ShuttleClipData
    {
        [InspectorLabel("模式")]
        public ForkliftMode Mode = ForkliftMode.PickUp;

        [Tooltip("沿夹爪伸出轴的方向：右=正向，左=负向。不决定动哪只夹爪。")]
        [InspectorLabel("伸出方向")]
        public ShuttleSide Side = ShuttleSide.Right;

        [Header("夹爪伸出（沿各夹爪本地 ClawAxisLocal）")]
        [Tooltip("本 Clip 是否驱动左夹爪")]
        [InspectorLabel("左夹爪")]
        public bool ClawLeft = true;

        [Tooltip("本 Clip 是否驱动右夹爪")]
        [InspectorLabel("右夹爪")]
        public bool ClawRight = true;

        [Tooltip("收回时的夹爪偏移（通常 0）")]
        [InspectorLabel("夹爪收回偏移")]
        public float ClawRetracted;

        [Tooltip("侧伸距离（正值）。符号由「伸出方向」决定：右+距离，左=−距离。")]
        [InspectorLabel("夹爪伸出距离")]
        public float ClawExtended = 0.8f;

        /// <summary>带方向的伸出到位偏移：右为正，左为负。</summary>
        public float SignedClawExtended =>
            (Side == ShuttleSide.Left ? -1f : 1f) * Mathf.Abs(ClawExtended);

        public bool UsesClawLeft(ShuttleAnim anim) =>
            ClawLeft && anim != null && anim.ClawLeft != null;

        public bool UsesClawRight(ShuttleAnim anim) =>
            ClawRight && anim != null && anim.ClawRight != null;

        public bool UsesAnyClaw(ShuttleAnim anim) =>
            UsesClawLeft(anim) || UsesClawRight(anim);

        [Header("夹紧（左右独立开关，沿各夹紧本地 ClampAxisLocal 向内）")]
        [Tooltip("本 Clip 是否驱动左夹紧")]
        [InspectorLabel("左夹紧")]
        public bool ClampLeft = true;

        [Tooltip("本 Clip 是否驱动右夹紧")]
        [InspectorLabel("右夹紧")]
        public bool ClampRight = true;

        [Tooltip("松开时的夹紧偏移（通常 0）")]
        [InspectorLabel("夹紧松开偏移")]
        public float ClampReleased;

        [Tooltip("夹持到位时的夹紧偏移（向内为正，相对各夹紧本地 ClampAxisLocal）")]
        [InspectorLabel("夹紧夹持偏移")]
        public float ClampClosed = 0.08f;

        public bool UsesClampLeft(ShuttleAnim anim) =>
            ClampLeft && anim != null && anim.ClampLeft != null;

        public bool UsesClampRight(ShuttleAnim anim) =>
            ClampRight && anim != null && anim.ClampRight != null;

        public bool UsesAnyClamp(ShuttleAnim anim) =>
            UsesClampLeft(anim) || UsesClampRight(anim);

        [Header("速度")]
        [InspectorLabel("夹爪速度")]
        public float ClawSpeed = 0.8f;

        [InspectorLabel("夹紧速度")]
        public float ClampSpeed = 0.4f;

        [InspectorLabel("拨爪转速")]
        public float PaddleSpeed = 180f;

        [InspectorLabel("自动同步时长")]
        public bool AutoSyncDuration = true;
    }
}
