using System;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    [Serializable]
    public class StackerForkClipData
    {
        [InspectorLabel("模式")]
        public ForkliftMode Mode = ForkliftMode.PickUp;

        [Header("一级货叉偏移（沿 PrimaryForkAxisLocal 本地分量）")]
        [Tooltip("待机/行驶时的货叉偏移")]
        [InspectorLabel("一级行驶偏移")]
        public float ForkTravelOffset;

        [Tooltip("插入货架 / 放货时的货叉偏移")]
        [InspectorLabel("一级放货/插入偏移")]
        public float ForkPlaceOffset = 0.8f;

        [Tooltip("载货抬起后的货叉偏移")]
        [InspectorLabel("一级载货抬起偏移")]
        public float ForkLiftOffset = 1f;

        [Header("二级货叉偏移（沿 SecondaryForkAxisLocal 本地分量；未绑定二级货叉时忽略）")]
        [Tooltip("待机/行驶时的二级货叉偏移")]
        [InspectorLabel("二级行驶偏移")]
        public float SecondaryForkTravelOffset;

        [Tooltip("插入货架 / 放货时的二级货叉偏移")]
        [InspectorLabel("二级放货/插入偏移")]
        public float SecondaryForkPlaceOffset = 0.8f;

        [Tooltip("载货抬起后的二级货叉偏移")]
        [InspectorLabel("二级载货抬起偏移")]
        public float SecondaryForkLiftOffset = 1f;

        [Header("速度")]
        [InspectorLabel("一级货叉速度")]
        public float ForkSpeed = 0.8f;

        [InspectorLabel("二级货叉速度")]
        public float SecondaryForkSpeed = 0.8f;

        [InspectorLabel("自动同步时长")]
        public bool AutoSyncDuration = true;
    }
}
