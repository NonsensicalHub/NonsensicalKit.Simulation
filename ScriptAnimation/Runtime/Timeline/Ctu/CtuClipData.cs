using System;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    [Serializable]
    public class CtuClipData
    {
        [InspectorLabel("模式")]
        public ForkliftMode Mode = ForkliftMode.PickUp;

        [InspectorLabel("终点偏移")]
        public Vector3 DestinationOffset;

        [Header("升降高度（沿 LiftAxisLocal 本地分量）")]
        [Tooltip("行驶/待机时的升降高度（ReturnLiftToTravel 时回到此高度）")]
        [InspectorLabel("行驶升降高度")]
        public float LiftTravelHeight = 0.15f;

        [Tooltip("取放时的目标升降高度（移动到此高度后侧伸）")]
        [InspectorLabel("取放升降高度")]
        public float LiftPlaceHeight;

        [Header("夹爪伸出（沿 ClawAxisLocal 本地分量）")]
        [Tooltip("收回时的夹爪偏移（通常 0）")]
        [InspectorLabel("夹爪收回偏移")]
        public float ClawRetracted;

        [Tooltip("侧伸到位时的夹爪偏移")]
        [InspectorLabel("夹爪伸出偏移")]
        public float ClawExtended = 0.8f;

        [Header("结束姿态")]
        [Tooltip("取放结束后是否转回行驶角（CtuAnim.RotateTravelAngle）")]
        [InspectorLabel("结束回行驶转角")]
        public bool ReturnRotateToTravel = true;

        [Tooltip("取放结束后是否恢复到默认升降高度（LiftTravelHeight）；关闭则保持 LiftPlaceHeight")]
        [InspectorLabel("结束回行驶高度")]
        public bool ReturnLiftToTravel = true;

        [Header("速度")]
        [InspectorLabel("转台转速")]
        public float RotateSpeed = 90f;

        [InspectorLabel("升降速度")]
        public float LiftSpeed = 0.8f;

        [InspectorLabel("夹爪速度")]
        public float ClawSpeed = 0.8f;

        [InspectorLabel("拨爪转速")]
        public float PaddleSpeed = 180f;

        [InspectorLabel("自动同步时长")]
        public bool AutoSyncDuration = true;
    }
}
