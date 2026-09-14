using System;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 贝塞尔直角弯：在路网拐点处二次贝塞尔切弯，结束于出弯点并朝向下一段。
    /// 须配置 PrevNode →CornerNode →NextNode；开场位姿取前序 Clip 结束。
    /// </summary>
    [Serializable]
    public class BezierCornerClipData
    {
        [Tooltip("控制点距拐点的最大距离。")]
        [InspectorLabel("提前转弯距离")]
        public float EarlyTurnDistance = 1f;

        [InspectorLabel("移动速度")]
        public float MoveSpeed = 2f;

        [InspectorLabel("移动曲线")]
        public AnimationCurve MoveCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Tooltip("开启后车头朝向取前进方向的反方向。")]
        [InspectorLabel("反向行驶")]
        public bool ReverseFacing;

        [InspectorLabel("自动同步时长")]
        public bool AutoSyncDuration = true;
    }
}
