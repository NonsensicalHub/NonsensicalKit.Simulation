using System;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 双拐点贝塞尔弯：Prev → CornerA → CornerB → Next，以两拐点为控制点做三次贝塞尔切弯，
    /// 结束后直线到达 NextNode 并朝向下一段。
    /// </summary>
    [Serializable]
    public class BezierDualCornerClipData
    {
        [Tooltip("入弯/出弯点距首/末拐点的最大距离；出弯后仍会直线走到 Next。")]
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
