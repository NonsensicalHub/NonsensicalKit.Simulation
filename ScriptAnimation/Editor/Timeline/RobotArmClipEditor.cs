using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation.Editor
{
    [CustomEditor(typeof(RobotArmClip))]
    public class RobotArmClipEditor : UnityEditor.Editor
    {
        private static readonly HashSet<string> s_skipDataFields = new HashSet<string>
        {
            "Waypoints",
            "HasStartPoint"
        };

        private bool m_showAdvancedCoords;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var clipAsset = (RobotArmClip)target;
            clipAsset.EnsureWaypointTargetCount();
            serializedObject.Update();

            PlayableDirector director = TimelineEditor.inspectedDirector;
            Object binding = null;
            TimelineClip timelineClip = FindTimelineClip(clipAsset, out binding);

            EditorGUILayout.LabelField("点位链表（Transform）", EditorStyles.boldLabel);
            if (director == null)
            {
                EditorGUILayout.HelpBox(
                    "请在 Timeline 窗口中选中该 Clip（需有 PlayableDirector），才能绑定场景 Transform。",
                    MessageType.Warning);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "按顺序拖入场景 Transform，作为跟随目标。\n" +
                    "中间状态按相邻目标的位置/朝向插值，再做与独立跟随相同的 IK。\n" +
                    "首点留空且未勾选「指定起点」时：从前序 Clip 终点或 Home 起步。\n" +
                    "第六轴：须开启 Anim「末端保持向下」才生效。默认继承前序 J6；勾选「指定第六轴角」后本点使用配置角度。",
                    MessageType.Info);
            }

            DrawHomePathHint();
            DrawWaypointTransformList(clipAsset, director);
            EditorGUILayout.Space(4f);

            m_showAdvancedCoords = EditorGUILayout.Foldout(
                m_showAdvancedCoords, "高级：世界坐标 / 偏移（可选）", true);
            if (m_showAdvancedCoords)
                DrawAdvancedWaypoints(clipAsset);

            EditorGUILayout.Space(4f);
            ClipDataInspectorGui.DrawChildren(
                serializedObject.FindProperty("Data"), s_skipDataFields);
            serializedObject.ApplyModifiedProperties();
            clipAsset.EnsureWaypointTargetCount();

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "末端朝下：以绑定的 RobotArmAnim「末端保持向下」为准（第 5 轴连杆 → 世界下方）。\n" +
                "关闭 Anim 末端朝下时，对齐各点 Transform 朝向（中间按朝向插值）。\n" +
                "第六轴扭转：各点「指定第六轴角」可选覆盖；未指定则继承前序 / Home J6。\n" +
                "时长请用 Track「按速度刷新全轨时长」。\n" +
                "排查不动：在 RobotArmAnim 或 Clip 参数里开启「强制播放（禁缓存）」。",
                MessageType.None);

            if (binding is RobotArmAnim anim && clipAsset.Data != null)
            {
                IExposedPropertyTable resolver = director;
                Transform[] targets = clipAsset.ResolveWaypointTargets(resolver);
                Transform firstTarget = RobotArmSampler.GetTarget(targets, 0);
                Transform lastTarget = RobotArmSampler.GetTarget(
                    targets, Mathf.Max(0, clipAsset.Data.WaypointCount - 1));

                if (GUILayout.Button("打印当前姿态（Console）"))
                    anim.LogCurrentPose();

                DrawConstraintGuards(clipAsset, anim, director, lastTarget, targets);
                DrawPathPreview(clipAsset, anim, timelineClip, director, targets, firstTarget);
            }
            else if (timelineClip != null && binding is ScriptAnimActor)
            {
                EditorGUILayout.HelpBox("RobotArmClip 需要绑定 RobotArmAnim。", MessageType.Warning);
            }
        }

        private static void DrawHomePathHint()
        {
            EditorGUILayout.HelpBox(
                "路径：默认姿态（Home）→ 点位… → 默认姿态（Home）。点位均为途经/作业目标。",
                MessageType.Info);
        }

        private void DrawWaypointTransformList(RobotArmClip clipAsset, PlayableDirector director)
        {
            clipAsset.Data.Waypoints ??= new List<RobotArmWaypoint>();
            clipAsset.WaypointTargets ??= new List<RobotArmWaypointTarget>();
            clipAsset.EnsureWaypointTargetCount();

            int count = clipAsset.Data.WaypointCount;
            for (int i = 0; i < count; i++)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                string label = $"点位 {i + 1}";
                DrawWaypointTransformField(clipAsset, director, i, label);

                using (new EditorGUI.DisabledScope(count <= 1))
                {
                    if (GUILayout.Button("−", GUILayout.Width(24f)))
                    {
                        RemoveWaypointAt(clipAsset, director, i);
                        EditorGUILayout.EndHorizontal();
                        EditorGUILayout.EndVertical();
                        GUIUtility.ExitGUI();
                    }
                }

                EditorGUILayout.EndHorizontal();
                DrawWaypointJ6Field(clipAsset, i);
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("+ 添加点位", GUILayout.Width(160f)))
            {
                AddWaypoint(clipAsset);
                GUIUtility.ExitGUI();
            }

            EditorGUILayout.EndHorizontal();
        }

        private static void DrawWaypointJ6Field(RobotArmClip clipAsset, int index)
        {
            RobotArmWaypoint wp = clipAsset.Data.GetWaypoint(index);
            if (wp == null)
                return;

            EditorGUI.indentLevel++;
            EditorGUI.BeginChangeCheck();
            bool useJ6 = EditorGUILayout.Toggle("指定第六轴角", wp.UseJ6Angle);
            float j6Angle = wp.J6Angle;
            if (useJ6)
                j6Angle = EditorGUILayout.FloatField("第六轴角 (°)", j6Angle);
                else
                EditorGUILayout.LabelField("第六轴角", "继承前序 / Home");
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(clipAsset, "Edit RobotArm J6");
                wp.UseJ6Angle = useJ6;
                wp.J6Angle = j6Angle;
                clipAsset.InvalidatePlanCache();
                EditorUtility.SetDirty(clipAsset);
                TimelineEditor.Refresh(RefreshReason.ContentsModified);
            }

            EditorGUI.indentLevel--;
        }

        private static void DrawWaypointTransformField(
            RobotArmClip clipAsset,
            PlayableDirector director,
            int index,
            string label)
        {
            clipAsset.EnsureWaypointTargetCount();
            ExposedReference<ScriptAnimPoint> exposed = clipAsset.WaypointTargets[index].Target;
            ScriptAnimPoint current = director != null
                ? ScriptAnimPointUtility.Resolve(exposed, director)
                : null;

            using (new EditorGUI.DisabledScope(director == null))
            {
                EditorGUI.BeginChangeCheck();
                ScriptAnimPoint next = (ScriptAnimPoint)EditorGUILayout.ObjectField(
                    label, current, typeof(ScriptAnimPoint), true);
                if (!EditorGUI.EndChangeCheck() || director == null)
                    return;

                Undo.RecordObject(clipAsset, "Set RobotArm Waypoint Point");
                Undo.RecordObject(director, "Set RobotArm Waypoint Point");

                ClearExposed(director, exposed);
                if (next != null)
                    clipAsset.WaypointTargets[index].Target = BindExposed(director, next);
                else
                    clipAsset.WaypointTargets[index].Target = default;

                // 绑了 Transform 就不再依赖世界坐标
                RobotArmWaypoint wp = clipAsset.Data.GetWaypoint(index);
                if (wp != null && next != null)
                    wp.HasPoint = false;

                clipAsset.InvalidatePlanCache();
                EditorUtility.SetDirty(clipAsset);
                EditorUtility.SetDirty(director);
                TimelineEditor.Refresh(RefreshReason.ContentsModified);
            }
        }

        private void DrawAdvancedWaypoints(RobotArmClip clipAsset)
        {
            clipAsset.EnsureWaypointTargetCount();
            int count = clipAsset.Data.WaypointCount;
            for (int i = 0; i < count; i++)
            {
                RobotArmWaypoint wp = clipAsset.Data.GetWaypoint(i);
                if (wp == null)
                    continue;

                EditorGUILayout.LabelField($"点位 {i + 1}", EditorStyles.miniBoldLabel);
                EditorGUI.BeginChangeCheck();
                bool hasPoint = EditorGUILayout.Toggle("指定坐标（无点位时）", wp.HasPoint);
                Vector3 point = wp.Point;
                Vector3 euler = wp.Euler;
                Vector3 offset = wp.Offset;
                if (hasPoint)
                {
                    point = EditorGUILayout.Vector3Field("坐标", point);
                    euler = EditorGUILayout.Vector3Field("欧拉角", euler);
                }

                offset = EditorGUILayout.Vector3Field("偏移", offset);
                // 第六轴在主列表已编辑；高级区只保留坐标/偏移
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(clipAsset, "Edit RobotArm Waypoint");
                    wp.HasPoint = hasPoint;
                    wp.Point = point;
                    wp.Euler = euler;
                    wp.Offset = offset;
                    clipAsset.InvalidatePlanCache();
                    EditorUtility.SetDirty(clipAsset);
                    TimelineEditor.Refresh(RefreshReason.ContentsModified);
                }

                EditorGUILayout.Space(2f);
            }
        }

        private static void AddWaypoint(RobotArmClip clipAsset)
        {
            Undo.RecordObject(clipAsset, "Add RobotArm Waypoint");
            clipAsset.Data.Waypoints ??= new List<RobotArmWaypoint>();
            clipAsset.WaypointTargets ??= new List<RobotArmWaypointTarget>();
            clipAsset.Data.Waypoints.Add(new RobotArmWaypoint());
            clipAsset.WaypointTargets.Add(new RobotArmWaypointTarget());
            clipAsset.EnsureWaypointTargetCount();
            clipAsset.InvalidatePlanCache();
            EditorUtility.SetDirty(clipAsset);
            TimelineEditor.Refresh(RefreshReason.ContentsModified);
        }

        private static void RemoveWaypointAt(
            RobotArmClip clipAsset, PlayableDirector director, int index)
        {
            if (clipAsset.Data.WaypointCount <= 1)
                return;

            Undo.RecordObject(clipAsset, "Remove RobotArm Waypoint");
            if (director != null)
                Undo.RecordObject(director, "Remove RobotArm Waypoint");

            if (index >= 0 && index < clipAsset.WaypointTargets.Count)
                ClearExposed(director, clipAsset.WaypointTargets[index].Target);

            if (index >= 0 && index < clipAsset.Data.Waypoints.Count)
                clipAsset.Data.Waypoints.RemoveAt(index);
            if (index >= 0 && index < clipAsset.WaypointTargets.Count)
                clipAsset.WaypointTargets.RemoveAt(index);

            clipAsset.EnsureWaypointTargetCount();
            clipAsset.InvalidatePlanCache();
            EditorUtility.SetDirty(clipAsset);
            if (director != null)
                EditorUtility.SetDirty(director);
            TimelineEditor.Refresh(RefreshReason.ContentsModified);
        }

        private void DrawPathPreview(
            RobotArmClip clipAsset,
            RobotArmAnim anim,
            TimelineClip timelineClip,
            PlayableDirector director,
            Transform[] targets,
            Transform firstTarget)
        {
            var fallback = RobotArmSampler.TimelineStartFallback.None;
            if (timelineClip != null)
                fallback = RobotArmSampler.ResolvePreviousClipEnd(anim, timelineClip, director);

            float[] continuousStart = RobotArmSampler.GetContinuousStartAngles(
                clipAsset.Data, fallback, firstTarget);
            float[] ikSeed = RobotArmSampler.GetIkSeedAngles(continuousStart, fallback);
            bool forceDebug = RobotArmSampler.ShouldForceDebugPlayback(anim, clipAsset.Data);

            if (!RobotArmSampler.TryBuildPathPlans(
                    clipAsset, anim, clipAsset.Data, targets, fallback,
                    out RobotArmSampler.PathMotionPlans path,
                    continuousStart, ikSeed,
                    useCache: !forceDebug))
            {
                EditorGUILayout.HelpBox(
                    "无法解析点位链表。请为各点位绑定 Transform（或在高级里填世界坐标），并确保 Anim 已捕获 Home。",
                    MessageType.Warning);
                return;
            }

            var sb = new System.Text.StringBuilder(512);
            int wpCount = clipAsset.Data.WaypointCount;
            int segCount = path.Segments.Length;
            sb.AppendLine(
                $"点位数 {wpCount}，段数 {segCount}（含 Home→首点、末点→Home），" +
                $"起点角={(continuousStart != null ? "前序终点/Home" : "Home IK")}");

            for (int i = 0; i < wpCount; i++)
            {
                Transform t = RobotArmSampler.GetTarget(targets, i);
                var wp = clipAsset.Data.GetWaypoint(i);
                string j6Text = wp != null && wp.UseJ6Angle
                    ? $"J6={wp.J6Angle:F1}°"
                    : "J6=继承";
                sb.AppendLine($"  [{i + 1}] {(t != null ? t.name : "（坐标/未绑定）")}  {j6Text}");
            }

            sb.AppendLine($"轴数: {anim.JointCount}（定死六轴）");
            for (int s = 0; s < segCount; s++)
            {
                var seg = path.Segments[s];
                sb.AppendLine($"—— 段 {s + 1}（{seg.Duration:F3}s）——");
                for (int j = 0; j < anim.JointCount; j++)
                {
                    float delta = Mathf.Abs(
                        Mathf.DeltaAngle(seg.StartAngles[j], seg.EndAngles[j]));
                    sb.AppendLine(
                        $"  J{j + 1}: {seg.StartAngles[j]:F1}° → {seg.EndAngles[j]:F1}° " +
                        $"(Δ{delta:F1}°) {seg.Joints[j].Duration:F3}s");
                }
            }

            sb.Append($"总时长 Σ = {path.Duration:F3} s");
            EditorGUILayout.HelpBox(sb.ToString(), MessageType.Info);
        }

        private static void DrawConstraintGuards(
            RobotArmClip clipAsset,
            RobotArmAnim anim,
            PlayableDirector director,
            Transform lastTarget,
            Transform[] targets)
        {
            EditorGUILayout.HelpBox(
                anim.KeepTipDown
                    ? "末端约束：使用 Anim「末端保持向下」（第 5 轴连杆方向 → 世界下方）；第 6 轴由点位「指定第六轴角」驱动。"
                    : "末端约束：Anim 已关闭「末端保持向下」，将对齐各点 Transform 朝向；点位 J6 仍会写入第六轴。",
                MessageType.None);

            int missing = 0;
            for (int i = 0; i < clipAsset.Data.WaypointCount; i++)
            {
                Transform t = RobotArmSampler.GetTarget(targets, i);
                var wp = clipAsset.Data.GetWaypoint(i);
                bool ok = t != null ||
                          wp != null && (wp.HasPoint || i == 0 && clipAsset.Data.HasStartPoint);
                if (!ok)
                    missing++;
            }

            if (missing > 0)
            {
                EditorGUILayout.HelpBox(
                    $"有 {missing} 个点位未绑定 Transform（也未填世界坐标）。请拖入场景物体。",
                    MessageType.Warning);
            }

            if (lastTarget == null && anim.IkTarget != null && director != null)
            {
                if (GUILayout.Button($"将末点设为 Anim.IkTarget（{anim.IkTarget.name}）"))
                {
                    Undo.RecordObject(clipAsset, "Bind RobotArmClip End Target");
                    Undo.RecordObject(director, "Bind RobotArmClip End Target");
                    ScriptAnimPoint point = EnsurePoint(anim.IkTarget);
                    clipAsset.EndTarget = BindExposed(director, point);
                    clipAsset.InvalidatePlanCache();
                    EditorUtility.SetDirty(clipAsset);
                    EditorUtility.SetDirty(director);
                    TimelineEditor.Refresh(RefreshReason.ContentsModified);
                }
            }
        }

        private static void ClearExposed(PlayableDirector director, ExposedReference<ScriptAnimPoint> exposed)
        {
            if (director == null || exposed.exposedName == default)
                return;
            director.ClearReferenceValue(exposed.exposedName);
        }

        private static ExposedReference<ScriptAnimPoint> BindExposed(
            PlayableDirector director, ScriptAnimPoint value)
        {
            var exposed = new ExposedReference<ScriptAnimPoint>
            {
                exposedName = System.Guid.NewGuid().ToString("N")
            };
            director.SetReferenceValue(exposed.exposedName, value);
            return exposed;
        }

        private static ScriptAnimPoint EnsurePoint(Transform transform)
        {
            if (transform == null)
                return null;
            var point = transform.GetComponent<ScriptAnimPoint>();
            if (point == null)
                point = Undo.AddComponent<ScriptAnimPoint>(transform.gameObject);
            return point;
        }

        private static TimelineClip FindTimelineClip(RobotArmClip asset, out Object binding)
        {
            binding = null;
            var director = TimelineEditor.inspectedDirector;
            var timeline = TimelineEditor.inspectedAsset;
            if (director == null || timeline == null || asset == null)
                return null;

            foreach (var track in timeline.GetOutputTracks())
            {
                if (track is not ScriptAnimTrackBase)
                    continue;

                foreach (var clip in track.GetClips())
                {
                    if (clip.asset != asset)
                        continue;
                    binding = director.GetGenericBinding(track);
                    return clip;
                }
            }

            return null;
        }
    }

    [CustomTimelineEditor(typeof(RobotArmClip))]
    public class RobotArmClipTimelineEditor : ClipEditor
    {
        public override void OnClipChanged(TimelineClip clip)
        {
            if (clip?.asset is not RobotArmClip armClip || armClip.Data == null)
                return;
            if (!armClip.Data.AutoSyncDuration)
                return;

            ClipDurationSync.TryAutoSyncTrack(clip);
        }
    }
}
