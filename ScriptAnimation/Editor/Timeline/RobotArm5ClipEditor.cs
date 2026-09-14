using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation.Editor
{
    [CustomEditor(typeof(RobotArm5Clip))]
    public class RobotArm5ClipEditor : UnityEditor.Editor
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
            var clipAsset = (RobotArm5Clip)target;
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
                    "第五轴：须开启 Anim「末端保持向下」才生效。默认继承前序 J5；勾选「指定第五轴角」后本点使用配置角度。",
                    MessageType.Info);
            }

            EditorGUILayout.HelpBox(
                "路径：默认姿态（Home）→ 点位… → 默认姿态（Home）。点位均为途经/作业目标。",
                MessageType.Info);

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
                "末端朝下：以绑定的 RobotArm5Anim「末端保持向下」为准（第 5 轴在目标正上方，J5→TCP → 世界下方；第 4 轴俯仰）。\n" +
                "第五轴扭转：各点「指定第五轴角」可选覆盖；未指定则继承前序 / Home J5。\n" +
                "时长请用 Track「按速度刷新全轨时长」。",
                MessageType.None);

            if (binding is RobotArm5Anim anim && clipAsset.Data != null)
            {
                IExposedPropertyTable resolver = director;
                Transform[] targets = clipAsset.ResolveWaypointTargets(resolver);
                Transform firstTarget = RobotArm5Sampler.GetTarget(targets, 0);
                Transform lastTarget = RobotArm5Sampler.GetTarget(
                    targets, Mathf.Max(0, clipAsset.Data.WaypointCount - 1));

                if (GUILayout.Button("打印当前姿态（Console）"))
                    anim.LogCurrentPose();

                DrawConstraintGuards(clipAsset, anim, director, lastTarget, targets);
                DrawPathPreview(clipAsset, anim, timelineClip, director, targets, firstTarget);
            }
            else if (timelineClip != null && binding is ScriptAnimActor)
            {
                EditorGUILayout.HelpBox("RobotArm5Clip 需要绑定 RobotArm5Anim。", MessageType.Warning);
            }
        }

        private void DrawWaypointTransformList(RobotArm5Clip clipAsset, PlayableDirector director)
        {
            clipAsset.Data.Waypoints ??= new List<RobotArm5Waypoint>();
            clipAsset.WaypointTargets ??= new List<RobotArm5WaypointTarget>();
            clipAsset.EnsureWaypointTargetCount();

            int count = clipAsset.Data.WaypointCount;
            for (int i = 0; i < count; i++)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                DrawWaypointTransformField(clipAsset, director, i, $"点位 {i + 1}");

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
                DrawWaypointJ5Field(clipAsset, i);
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

        private static void DrawWaypointJ5Field(RobotArm5Clip clipAsset, int index)
        {
            RobotArm5Waypoint wp = clipAsset.Data.GetWaypoint(index);
            if (wp == null)
                return;

            EditorGUI.indentLevel++;
            EditorGUI.BeginChangeCheck();
            bool useJ5 = EditorGUILayout.Toggle("指定第五轴角", wp.UseJ5Angle);
            float j5Angle = wp.J5Angle;
            if (useJ5)
                j5Angle = EditorGUILayout.FloatField("第五轴角 (°)", j5Angle);
            else
                EditorGUILayout.LabelField("第五轴角", "继承前序 / Home");
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(clipAsset, "Edit RobotArm5 J5");
                wp.UseJ5Angle = useJ5;
                wp.J5Angle = j5Angle;
                clipAsset.InvalidatePlanCache();
                EditorUtility.SetDirty(clipAsset);
                TimelineEditor.Refresh(RefreshReason.ContentsModified);
            }

            EditorGUI.indentLevel--;
        }

        private static void DrawWaypointTransformField(
            RobotArm5Clip clipAsset,
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

                Undo.RecordObject(clipAsset, "Set RobotArm5 Waypoint Point");
                Undo.RecordObject(director, "Set RobotArm5 Waypoint Point");

                ClearExposed(director, exposed);
                if (next != null)
                    clipAsset.WaypointTargets[index].Target = BindExposed(director, next);
                else
                    clipAsset.WaypointTargets[index].Target = default;

                RobotArm5Waypoint wp = clipAsset.Data.GetWaypoint(index);
                if (wp != null && next != null)
                    wp.HasPoint = false;

                clipAsset.InvalidatePlanCache();
                EditorUtility.SetDirty(clipAsset);
                EditorUtility.SetDirty(director);
                TimelineEditor.Refresh(RefreshReason.ContentsModified);
            }
        }

        private void DrawAdvancedWaypoints(RobotArm5Clip clipAsset)
        {
            clipAsset.EnsureWaypointTargetCount();
            int count = clipAsset.Data.WaypointCount;
            for (int i = 0; i < count; i++)
            {
                RobotArm5Waypoint wp = clipAsset.Data.GetWaypoint(i);
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
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(clipAsset, "Edit RobotArm5 Waypoint");
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

        private static void AddWaypoint(RobotArm5Clip clipAsset)
        {
            Undo.RecordObject(clipAsset, "Add RobotArm5 Waypoint");
            clipAsset.Data.Waypoints ??= new List<RobotArm5Waypoint>();
            clipAsset.WaypointTargets ??= new List<RobotArm5WaypointTarget>();
            clipAsset.Data.Waypoints.Add(new RobotArm5Waypoint());
            clipAsset.WaypointTargets.Add(new RobotArm5WaypointTarget());
            clipAsset.EnsureWaypointTargetCount();
            clipAsset.InvalidatePlanCache();
            EditorUtility.SetDirty(clipAsset);
            TimelineEditor.Refresh(RefreshReason.ContentsModified);
        }

        private static void RemoveWaypointAt(
            RobotArm5Clip clipAsset, PlayableDirector director, int index)
        {
            if (clipAsset.Data.WaypointCount <= 1)
                return;

            Undo.RecordObject(clipAsset, "Remove RobotArm5 Waypoint");
            if (director != null)
                Undo.RecordObject(director, "Remove RobotArm5 Waypoint");

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
            RobotArm5Clip clipAsset,
            RobotArm5Anim anim,
            TimelineClip timelineClip,
            PlayableDirector director,
            Transform[] targets,
            Transform firstTarget)
        {
            var fallback = RobotArm5Sampler.TimelineStartFallback.None;
            if (timelineClip != null)
                fallback = RobotArm5Sampler.ResolvePreviousClipEnd(anim, timelineClip, director);

            float[] continuousStart = RobotArm5Sampler.GetContinuousStartAngles(
                clipAsset.Data, fallback, firstTarget);
            float[] ikSeed = RobotArm5Sampler.GetIkSeedAngles(continuousStart, fallback);
            bool forceDebug = RobotArm5Sampler.ShouldForceDebugPlayback(anim, clipAsset.Data);

            if (!RobotArm5Sampler.TryBuildPathPlans(
                    clipAsset, anim, clipAsset.Data, targets, fallback,
                    out RobotArm5Sampler.PathMotionPlans path,
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
                $"点位数 {wpCount}，段数 {segCount}（含 Home→首点、末点→Home）");

            for (int i = 0; i < wpCount; i++)
            {
                Transform t = RobotArm5Sampler.GetTarget(targets, i);
                var wp = clipAsset.Data.GetWaypoint(i);
                string j5Text = wp != null && wp.UseJ5Angle
                    ? $"J5={wp.J5Angle:F1}°"
                    : "J5=继承";
                sb.AppendLine($"  [{i + 1}] {(t != null ? t.name : "（坐标/未绑定）")}  {j5Text}");
            }

            sb.AppendLine($"轴数: {anim.JointCount}（定死五轴）");
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
            RobotArm5Clip clipAsset,
            RobotArm5Anim anim,
            PlayableDirector director,
            Transform lastTarget,
            Transform[] targets)
        {
            EditorGUILayout.HelpBox(
                anim.KeepTipDown
                    ? "末端约束：使用 Anim「末端保持向下」（轴5在目标正上方，J5→TCP → 世界下方；J4 俯仰）；第 5 轴由点位「指定第五轴角」驱动。"
                    : "末端约束：Anim 已关闭「末端保持向下」，将对齐各点 Transform 朝向。",
                MessageType.None);

            int missing = 0;
            for (int i = 0; i < clipAsset.Data.WaypointCount; i++)
            {
                Transform t = RobotArm5Sampler.GetTarget(targets, i);
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
                    Undo.RecordObject(clipAsset, "Bind RobotArm5Clip End Target");
                    Undo.RecordObject(director, "Bind RobotArm5Clip End Target");
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

        private static TimelineClip FindTimelineClip(RobotArm5Clip asset, out Object binding)
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

    [CustomTimelineEditor(typeof(RobotArm5Clip))]
    public class RobotArm5ClipTimelineEditor : ClipEditor
    {
        public override void OnClipChanged(TimelineClip clip)
        {
            if (clip?.asset is not RobotArm5Clip armClip || armClip.Data == null)
                return;
            if (!armClip.Data.AutoSyncDuration)
                return;

            ClipDurationSync.TryAutoSyncTrack(clip);
        }
    }
}
