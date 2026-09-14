using System;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// PathMove Clip 动态参数。
    /// 场景对象请用 PathMoveClip 上的 ExposedReference。
    /// </summary>
    [Serializable]
    public class PathMoveClipData
    {
        [InspectorLabel("终点偏移")]
        public Vector3 DestinationOffset;

        [InspectorLabel("移动速度")]
        public float MoveSpeed = 2f;

        [InspectorLabel("移动类型")]
        public PathMoveMode MoveMode = PathMoveMode.FaceWhileMove;

        [Tooltip("度/秒。")]
        [InspectorLabel("旋转速度")]
        public float RotateSpeed = 90f;

        [Tooltip("开启后车头朝向取前进方向的反方向（双向 AGV 倒车/反向行驶，车体不必原地掉头）。")]
        [InspectorLabel("反向行驶")]
        public bool ReverseFacing;

        [InspectorLabel("移动曲线")]
        public AnimationCurve MoveCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [InspectorLabel("自动同步时长")]
        public bool AutoSyncDuration = true;
    }
}
