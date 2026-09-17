using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 潜伏车取放货参数。绑定移动点；取货抬货后驶离货位，放货先转向再驶入货位。
    /// </summary>
    [Serializable]
    public class LatentAgvClipData
    {
        [InspectorLabel("模式")]
        public ForkliftMode Mode = ForkliftMode.PickUp;

        [Header("平台高度（沿平台节点本地抬升轴；新增 Clip 时从 LatentAgvAnim 默认值写入）")]
        [Tooltip("空载时的平台高度（取货开场 / 放货结束）")]
        [InspectorLabel("空载高度")]
        [FormerlySerializedAs("PlatformStartTravelHeight")]
        public float PlatformEmptyHeight = 0.15f;

        [Tooltip("载货行驶时的平台高度（取货结束 / 放货开场）")]
        [InspectorLabel("载货行驶高度")]
        [FormerlySerializedAs("PlatformEndTravelHeight")]
        public float PlatformLoadedTravelHeight = 0.15f;

        [Tooltip("货物放置 / 插入货位时的平台高度")]
        [InspectorLabel("货物放置高度")]
        public float PlatformPlaceHeight;

        [Tooltip("货物抬起后的平台高度（驶入 / 驶离货位时保持）")]
        [InspectorLabel("货物抬起高度")]
        public float PlatformLiftHeight = 0.5f;

        [Header("货物显隐切换")]
        [Tooltip("平台与货位重叠时的停顿帧数（取货：到达放置高度尚未继续抬到抬起高度；放货：放到放置高度尚未继续下降），供车上货与地面货显隐切换。按 60fps 换算时长。")]
        [InspectorLabel("货物切换停顿帧数")]
        public int CargoSwapHoldFrames = 5;

        [Header("速度")]
        [InspectorLabel("移动速度")]
        public float MoveSpeed = 1.5f;

        [InspectorLabel("平台速度")]
        public float PlatformSpeed = 0.8f;

        [Tooltip("度/秒。仅放货开场转向使用。")]
        [InspectorLabel("转向速度")]
        public float RotateSpeed = 90f;

        [InspectorLabel("自动同步时长")]
        public bool AutoSyncDuration = true;
    }
}
