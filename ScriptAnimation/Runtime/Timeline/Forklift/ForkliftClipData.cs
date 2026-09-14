using System;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    [Serializable]
    public class ForkliftClipData
    {
        [InspectorLabel("模式")]
        public ForkliftMode Mode = ForkliftMode.PickUp;

        [InspectorLabel("终点偏移")]
        public Vector3 DestinationOffset;

        [Tooltip("开启后车头朝向取驶向货点方向的反方向（如地牛：车头背对货位、倒车取放，货叉仍朝向货点）。")]
        [InspectorLabel("倒车朝向")]
        public bool ReverseFacing;

        [Header("货叉高度（沿货叉节点本地抬升轴；新增 Clip 时从 ForkliftAnim 默认值写入）")]
        [Tooltip("开场/前进前的行驶货叉高度")]
        [InspectorLabel("开始行驶货叉高度")]
        public float ForkStartTravelHeight = 0.15f;

        [Tooltip("后退结束后回到的行驶货叉高度")]
        [InspectorLabel("结束行驶货叉高度")]
        public float ForkEndTravelHeight = 0.15f;

        [Tooltip("插入货架 / 放货落地时的货叉高度")]
        [InspectorLabel("放货/插入高度")]
        public float ForkPlaceHeight;

        [Tooltip("载货抬起后的货叉高度")]
        [InspectorLabel("载货抬起高度")]
        public float ForkLiftHeight = 0.5f;

        [Header("货物显隐切换")]
        [Tooltip("货叉与货位重叠时的停顿帧数（取货：到达 Place 尚未继续抬到 Lift；放货：放到 Place 尚未离开或继续下降），供车上货与地面货显隐切换。按 60fps 换算时长。")]
        [InspectorLabel("货物切换停顿帧数")]
        public int CargoSwapHoldFrames = 5;

        [Header("速度 / 距离")]
        [InspectorLabel("移动速度")]
        public float MoveSpeed = 1.5f;

        [InspectorLabel("货叉速度")]
        public float ForkSpeed = 0.8f;

        [Tooltip("度/秒。")]
        [InspectorLabel("转向速度")]
        public float RotateSpeed = 90f;

        [InspectorLabel("接近距离")]
        public float ApproachDistance = 1.2f;

        [InspectorLabel("自动同步时长")]
        public bool AutoSyncDuration = true;
    }
}
