using System;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>三点转向方向：相对开场车头，转到道路左/右侧（俯视）。</summary>
    public enum ThreePointTurnDirection
    {
        /// <summary>车头顺时针转 90°，倒车落在原车道左侧。</summary>
        [InspectorName("右转")]
        Right = 0,

        /// <summary>车头逆时针转 90°，倒车落在原车道右侧。</summary>
        [InspectorName("左转")]
        Left = 1
    }

    /// <summary>
    /// 窄道三点转向：只配左/右转。开场位姿取前序 PathMove 等结束位姿。
    /// 前进 → 向对侧倒车圆弧转 90° 对准目标 → 再前进回到开场位置（朝向已转正）。
    /// </summary>
    [Serializable]
    public class ThreePointTurnClipData
    {
        [Tooltip("相对开场车头的转向。右转：先前进，再向左侧倒车对准右侧目标，然后前进回到开场点。")]
        [InspectorLabel("转向方向")]
        public ThreePointTurnDirection Direction = ThreePointTurnDirection.Right;

        [Tooltip("前进、倒车圆弧半径、再前进共用此距离。")]
        [InspectorLabel("机动距离")]
        public float ManeuverDistance = 0.8f;

        [InspectorLabel("移动速度")]
        public float MoveSpeed = 2f;

        [Tooltip("度/秒。倒车圆弧转向用。")]
        [InspectorLabel("旋转速度")]
        public float RotateSpeed = 90f;

        [InspectorLabel("移动曲线")]
        public AnimationCurve MoveCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [InspectorLabel("自动同步时长")]
        public bool AutoSyncDuration = true;
    }
}
