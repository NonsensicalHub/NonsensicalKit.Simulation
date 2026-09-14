using System;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 潜伏车取放货参数。车体已用 PathMove 等到货下方，本 Clip 只做平台升降，无货点 / 接近距离。
    /// </summary>
    [Serializable]
    public class LatentAgvClipData
    {
        [InspectorLabel("模式")]
        public ForkliftMode Mode = ForkliftMode.PickUp;

        [Header("平台高度（沿平台节点本地抬升轴；新增 Clip 时从 LatentAgvAnim 默认值写入）")]
        [Tooltip("开场时的平台行驶高度")]
        [InspectorLabel("开始行驶平台高度")]
        public float PlatformStartTravelHeight = 0.15f;

        [Tooltip("动作结束后回到的平台行驶高度")]
        [InspectorLabel("结束行驶平台高度")]
        public float PlatformEndTravelHeight = 0.15f;

        [Tooltip("插入货架 / 放货落地时的平台高度")]
        [InspectorLabel("放货/插入高度")]
        public float PlatformPlaceHeight;

        [Tooltip("载货抬起后的平台高度")]
        [InspectorLabel("载货抬起高度")]
        public float PlatformLiftHeight = 0.5f;

        [Header("货物显隐切换")]
        [Tooltip("平台与货位重叠时的停顿帧数（取货：到达 Place 尚未继续抬到 Lift；放货：放到 Place 尚未继续下降），供车上货与地面货显隐切换。按 60fps 换算时长。")]
        [InspectorLabel("货物切换停顿帧数")]
        public int CargoSwapHoldFrames = 5;

        [Header("速度")]
        [InspectorLabel("平台速度")]
        public float PlatformSpeed = 0.8f;

        [InspectorLabel("自动同步时长")]
        public bool AutoSyncDuration = true;
    }
}
