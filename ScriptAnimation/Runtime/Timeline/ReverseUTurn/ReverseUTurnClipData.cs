using System;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 倒车掉头：在拐点处倒车旋转并对准下一段，再贝塞尔出弯。
    /// 须配置 PrevNode →CornerNode →NextNode；开场位姿取前序 Clip 结束。
    /// </summary>
    [Serializable]
    public class ReverseUTurnClipData
    {
        [Tooltip("倒车距离。")]
        [InspectorLabel("后退距离")]
        public float BackDistance = 0.8f;

        [InspectorLabel("提前转弯距离")]
        public float EarlyTurnDistance = 1f;

        [InspectorLabel("移动速度")]
        public float MoveSpeed = 2f;

        [InspectorLabel("旋转速度")]
        public float RotateSpeed = 90f;

        [InspectorLabel("移动曲线")]
        public AnimationCurve MoveCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [InspectorLabel("反向行驶")]
        public bool ReverseFacing;

        [InspectorLabel("自动同步时长")]
        public bool AutoSyncDuration = true;
    }
}
