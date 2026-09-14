using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Timeline;
using Object = UnityEngine.Object;

namespace NonsensicalKit.ScriptAnimation.Editor
{
    [CustomPropertyDrawer(typeof(PoseDefinition))]
    public class PoseDefinitionDrawer : PropertyDrawer
    {
        const float ButtonHeight = 20f;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            Rect foldout = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            property.isExpanded = EditorGUI.Foldout(foldout, property.isExpanded, label, true);
            if (!property.isExpanded)
            {
                EditorGUI.EndProperty();
                return;
            }

            EditorGUI.indentLevel++;
            float y = foldout.yMax + EditorGUIUtility.standardVerticalSpacing;
            y = DrawField(position, property, "Name", y);
            y = DrawField(position, property, "Position", y);
            y = DrawField(position, property, "EulerAngles", y);
            y = DrawField(position, property, "Scale", y);

            var anim = property.serializedObject.targetObject as IPoseChangeActor;
            using (new EditorGUI.DisabledScope(anim == null))
            {
                Rect row = new Rect(
                    position.x,
                    y,
                    position.width,
                    ButtonHeight);
                Rect capture = new Rect(row.x, row.y, row.width * 0.5f - 2f, row.height);
                Rect preview = new Rect(row.x + row.width * 0.5f + 2f, row.y, row.width * 0.5f - 2f, row.height);

                if (GUI.Button(capture, "捕获当前"))
                    Capture(property, anim);

                if (GUI.Button(preview, "预览"))
                    Preview(property, anim);
            }

            EditorGUI.indentLevel--;
            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (!property.isExpanded)
                return EditorGUIUtility.singleLineHeight;

            float h = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            h += EditorGUI.GetPropertyHeight(property.FindPropertyRelative("Name"), true);
            h += EditorGUIUtility.standardVerticalSpacing;
            h += EditorGUI.GetPropertyHeight(property.FindPropertyRelative("Position"), true);
            h += EditorGUIUtility.standardVerticalSpacing;
            h += EditorGUI.GetPropertyHeight(property.FindPropertyRelative("EulerAngles"), true);
            h += EditorGUIUtility.standardVerticalSpacing;
            h += EditorGUI.GetPropertyHeight(property.FindPropertyRelative("Scale"), true);
            h += EditorGUIUtility.standardVerticalSpacing;
            h += ButtonHeight;
            return h;
        }

        static float DrawField(Rect position, SerializedProperty parent, string name, float y)
        {
            SerializedProperty prop = parent.FindPropertyRelative(name);
            float height = EditorGUI.GetPropertyHeight(prop, true);
            var rect = new Rect(position.x, y, position.width, height);
            EditorGUI.PropertyField(rect, prop, true);
            return y + height + EditorGUIUtility.standardVerticalSpacing;
        }

        static void Capture(SerializedProperty property, IPoseChangeActor anim)
        {
            if (anim == null)
                return;

            var pose = new PoseDefinition();
            anim.CaptureCurrentInto(pose);
            property.FindPropertyRelative("Name").stringValue =
                property.FindPropertyRelative("Name").stringValue;
            property.FindPropertyRelative("Position").vector3Value = pose.Position;
            property.FindPropertyRelative("EulerAngles").vector3Value = pose.EulerAngles;
            property.FindPropertyRelative("Scale").vector3Value = pose.Scale;
            property.serializedObject.ApplyModifiedProperties();
        }

        static void Preview(SerializedProperty property, IPoseChangeActor anim)
        {
            if (anim == null)
                return;

            var pose = new PoseDefinition
            {
                Name = property.FindPropertyRelative("Name").stringValue,
                Position = property.FindPropertyRelative("Position").vector3Value,
                EulerAngles = property.FindPropertyRelative("EulerAngles").vector3Value,
                Scale = property.FindPropertyRelative("Scale").vector3Value
            };

            Transform[] targets = anim.GetControlTransforms();
            for (int i = 0; i < targets.Length; i++)
            {
                Transform target = targets[i];
                if (target == null)
                    continue;
                Undo.RecordObject(target, "Preview Pose");
            }

            anim.ApplyPose(PoseChangeSampler.PoseTRS.FromDefinition(pose));
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] != null)
                    EditorUtility.SetDirty(targets[i]);
            }
        }
    }

    static class PoseChangeEditorUtility
    {
        public static bool IsSupportedBinding(Object binding)
            => binding is IPoseChangeActor or PoseChangeAnimMax;

        public static string[] GetPoseNames(Object binding)
        {
            if (binding is IPoseChangeActor single)
                return single.GetPoseNames();
            if (binding is PoseChangeAnimMax max)
                return max.GetPoseNames();
            return Array.Empty<string>();
        }

        public static int FindPoseIndex(Object binding, string poseName)
        {
            if (binding is IPoseChangeActor single)
                return single.FindPoseIndex(poseName);
            if (binding is PoseChangeAnimMax max)
                return max.FindPoseIndex(poseName);
            return -1;
        }

        public static int GetPoseCount(Object binding)
        {
            if (binding is IPoseChangeActor single)
                return single.PoseCount;
            if (binding is PoseChangeAnimMax max)
                return max.PoseCount;
            return 0;
        }

        public static bool HasDefaultPose(Object binding)
        {
            if (binding is IPoseChangeActor single)
                return single.HasDefaultPose;
            if (binding is PoseChangeAnimMax max)
                return max.HasDefaultPose;
            return false;
        }

        public static PoseSpace GetSpace(Object binding)
        {
            if (binding is IPoseChangeActor single)
                return single.Space;
            if (binding is PoseChangeAnimMax max)
                return max.Space;
            return PoseSpace.Local;
        }

        public static Transform[] GetControlTransforms(Object binding)
        {
            if (binding is IPoseChangeActor single)
                return single.GetControlTransforms();
            if (binding is PoseChangeAnimMax max)
                return max.GetControlTransforms();
            return Array.Empty<Transform>();
        }
    }

    [CustomPropertyDrawer(typeof(MultiTargetPoseDefinition))]
    public class MultiTargetPoseDefinitionDrawer : PropertyDrawer
    {
        const float ButtonHeight = 20f;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            var maxAnim = property.serializedObject.targetObject as PoseChangeAnimMax;
            Transform[] controls = maxAnim != null ? maxAnim.GetControlTransforms() : Array.Empty<Transform>();

            Rect foldout = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            property.isExpanded = EditorGUI.Foldout(foldout, property.isExpanded, label, true);
            if (!property.isExpanded)
            {
                EditorGUI.EndProperty();
                return;
            }

            EditorGUI.indentLevel++;
            float y = foldout.yMax + EditorGUIUtility.standardVerticalSpacing;
            y = DrawField(position, property, "Name", y);

            SerializedProperty targetsProp = property.FindPropertyRelative("Targets");
            if (maxAnim != null && targetsProp != null)
            {
                int expected = controls.Length;
                if (targetsProp.arraySize != expected)
                {
                    targetsProp.arraySize = expected;
                    for (int i = 0; i < expected; i++)
                    {
                        SerializedProperty item = targetsProp.GetArrayElementAtIndex(i);
                        if (item.FindPropertyRelative("Scale").vector3Value == Vector3.zero)
                            item.FindPropertyRelative("Scale").vector3Value = Vector3.one;
                    }
                }
            }

            if (targetsProp != null)
            {
                for (int i = 0; i < targetsProp.arraySize; i++)
                {
                    SerializedProperty targetProp = targetsProp.GetArrayElementAtIndex(i);
                    string targetLabel = i < controls.Length && controls[i] != null
                        ? $"目标 [{i + 1}] {controls[i].name}"
                        : $"目标 [{i + 1}]";

                    float blockHeight = EditorGUI.GetPropertyHeight(targetProp, new GUIContent(targetLabel), true);
                    var blockRect = new Rect(position.x, y, position.width, blockHeight);
                    EditorGUI.PropertyField(blockRect, targetProp, new GUIContent(targetLabel), true);
                    y = blockRect.yMax + EditorGUIUtility.standardVerticalSpacing;
                }
            }

            using (new EditorGUI.DisabledScope(maxAnim == null))
            {
                Rect row = new Rect(position.x, y, position.width, ButtonHeight);
                Rect capture = new Rect(row.x, row.y, row.width * 0.5f - 2f, row.height);
                Rect preview = new Rect(row.x + row.width * 0.5f + 2f, row.y, row.width * 0.5f - 2f, row.height);

                if (GUI.Button(capture, "捕获当前全部"))
                    CaptureAll(property, maxAnim);

                if (GUI.Button(preview, "预览全部"))
                    PreviewAll(property, maxAnim);
            }

            EditorGUI.indentLevel--;
            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (!property.isExpanded)
                return EditorGUIUtility.singleLineHeight;

            float h = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            h += EditorGUI.GetPropertyHeight(property.FindPropertyRelative("Name"), true);
            h += EditorGUIUtility.standardVerticalSpacing;

            SerializedProperty targetsProp = property.FindPropertyRelative("Targets");
            if (targetsProp != null)
            {
                var maxAnim = property.serializedObject.targetObject as PoseChangeAnimMax;
                Transform[] controls = maxAnim != null ? maxAnim.GetControlTransforms() : Array.Empty<Transform>();
                for (int i = 0; i < targetsProp.arraySize; i++)
                {
                    SerializedProperty targetProp = targetsProp.GetArrayElementAtIndex(i);
                    string targetLabel = i < controls.Length && controls[i] != null
                        ? $"目标 [{i + 1}] {controls[i].name}"
                        : $"目标 [{i + 1}]";
                    h += EditorGUI.GetPropertyHeight(targetProp, new GUIContent(targetLabel), true);
                    h += EditorGUIUtility.standardVerticalSpacing;
                }
            }

            h += ButtonHeight;
            return h;
        }

        static float DrawField(Rect position, SerializedProperty parent, string name, float y)
        {
            SerializedProperty prop = parent.FindPropertyRelative(name);
            float height = EditorGUI.GetPropertyHeight(prop, true);
            var rect = new Rect(position.x, y, position.width, height);
            EditorGUI.PropertyField(rect, prop, true);
            return y + height + EditorGUIUtility.standardVerticalSpacing;
        }

        static void CaptureAll(SerializedProperty property, PoseChangeAnimMax anim)
        {
            if (anim == null)
                return;

            var pose = new MultiTargetPoseDefinition
            {
                Name = property.FindPropertyRelative("Name").stringValue
            };
            anim.CaptureCurrentInto(pose);
            WriteTargets(property, pose);
            property.serializedObject.ApplyModifiedProperties();
        }

        static void PreviewAll(SerializedProperty property, PoseChangeAnimMax anim)
        {
            if (anim == null)
                return;

            var pose = ReadTargets(property);
            Transform[] controls = anim.GetControlTransforms();
            for (int i = 0; i < controls.Length; i++)
            {
                if (controls[i] != null)
                    Undo.RecordObject(controls[i], "Preview Pose");
            }

            anim.ApplyPoses(MultiTargetPoseDefinition.ToPoseTRSArray(pose));
            for (int i = 0; i < controls.Length; i++)
            {
                if (controls[i] != null)
                    EditorUtility.SetDirty(controls[i]);
            }
        }

        static MultiTargetPoseDefinition ReadTargets(SerializedProperty property)
        {
            var pose = new MultiTargetPoseDefinition
            {
                Name = property.FindPropertyRelative("Name").stringValue
            };

            SerializedProperty targetsProp = property.FindPropertyRelative("Targets");
            if (targetsProp == null)
                return pose;

            pose.Targets = new PoseDefinition[targetsProp.arraySize];
            for (int i = 0; i < targetsProp.arraySize; i++)
            {
                SerializedProperty targetProp = targetsProp.GetArrayElementAtIndex(i);
                pose.Targets[i] = new PoseDefinition
                {
                    Position = targetProp.FindPropertyRelative("Position").vector3Value,
                    EulerAngles = targetProp.FindPropertyRelative("EulerAngles").vector3Value,
                    Scale = targetProp.FindPropertyRelative("Scale").vector3Value
                };
            }

            return pose;
        }

        static void WriteTargets(SerializedProperty property, MultiTargetPoseDefinition pose)
        {
            if (pose?.Targets == null)
                return;

            SerializedProperty targetsProp = property.FindPropertyRelative("Targets");
            if (targetsProp == null)
                return;

            targetsProp.arraySize = pose.Targets.Length;
            for (int i = 0; i < pose.Targets.Length; i++)
            {
                PoseDefinition target = pose.Targets[i];
                if (target == null)
                    continue;

                SerializedProperty targetProp = targetsProp.GetArrayElementAtIndex(i);
                targetProp.FindPropertyRelative("Position").vector3Value = target.Position;
                targetProp.FindPropertyRelative("EulerAngles").vector3Value = target.EulerAngles;
                targetProp.FindPropertyRelative("Scale").vector3Value = target.Scale;
            }
        }
    }

    [CustomEditor(typeof(PoseChangeClip))]
    public class PoseChangeClipEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            SerializedProperty dataProp = serializedObject.FindProperty("Data");

            var clipAsset = (PoseChangeClip)target;
            Object binding = null;
            TimelineClip timelineClip = FindTimelineClip(clipAsset, out binding);
            var anim = binding;
            var singleAnim = binding as IPoseChangeActor;
            var maxAnim = binding as PoseChangeAnimMax;

            EditorGUI.BeginChangeCheck();
            DrawPoseNamePopup(dataProp.FindPropertyRelative("PoseName"), binding);
            EditorGUILayout.PropertyField(
                dataProp.FindPropertyRelative("DurationSeconds"),
                new GUIContent("目标用时（秒）"));
            EditorGUILayout.PropertyField(
                dataProp.FindPropertyRelative("Ease"),
                new GUIContent("缓动曲线"));
            EditorGUILayout.PropertyField(
                dataProp.FindPropertyRelative("AutoSyncDuration"),
                new GUIContent("自动同步时长"));
            bool changed = EditorGUI.EndChangeCheck();
            serializedObject.ApplyModifiedProperties();

            if (changed &&
                timelineClip != null &&
                clipAsset.Data != null &&
                clipAsset.Data.AutoSyncDuration)
            {
                ClipDurationSync.TryApply(timelineClip, binding, out _);
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "驱动 PoseChangeAnim / PoseChangeAnimMax：从前序 PoseChange 终点（无前序则用组件「默认状态」）插值到所选命名姿态。\n" +
                "默认可在 Timeline 上拖拽 Clip 长度来控制插值时长。\n" +
                $"目标用时为0 表示瞬间改变；此时 Clip 仍占轨配置的 {ScriptAnimTrackBase.ResolveInstantHoldFrames(timelineClip)} 帧，避免长度为0 无法选中。\n" +
                "勾选「自动同步时长」后，「按速度刷新全轨时长」会按目标用时（或轨瞬间占位帧数）回写。",
                MessageType.Info);

            if (PoseChangeEditorUtility.IsSupportedBinding(binding) && clipAsset.Data != null)
            {
                int holdFrames = ScriptAnimTrackBase.ResolveInstantHoldFrames(timelineClip);
                float estimate = PoseChangeSampler.EstimateClipDuration(clipAsset.Data, timelineClip);
                double clipDur = timelineClip != null ? timelineClip.duration : 0;
                bool instant = PoseChangeSampler.IsInstant(clipAsset.Data);

                var sb = new System.Text.StringBuilder();
                Transform[] controls = PoseChangeEditorUtility.GetControlTransforms(binding);
                if (controls.Length <= 1)
                {
                    sb.AppendLine($"控制对象: {(controls.Length > 0 && controls[0] != null ? controls[0].name : "(null)")}");
                }
                else
                {
                    sb.AppendLine($"控制对象数: {controls.Length}");
                    for (int i = 0; i < controls.Length; i++)
                    {
                        Transform control = controls[i];
                        sb.AppendLine($"  [{i + 1}] {(control != null ? control.name : "(null)")}");
                    }
                }
                sb.AppendLine($"姿态空间 {(PoseChangeEditorUtility.GetSpace(binding) == PoseSpace.World ? "世界" : "本地")}");
                sb.AppendLine($"已配置姿态 {PoseChangeEditorUtility.GetPoseCount(binding)}");
                sb.AppendLine(PoseChangeEditorUtility.HasDefaultPose(binding)
                    ? "默认状态 已设置（无前序时作开场）"
                    : "默认状态 未设置（无前序时用零位姿）");
                sb.AppendLine(instant
                    ? $"用时: 瞬间（Clip 占位 {holdFrames} 帧）"
                    : $"插值时长: {clipDur:F3} s（由 Clip 长度决定）");
                if (!instant && clipAsset.Data.AutoSyncDuration)
                    sb.AppendLine($"目标用时: {clipAsset.Data.DurationSeconds:F3} s");
                if (clipAsset.Data.AutoSyncDuration)
                    sb.AppendLine($"估算 Clip 时长: {estimate:F3} s");
                sb.AppendLine($"当前 Clip 时长: {clipDur:F3} s");

                bool poseFound = false;
                if (maxAnim != null &&
                    maxAnim.TryGetPose(clipAsset.Data.PoseName, out MultiTargetPoseDefinition multiPose) &&
                    multiPose != null)
                {
                    poseFound = true;
                    sb.AppendLine($"目标: {multiPose.Name}");
                    for (int i = 0; i < multiPose.Targets.Length; i++)
                    {
                        PoseDefinition targetPose = multiPose.Targets[i];
                        if (targetPose == null)
                            continue;

                        string label = i < controls.Length && controls[i] != null
                            ? controls[i].name
                            : $"目标 {i + 1}";
                        sb.AppendLine($"  [{label}] 位置 {targetPose.Position}");
                        sb.AppendLine($"         旋转 {targetPose.EulerAngles}");
                        sb.AppendLine($"         缩放 {targetPose.Scale}");
                    }
                }
                else if (singleAnim != null &&
                         singleAnim.TryGetPose(clipAsset.Data.PoseName, out PoseDefinition pose) &&
                         pose != null)
                {
                    poseFound = true;
                    sb.AppendLine($"目标: {pose.Name}");
                    sb.AppendLine($"  位置 {pose.Position}");
                    sb.AppendLine($"  旋转 {pose.EulerAngles}");
                    sb.AppendLine($"  缩放 {pose.Scale}");
                }
                else if (string.IsNullOrEmpty(clipAsset.Data.PoseName))
                {
                    sb.AppendLine("尚未选择目标姿态。");
                }
                else
                {
                    sb.AppendLine($"未找到姿态「{clipAsset.Data.PoseName}」。");
                }

                EditorGUILayout.HelpBox(sb.ToString().TrimEnd(), MessageType.Info);

                if (PoseChangeEditorUtility.GetPoseCount(binding) == 0)
                {
                    EditorGUILayout.HelpBox(
                        "组件上还没有姿态。请先在 PoseChangeAnim / PoseChangeAnimMax 的姿态列表中命名并捕获。",
                        MessageType.Warning);
                }
                else if (!poseFound && !string.IsNullOrEmpty(clipAsset.Data.PoseName))
                {
                    EditorGUILayout.HelpBox(
                        "目标姿态名称与组件配置不一致，请重新用下拉框选择。",
                        MessageType.Warning);
                }
            }
            else if (binding is ScriptAnimActor)
            {
                EditorGUILayout.HelpBox("PoseChangeClip 需要绑定 PoseChangeAnim 或 PoseChangeAnimMax。", MessageType.Warning);
            }
            else if (binding == null)
            {
                EditorGUILayout.HelpBox("Track 未绑定 PoseChangeAnim / PoseChangeAnimMax。", MessageType.Warning);
            }
        }

        static void DrawPoseNamePopup(SerializedProperty poseNameProp, Object binding)
        {
            if (poseNameProp == null)
                return;

            string current = poseNameProp.stringValue ?? string.Empty;
            if (!PoseChangeEditorUtility.IsSupportedBinding(binding))
            {
                EditorGUILayout.PropertyField(poseNameProp, new GUIContent("目标姿态"));
                return;
            }

            string[] names = PoseChangeEditorUtility.GetPoseNames(binding);
            var options = new List<string>(names.Length + 2) { "(未选择)" };
            int selected = 0;
            for (int i = 0; i < names.Length; i++)
            {
                options.Add(names[i]);
                if (names[i] == current)
                    selected = i + 1;
            }

            if (selected == 0 && !string.IsNullOrEmpty(current))
            {
                options.Add($"{current}（缺失）");
                selected = options.Count - 1;
            }

            int next = EditorGUILayout.Popup("目标姿态", selected, options.ToArray());
            if (next != selected)
            {
                poseNameProp.stringValue = next <= 0 ? string.Empty : options[next];
                if (next > 0 && options[next].EndsWith("（缺失）", StringComparison.Ordinal))
                    poseNameProp.stringValue = current;
            }
        }

        static TimelineClip FindTimelineClip(PoseChangeClip asset, out Object binding)
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

    [CustomTimelineEditor(typeof(PoseChangeClip))]
    public class PoseChangeClipTimelineEditor : ClipEditor
    {
        private static readonly Color s_color = new Color(0.72f, 0.48f, 0.88f, 1f);

        public override ClipDrawOptions GetClipOptions(TimelineClip clip)
        {
            ClipDrawOptions options = base.GetClipOptions(clip);
            options.highlightColor = s_color;
            return options;
        }

        public override void OnCreate(TimelineClip clip, TrackAsset track, TimelineClip clonedFrom)
        {
            if (clip == null || clonedFrom != null)
                return;

            Object binding = ResolveBinding(track);
            if (clip.asset is PoseChangeClip poseClip && poseClip.Data != null)
            {
                if (string.IsNullOrEmpty(poseClip.Data.PoseName))
                {
                    string[] names = PoseChangeEditorUtility.GetPoseNames(binding);
                    if (names.Length > 0)
                        poseClip.Data.PoseName = names[0];
                }

                ClipDurationSync.SetDuration(
                    clip,
                    PoseChangeSampler.EstimateClipDuration(poseClip.Data, clip));
            }

            UpdateDisplayName(clip, binding);
        }

        public override void OnClipChanged(TimelineClip clip)
        {
            if (clip?.asset is not PoseChangeClip poseClip || poseClip.Data == null)
                return;

            Object binding = ResolveBinding(clip.GetParentTrack());
            UpdateDisplayName(clip, binding);

            if (!poseClip.Data.AutoSyncDuration)
                return;

            ClipDurationSync.TryAutoSyncTrack(clip);
        }

        static Object ResolveBinding(TrackAsset track)
        {
            var director = TimelineEditor.inspectedDirector;
            if (director == null || track == null)
                return null;
            return director.GetGenericBinding(track);
        }

        static void UpdateDisplayName(TimelineClip clip, Object binding)
        {
            if (clip?.asset is not PoseChangeClip poseClip || poseClip.Data == null)
                return;

            string name = string.IsNullOrEmpty(poseClip.Data.PoseName)
                ? "(未设置"
                : poseClip.Data.PoseName;
            if (PoseChangeEditorUtility.IsSupportedBinding(binding) &&
                !string.IsNullOrEmpty(poseClip.Data.PoseName) &&
                PoseChangeEditorUtility.FindPoseIndex(binding, poseClip.Data.PoseName) < 0)
            {
                name += "?";
            }

            if (PoseChangeSampler.IsInstant(poseClip.Data))
                clip.displayName = $"姿态 {name} (瞬间)";
            else
                clip.displayName = $"姿态 {name} ({clip.duration:F2}s)";
        }
    }

    [CustomEditor(typeof(PoseChangeAnim))]
    public class PoseChangeAnimEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var anim = (PoseChangeAnim)target;
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "挂到 Timeline 绑定用。\n" +
                "「详细控制」可分别开关位置、旋转、大小；取消勾选则不写入对应属性。\n" +
                "在姿态列表中命名任意数量姿态，可用「捕获当前」写入当前 Transform。\n" +
                "「默认状态」用于同轨无前序 PoseChangeClip 时的开场姿态（确定性，不读运行时当前位置）。\n" +
                "Timeline Clip 用下拉框选择目标姿态；默认可拖拽 Clip 长度控制插值时长，目标用时 0 为瞬间改变。",
                MessageType.Info);

            EditorGUILayout.LabelField("控制对象", anim.ControlTarget != null ? anim.ControlTarget.name : "(null)");
            EditorGUILayout.LabelField(
                "控制通道",
                $"{(anim.ControlPosition ? "位置 " : "")}{(anim.ControlRotation ? "旋转 " : "")}{(anim.ControlScale ? "大小" : "")}".Trim());
            EditorGUILayout.LabelField("姿态数", anim.PoseCount.ToString());
            EditorGUILayout.LabelField("空间", anim.Space == PoseSpace.World ? "世界" : "本地");
            EditorGUILayout.LabelField("默认状态", anim.HasDefaultPose ? "已设置" : "未设置");

            if (GUILayout.Button("从当前姿态捕获默认状态"))
            {
                Undo.RecordObject(anim, "Capture Default Pose");
                anim.CaptureDefaultPoseFromCurrent();
                EditorUtility.SetDirty(anim);
                serializedObject.Update();
            }

            if (GUILayout.Button("添加姿态（捕获当前）"))
            {
                Undo.RecordObject(anim, "Add Pose From Current");
                anim.AddPoseFromCurrent();
                EditorUtility.SetDirty(anim);
                serializedObject.Update();
            }

            WarnDuplicateNames(anim);
        }

        internal static void WarnDuplicateNames(IPoseChangeActor anim)
            => WarnDuplicateNames(anim?.GetPoseNames());

        internal static void WarnDuplicateNames(PoseChangeAnimMax anim)
            => WarnDuplicateNames(anim?.GetPoseNames());

        internal static void WarnDuplicateNames(string[] names)
        {
            if (names == null || names.Length <= 1)
                return;

            var seen = new HashSet<string>();
            var dup = new List<string>();
            for (int i = 0; i < names.Length; i++)
            {
                if (!seen.Add(names[i]) && !dup.Contains(names[i]))
                    dup.Add(names[i]);
            }

            if (dup.Count > 0)
            {
                EditorGUILayout.HelpBox(
                    "存在重复姿态名：" + string.Join("、", dup) + "。下拉选择时只会命中第一项。",
                    MessageType.Warning);
            }
        }
    }

    [CustomEditor(typeof(PoseChangeAnimMax))]
    public class PoseChangeAnimMaxEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var anim = (PoseChangeAnimMax)target;
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "挂到 Timeline 绑定用。\n" +
                "每个命名姿态内，按「控制对象」列表分别为各目标配置位姿；展开姿态项可看到各目标的位姿数据。\n" +
                "「捕获当前全部」会一次性从各控制对象读取位姿；「预览全部」会写回场景。\n" +
                "「详细控制」可分别开关位置、旋转、大小；取消勾选则不写入对应属性。\n" +
                "「默认状态」用于同轨无前序 PoseChangeClip 时的开场姿态（确定性，不读运行时当前位置）。\n" +
                "Timeline Clip 用下拉框选择目标姿态；默认可拖拽 Clip 长度控制插值时长，目标用时 0 为瞬间改变。",
                MessageType.Info);

            Transform[] controls = anim.GetControlTransforms();
            if (controls.Length == 0)
            {
                EditorGUILayout.LabelField("控制对象", "(null)");
            }
            else if (controls.Length == 1)
            {
                EditorGUILayout.LabelField("控制对象", controls[0] != null ? controls[0].name : "(null)");
            }
            else
            {
                EditorGUILayout.LabelField("控制对象数", controls.Length.ToString());
                for (int i = 0; i < controls.Length; i++)
                {
                    Transform control = controls[i];
                    EditorGUILayout.LabelField($"  [{i + 1}]", control != null ? control.name : "(null)");
                }
            }

            EditorGUILayout.LabelField(
                "控制通道",
                $"{(anim.ControlPosition ? "位置 " : "")}{(anim.ControlRotation ? "旋转 " : "")}{(anim.ControlScale ? "大小" : "")}".Trim());
            EditorGUILayout.LabelField("姿态数", anim.PoseCount.ToString());
            EditorGUILayout.LabelField("空间", anim.Space == PoseSpace.World ? "世界" : "本地");
            EditorGUILayout.LabelField("默认状态", anim.HasDefaultPose ? "已设置" : "未设置");

            if (GUILayout.Button("从当前姿态捕获默认状态"))
            {
                Undo.RecordObject(anim, "Capture Default Pose");
                anim.CaptureDefaultPoseFromCurrent();
                EditorUtility.SetDirty(anim);
                serializedObject.Update();
            }

            if (GUILayout.Button("添加姿态（捕获当前）"))
            {
                Undo.RecordObject(anim, "Add Pose From Current");
                anim.AddPoseFromCurrent();
                EditorUtility.SetDirty(anim);
                serializedObject.Update();
            }

            PoseChangeAnimEditor.WarnDuplicateNames(anim);
        }
    }
}
