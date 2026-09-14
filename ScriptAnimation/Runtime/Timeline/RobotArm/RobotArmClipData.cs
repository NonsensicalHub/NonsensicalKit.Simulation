using System;
using System.Collections.Generic;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>机械臂 TCP 点位（世界坐标）；场景物体优先用 Clip 上对应的 WaypointTargets。</summary>
    [Serializable]
    public class RobotArmWaypoint
    {
        [Tooltip("勾选后使用下方坐标/欧拉；若已绑定对应 Target 则始终用 Target（不必勾选）")]
        [InspectorLabel("指定坐标")]
        public bool HasPoint;

        [InspectorLabel("坐标")]
        public Vector3 Point;

        [InspectorLabel("欧拉角")]
        public Vector3 Euler;

        [Tooltip("相对 Target / Point 的世界偏移")]
        [InspectorLabel("偏移")]
        public Vector3 Offset;

        [Tooltip("勾选后本点使用下方第六轴目标角；未勾选则继承前序点 / 起点 J6")]
        [InspectorLabel("指定第六轴角")]
        public bool UseJ6Angle;

        [Tooltip("第六轴（J6）目标角（度，相对 Rest）")]
        [InspectorLabel("第六轴角")]
        public float J6Angle;
    }

    [Serializable]
    public class RobotArmClipData
    {
        [Header("点位链表（依次移动）")]
        [Tooltip("与 Clip 上各 Transform 一一对应；通常只需绑 Transform，此处可填偏移。坐标仅在无 Transform 时使用。")]
        [HideInInspector]
        public List<RobotArmWaypoint> Waypoints = new List<RobotArmWaypoint>
        {
            new RobotArmWaypoint()
        };

        [Tooltip("首点时 Target 时是否用坐标作途经点；路径起止一律为 Home")]
        [InspectorLabel("首点用坐标（兼容）")]
        [HideInInspector]
        public bool HasStartPoint;

        [Header("角速度 / 角加速度")]
        [InspectorLabel("关节角速度")]
        public float JointSpeed = 60f;

        [InspectorLabel("关节角加速度")]
        public float JointAcceleration = 90f;

        [InspectorLabel("自动同步时长")]
        public bool AutoSyncDuration = true;

        [Header("调试")]
        [Tooltip("开启后禁用 IK / 前序链缓存，每帧完整重解并输出 [RobotArm DBG] 日志")]
        [InspectorLabel("强制播放（禁缓存）")]
        public bool DebugForcePlayback;

        public int WaypointCount => Waypoints != null ? Waypoints.Count : 0;

        public RobotArmWaypoint GetWaypoint(int index)
        {
            if (Waypoints == null || index < 0 || index >= Waypoints.Count)
                return null;
            return Waypoints[index];
        }
    }
}
