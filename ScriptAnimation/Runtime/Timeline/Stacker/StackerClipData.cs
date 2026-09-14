using System;
using NonsensicalKit.Core;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    [Serializable]
    public class StackerClipData
    {
        [Header("货位坐标（层 / 列 / 排 / 深）")]
        [Tooltip("是否指定起点坐标；关闭则用前序 StackerClip 终点 / Home（须已捕获）")]
        [InspectorLabel("指定起点货位")]
        public bool HasStartCell;

        [Tooltip("起点货位索引：X=层 Y=列 Z=排 W=深")]
        [InspectorLabel("起点货位")]
        public Int4 StartCell;

        [Tooltip("终点货位索引：X=层 Y=列 Z=排 W=深")]
        [InspectorLabel("终点货位")]
        public Int4 EndCell;

        [Tooltip("终点世界坐标额外偏移")]
        [InspectorLabel("终点偏移")]
        public Vector3 DestinationOffset;

        [Header("速度 / 加速度")]
        [InspectorLabel("行走速度")]
        public float TravelSpeed = 2f;

        [InspectorLabel("行走加速度")]
        public float TravelAcceleration = 1.5f;

        [InspectorLabel("升降速度")]
        public float LiftSpeed = 1.2f;

        [InspectorLabel("升降加速度")]
        public float LiftAcceleration = 1f;

        [InspectorLabel("自动同步时长")]
        public bool AutoSyncDuration = true;
    }
}
