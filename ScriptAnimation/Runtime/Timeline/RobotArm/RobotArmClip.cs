using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation
{
    [Serializable]
    public class RobotArmWaypointTarget
    {
        [Tooltip("场景点位（ScriptAnimPoint）；解析成功时覆盖对应 Waypoint 的坐标")]
        public ExposedReference<ScriptAnimPoint> Target;
    }

    /// <summary>
    /// 六轴机械臂点位链表 Clip：配置若干跟随目标（场景 Target / TCP 点位）。
    /// 路径始终为：默认姿态（Home）→ 点位… → 默认姿态（Home）。
    /// 中间状态按相邻目标位置插值，再以独立 IK 跟随的方式跟踪该插值目标。
    /// 各点可选手动指定第六轴（J6）目标角；未指定时继承前序。
    /// </summary>
    [Serializable]
    public class RobotArmClip : PlayableAsset, ITimelineClipAsset, IDurationResolvable
    {
        [HideInInspector]
        [Tooltip("与 Data.Waypoints 一一对应的场景点位；解析成功时覆盖该点坐标")]
        public List<RobotArmWaypointTarget> WaypointTargets = new List<RobotArmWaypointTarget>();

        public RobotArmClipData Data = new RobotArmClipData();

        [NonSerialized] RobotArmSampler.PlanCache m_planCache;

        internal RobotArmSampler.PlanCache CachedPlans =>
            m_planCache ??= new RobotArmSampler.PlanCache();

        /// <summary>Inspector / 编辑器改参后清除 IK 计划缓存。</summary>
        public void InvalidatePlanCache() => CachedPlans.Invalidate();

        public ClipCaps clipCaps => ClipCaps.ClipIn | ClipCaps.SpeedMultiplier;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<RobotArmBehaviour>.Create(graph);
            RobotArmBehaviour behaviour = playable.GetBehaviour();
            behaviour.Data = Data;
            behaviour.ClipAsset = this;

            IExposedPropertyTable resolver = graph.GetResolver();
            behaviour.SetWaypointTargets(ResolveWaypointTargets(resolver));
            return playable;
        }

        public float ResolveDuration(in DurationResolveContext context)
        {
            var anim = context.TrackBinding as RobotArmAnim;
            if (anim == null || Data == null)
                return -1f;

            Transform[] targets = ResolveWaypointTargets(context.Resolver);
            var fallback = RobotArmSampler.ResolvePreviousClipEnd(
                anim, context.TimelineClip, context.Resolver);

            if (!RobotArmSampler.TryBuildPathPlans(
                    this, anim, Data, targets, fallback,
                    out RobotArmSampler.PathMotionPlans path))
                return -1f;

            return path.Duration;
        }

        /// <summary>解析为 Data.Waypoints 对齐的 Target 数组。</summary>
        public Transform[] ResolveWaypointTargets(IExposedPropertyTable resolver)
        {
            EnsureWaypointTargetCount();
            int count = Data != null ? Mathf.Max(1, Data.WaypointCount) : 1;
            var result = new Transform[count];

            if (WaypointTargets != null && WaypointTargets.Count > 0)
            {
                for (int i = 0; i < count; i++)
                {
                    if (i >= WaypointTargets.Count || resolver == null)
                        continue;
                    result[i] = ScriptAnimPointUtility.AsTransform(
                        ScriptAnimPointUtility.Resolve(WaypointTargets[i].Target, resolver));
                }
            }

            return result;
        }

        /// <summary>确保 WaypointTargets 与 Waypoints 等长（编辑器增删点位时用）。</summary>
        public void EnsureWaypointTargetCount()
        {
            int count = Data != null ? Mathf.Max(1, Data.WaypointCount) : 1;
            WaypointTargets ??= new List<RobotArmWaypointTarget>();
            while (WaypointTargets.Count < count)
                WaypointTargets.Add(new RobotArmWaypointTarget());
            while (WaypointTargets.Count > count)
                WaypointTargets.RemoveAt(WaypointTargets.Count - 1);
        }

        /// <summary>设置指定下标的场景 Target（会扩展列表）。</summary>
        public void SetWaypointTarget(int index, ExposedReference<ScriptAnimPoint> target)
        {
            if (index < 0)
                return;
            if (Data != null)
            {
                Data.Waypoints ??= new List<RobotArmWaypoint>();
                while (Data.Waypoints.Count <= index)
                    Data.Waypoints.Add(new RobotArmWaypoint());
            }

            EnsureWaypointTargetCount();
            while (WaypointTargets.Count <= index)
                WaypointTargets.Add(new RobotArmWaypointTarget());
            WaypointTargets[index].Target = target;
        }

        /// <summary>末点 Target（兼容旧调用）。</summary>
        public ExposedReference<ScriptAnimPoint> EndTarget
        {
            get
            {
                EnsureWaypointTargetCount();
                int last = WaypointTargets.Count - 1;
                return last >= 0 ? WaypointTargets[last].Target : default;
            }
            set
            {
                int last = Mathf.Max(0, (Data != null ? Data.WaypointCount : 1) - 1);
                SetWaypointTarget(last, value);
            }
        }

        /// <summary>首点 Target（兼容旧调用）。</summary>
        public ExposedReference<ScriptAnimPoint> StartTarget
        {
            get
            {
                EnsureWaypointTargetCount();
                return WaypointTargets.Count > 0 ? WaypointTargets[0].Target : default;
            }
            set => SetWaypointTarget(0, value);
        }
    }
}
