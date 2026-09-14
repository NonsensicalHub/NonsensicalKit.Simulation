using System;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 直线移动 Clip 参数：A→B 直移，不寻路、不改朝向。
    /// 场景对象请用 DirectMoveClip 上的 ExposedReference；也可改用下方世界坐标。
    /// </summary>
    [Serializable]
    public class DirectMoveClipData
    {
        [Tooltip("开启后忽略起点 ScriptAnimPoint，直接使用起点世界坐标")]
        [InspectorLabel("起点用世界坐标")]
        public bool UseStartWorldPosition;

        [InspectorLabel("起点世界坐标")]
        public Vector3 StartWorldPosition;

        [Tooltip("开启后忽略终点 ScriptAnimPoint，直接使用终点世界坐标")]
        [InspectorLabel("终点用世界坐标")]
        public bool UseEndWorldPosition;

        [InspectorLabel("终点世界坐标")]
        public Vector3 EndWorldPosition;

        [InspectorLabel("终点偏移")]
        public Vector3 DestinationOffset;

        [InspectorLabel("移动速度")]
        public float MoveSpeed = 2f;

        [InspectorLabel("移动曲线")]
        public AnimationCurve MoveCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [InspectorLabel("自动同步时长")]
        public bool AutoSyncDuration = true;
    }
}
