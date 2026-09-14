using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation
{
    [Serializable]
    public class RobotArm5WaypointTarget
    {
        [Tooltip("场景点位（ScriptAnimPoint）；解析成功时覆盖对应 Waypoint 的坐标")]
        public ExposedReference<ScriptAnimPoint> Target;
    }

    /// <summary>
    /// 五轴机械臂点位链表 Clip。路径：Home → 点位… → Home。
    /// 各点可选手动指定第五轴（J5）目标角；未指定时继承前序。
    /// 末端朝下：保持 J5 在目标正上方（J4 俯仰），与六轴 RobotArmClip 互不影响。
    /// </summary>
    [Serializable]
    public class RobotArm5Clip : PlayableAsset, ITimelineClipAsset, IDurationResolvable
    {
        [HideInInspector]
        [Tooltip("与 Data.Waypoints 一一对应的场景点位；解析成功时覆盖该点坐标")]
        public List<RobotArm5WaypointTarget> WaypointTargets = new List<RobotArm5WaypointTarget>();

        public RobotArm5ClipData Data = new RobotArm5ClipData();

        [NonSerialized] RobotArm5Sampler.PlanCache m_planCache;

        internal RobotArm5Sampler.PlanCache CachedPlans =>
            m_planCache ??= new RobotArm5Sampler.PlanCache();

        public void InvalidatePlanCache() => CachedPlans.Invalidate();

        public ClipCaps clipCaps => ClipCaps.ClipIn | ClipCaps.SpeedMultiplier;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<RobotArm5Behaviour>.Create(graph);
            RobotArm5Behaviour behaviour = playable.GetBehaviour();
            behaviour.Data = Data;
            behaviour.ClipAsset = this;

            IExposedPropertyTable resolver = graph.GetResolver();
            behaviour.SetWaypointTargets(ResolveWaypointTargets(resolver));
            return playable;
        }

        public float ResolveDuration(in DurationResolveContext context)
        {
            var anim = context.TrackBinding as RobotArm5Anim;
            if (anim == null || Data == null)
                return -1f;

            Transform[] targets = ResolveWaypointTargets(context.Resolver);
            var fallback = RobotArm5Sampler.ResolvePreviousClipEnd(
                anim, context.TimelineClip, context.Resolver);

            if (!RobotArm5Sampler.TryBuildPathPlans(
                    this, anim, Data, targets, fallback,
                    out RobotArm5Sampler.PathMotionPlans path))
                return -1f;

            return path.Duration;
        }

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

        public void EnsureWaypointTargetCount()
        {
            int count = Data != null ? Mathf.Max(1, Data.WaypointCount) : 1;
            WaypointTargets ??= new List<RobotArm5WaypointTarget>();
            while (WaypointTargets.Count < count)
                WaypointTargets.Add(new RobotArm5WaypointTarget());
            while (WaypointTargets.Count > count)
                WaypointTargets.RemoveAt(WaypointTargets.Count - 1);
        }

        public void SetWaypointTarget(int index, ExposedReference<ScriptAnimPoint> target)
        {
            if (index < 0)
                return;
            if (Data != null)
            {
                Data.Waypoints ??= new List<RobotArm5Waypoint>();
                while (Data.Waypoints.Count <= index)
                    Data.Waypoints.Add(new RobotArm5Waypoint());
            }

            EnsureWaypointTargetCount();
            while (WaypointTargets.Count <= index)
                WaypointTargets.Add(new RobotArm5WaypointTarget());
            WaypointTargets[index].Target = target;
        }

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
    }
}
