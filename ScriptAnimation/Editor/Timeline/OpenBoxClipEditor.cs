using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Timeline;
using Object = UnityEngine.Object;

namespace NonsensicalKit.ScriptAnimation.Editor
{
    [CustomEditor(typeof(OpenBoxClip))]
    public class OpenBoxClipEditor : UnityEditor.Editor
    {
        bool m_showPreview;
        bool m_showHelp;

        const float StageLabelWidth = 64f;
        const float ArrowWidth = 14f;
        const float ToggleWidth = 16f;

        static readonly GUIContent s_fromCurrent =
            new GUIContent("从前序/默认开始", "开启后忽略 From，从前序终点（无前序则用组件默认状态）插到 To。");

        static readonly (string Animate, string From, string To, string Label, bool IsWall)[] s_stages =
        {
            ("AnimateWall", "FromWall", "Wall", "侧壁", true),
            ("AnimateBottomShort", "FromBottomShort", "BottomShort", "底盖短边", false),
            ("AnimateBottomLong", "FromBottomLong", "BottomLong", "底盖长边", false),
            ("AnimateTopShort", "FromTopShort", "TopShort", "顶盖短边", false),
            ("AnimateTopLong", "FromTopLong", "TopLong", "顶盖长边", false),
        };

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            SerializedProperty dataProp = serializedObject.FindProperty("Data");

            var clipAsset = (OpenBoxClip)target;
            Object binding = null;
            TimelineClip timelineClip = FindTimelineClip(clipAsset, out binding);
            var anim = binding as OpenBoxAnim;

            EditorGUI.BeginChangeCheck();

            DrawPresets(dataProp);
            EditorGUILayout.Space(4);
            DrawStages(dataProp);
            EditorGUILayout.Space(4);
            DrawTiming(dataProp);

            bool changed = EditorGUI.EndChangeCheck();
            serializedObject.ApplyModifiedProperties();

            if (changed && timelineClip != null)
            {
                OpenBoxClipTimelineEditor.UpdateDisplayNameIfNeeded(timelineClip);
                if (clipAsset.Data != null && clipAsset.Data.AutoSyncDuration)
                    ClipDurationSync.TryApply(timelineClip, binding, out _);
            }

            DrawWarnings(clipAsset, anim, binding);
            DrawPreview(clipAsset, anim, timelineClip);
            DrawHelp();
        }

        static void DrawPresets(SerializedProperty dataProp)
        {
            EditorGUILayout.LabelField("快捷预设", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("打开（1→0）", GUILayout.Height(22)))
                SetAllFromTo(dataProp, 1f, 0f);
            if (GUILayout.Button("闭合（0→1）", GUILayout.Height(22)))
                SetAllFromTo(dataProp, 0f, 1f);
            EditorGUILayout.EndHorizontal();
        }

        static void DrawStages(SerializedProperty dataProp)
        {
            EditorGUILayout.LabelField("阶段插值", EditorStyles.boldLabel);

            SerializedProperty fromCurrentProp = dataProp.FindPropertyRelative("FromCurrent");
            fromCurrentProp.boolValue = EditorGUILayout.ToggleLeft(
                s_fromCurrent, fromCurrentProp.boolValue);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("全选", EditorStyles.miniButtonLeft, GUILayout.Width(48)))
                SetAllAnimate(dataProp, true);
            if (GUILayout.Button("全不选", EditorStyles.miniButtonRight, GUILayout.Width(56)))
                SetAllAnimate(dataProp, false);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            bool fromCurrent = fromCurrentProp.boolValue;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            DrawStageHeader(fromCurrent);
            for (int i = 0; i < s_stages.Length; i++)
            {
                var s = s_stages[i];
                DrawStageRow(dataProp, s.Animate, s.From, s.To, s.Label, s.IsWall, fromCurrent);
            }

            EditorGUILayout.LabelField(
                "0=打开/压扁，1=闭合/成型；顶底盖可到 ±2",
                EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
        }

        static void DrawStageHeader(bool fromCurrent)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(ToggleWidth + 4);
            EditorGUILayout.LabelField("阶段", EditorStyles.miniBoldLabel, GUILayout.Width(StageLabelWidth));
            EditorGUILayout.LabelField(
                fromCurrent ? "From（前序）" : "From",
                EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField("", GUILayout.Width(ArrowWidth));
            EditorGUILayout.LabelField("To", EditorStyles.miniBoldLabel);
            EditorGUILayout.EndHorizontal();
        }

        static void DrawTiming(SerializedProperty dataProp)
        {
            EditorGUILayout.LabelField("时长", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.PropertyField(
                dataProp.FindPropertyRelative("Ease"),
                new GUIContent("缓动曲线"));
            EditorGUILayout.PropertyField(
                dataProp.FindPropertyRelative("DurationSeconds"),
                new GUIContent("目标用时（秒）", "为 0 时瞬间到位；关闭自动同步时由 Clip 长度决定插值时长。"));
            EditorGUILayout.PropertyField(
                dataProp.FindPropertyRelative("AutoSyncDuration"),
                new GUIContent("自动同步时长", "开启后「按速度刷新全轨时长」会按目标用时回写 Clip 长度。"));
            EditorGUILayout.EndVertical();
        }

        void DrawWarnings(OpenBoxClip clipAsset, OpenBoxAnim anim, Object binding)
        {
            if (anim != null && clipAsset.Data != null)
            {
                if (!clipAsset.Data.AnimatesAny)
                    EditorGUILayout.HelpBox("未勾选任何阶段，本 Clip 不会改变箱子。", MessageType.Warning);

                if (anim.BindingCount == 0)
                {
                    EditorGUILayout.HelpBox(
                        "尚未绑定折痕。请用 CartonRigConfig「生成纸箱节点树」，或手动填写 OpenBoxAnim 的折痕绑定。",
                        MessageType.Warning);
                }

                if (!clipAsset.Data.FromCurrent &&
                    Mathf.Abs(clipAsset.Data.FromWall - clipAsset.Data.Wall) < 1e-4f &&
                    Mathf.Abs(clipAsset.Data.FromBottomShort - clipAsset.Data.BottomShort) < 1e-4f &&
                    Mathf.Abs(clipAsset.Data.FromBottomLong - clipAsset.Data.BottomLong) < 1e-4f &&
                    Mathf.Abs(clipAsset.Data.FromTopShort - clipAsset.Data.TopShort) < 1e-4f &&
                    Mathf.Abs(clipAsset.Data.FromTopLong - clipAsset.Data.TopLong) < 1e-4f &&
                    clipAsset.Data.AnimatesAny)
                {
                    EditorGUILayout.HelpBox(
                        "From 与 To 相同，箱子不会有折叠变化。可点上方「打开」或「闭合」预设。",
                        MessageType.Warning);
                }
            }
            else if (binding is ScriptAnimActor)
            {
                EditorGUILayout.HelpBox("OpenBoxClip 需要绑定 OpenBoxAnim（含 FoldableCarton）。", MessageType.Warning);
            }
            else if (binding == null)
            {
                EditorGUILayout.HelpBox("Track 未绑定 OpenBoxAnim。", MessageType.Warning);
            }
        }

        void DrawPreview(OpenBoxClip clipAsset, OpenBoxAnim anim, TimelineClip timelineClip)
        {
            if (anim == null || clipAsset.Data == null)
                return;

            EditorGUILayout.Space(2);
            m_showPreview = EditorGUILayout.Foldout(m_showPreview, "预览信息", true);
            if (!m_showPreview)
                return;

            int holdFrames = ScriptAnimTrackBase.ResolveInstantHoldFrames(timelineClip);
            float estimate = OpenBoxSampler.EstimateClipDuration(clipAsset.Data, timelineClip);
            double clipDur = timelineClip != null ? timelineClip.duration : 0;
            bool instant = OpenBoxSampler.IsInstant(clipAsset.Data);
            OpenBoxPose incoming = OpenBoxSampler.ResolveStartPose(anim, timelineClip);
            OpenBoxPose startPose = OpenBoxSampler.Evaluate(incoming, clipAsset.Data, 0f);
            OpenBoxPose end = OpenBoxSampler.Evaluate(incoming, clipAsset.Data, 1f);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("绑定", $"{anim.name}（折痕 {anim.BindingCount}）");
            EditorGUILayout.LabelField(
                "默认状态",
                anim.HasDefaultPose ? "已设置" : "未设置（无前序时用 Flat）");
            EditorGUILayout.LabelField("插值阶段", $"{clipAsset.Data.AnimatedCount} / 5");
            EditorGUILayout.LabelField(
                "用时",
                instant
                    ? $"瞬间（占位 {holdFrames} 帧）"
                    : $"{clipDur:F3} s（Clip 长度）");
            if (clipAsset.Data.AutoSyncDuration)
            {
                EditorGUILayout.LabelField("目标用时", $"{clipAsset.Data.DurationSeconds:F3} s");
                EditorGUILayout.LabelField("估算时长", $"{estimate:F3} s");
            }

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField("起点 / 终点", EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField(
                FormatPose("起", startPose),
                EditorStyles.miniLabel);
            EditorGUILayout.LabelField(
                FormatPose("终", end),
                EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
        }

        void DrawHelp()
        {
            EditorGUILayout.Space(2);
            m_showHelp = EditorGUILayout.Foldout(m_showHelp, "说明", true);
            if (!m_showHelp)
                return;

            EditorGUILayout.HelpBox(
                "勾选阶段按 From→To 插值；未勾选保持前序终点。\n" +
                "「从前序/默认开始」开启时，起点取前序 OpenBox 终点；无前序则用组件默认状态。\n" +
                "拖拽 Clip 长度控制时长；目标用时为 0 表示瞬间改变。",
                MessageType.None);
        }

        static string FormatPose(string tag, in OpenBoxPose pose) =>
            $"{tag}  侧壁 {pose.Wall:0.##}  底短 {pose.BottomShort:0.##}  底长 {pose.BottomLong:0.##}  " +
            $"顶短 {pose.TopShort:0.##}  顶长 {pose.TopLong:0.##}";

        static void SetAllAnimate(SerializedProperty dataProp, bool value)
        {
            dataProp.FindPropertyRelative("AnimateWall").boolValue = value;
            dataProp.FindPropertyRelative("AnimateBottomShort").boolValue = value;
            dataProp.FindPropertyRelative("AnimateBottomLong").boolValue = value;
            dataProp.FindPropertyRelative("AnimateTopShort").boolValue = value;
            dataProp.FindPropertyRelative("AnimateTopLong").boolValue = value;
        }

        static void SetAllFromTo(SerializedProperty dataProp, float from, float to)
        {
            dataProp.FindPropertyRelative("FromCurrent").boolValue = false;
            SetAllAnimate(dataProp, true);
            dataProp.FindPropertyRelative("FromWall").floatValue = from;
            dataProp.FindPropertyRelative("Wall").floatValue = to;
            dataProp.FindPropertyRelative("FromBottomShort").floatValue = from;
            dataProp.FindPropertyRelative("BottomShort").floatValue = to;
            dataProp.FindPropertyRelative("FromBottomLong").floatValue = from;
            dataProp.FindPropertyRelative("BottomLong").floatValue = to;
            dataProp.FindPropertyRelative("FromTopShort").floatValue = from;
            dataProp.FindPropertyRelative("TopShort").floatValue = to;
            dataProp.FindPropertyRelative("FromTopLong").floatValue = from;
            dataProp.FindPropertyRelative("TopLong").floatValue = to;
        }

        static void DrawStageRow(
            SerializedProperty dataProp,
            string animateName,
            string fromName,
            string toName,
            string label,
            bool isWall,
            bool fromCurrent)
        {
            SerializedProperty animate = dataProp.FindPropertyRelative(animateName);
            SerializedProperty from = dataProp.FindPropertyRelative(fromName);
            SerializedProperty to = dataProp.FindPropertyRelative(toName);
            bool enabled = animate.boolValue;
            float min = isWall ? 0f : OpenBoxPose.FlapFoldMin;
            float max = isWall ? 1f : OpenBoxPose.FlapFoldMax;

            // 不用 PropertyField：InspectorLabelDrawer 会强制画带标签的 Slider，横排会换行错乱
            EditorGUILayout.BeginHorizontal();
            animate.boolValue = EditorGUILayout.Toggle(animate.boolValue, GUILayout.Width(ToggleWidth));
            using (new EditorGUI.DisabledScope(!enabled))
                EditorGUILayout.LabelField(label, GUILayout.Width(StageLabelWidth));
            using (new EditorGUI.DisabledScope(!enabled || fromCurrent))
                from.floatValue = EditorGUILayout.Slider(from.floatValue, min, max);
            EditorGUILayout.LabelField("→", GUILayout.Width(ArrowWidth));
            using (new EditorGUI.DisabledScope(!enabled))
                to.floatValue = EditorGUILayout.Slider(to.floatValue, min, max);
            EditorGUILayout.EndHorizontal();
        }

        static TimelineClip FindTimelineClip(OpenBoxClip asset, out Object binding)
        {
            binding = null;
            var director = TimelineEditor.inspectedDirector;
            var timeline = TimelineEditor.inspectedAsset;
            if (director == null || timeline == null || asset == null)
                return null;

            TimelineClip[] selected = TimelineEditor.selectedClips;
            if (selected != null)
            {
                for (int i = 0; i < selected.Length; i++)
                {
                    TimelineClip clip = selected[i];
                    if (clip?.asset != asset)
                        continue;
                    binding = director.GetGenericBinding(clip.GetParentTrack());
                    return clip;
                }
            }

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

    [CustomTimelineEditor(typeof(OpenBoxClip))]
    public class OpenBoxClipTimelineEditor : ClipEditor
    {
        static readonly Color s_color = new Color(0.92f, 0.62f, 0.28f, 1f);

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

            if (clip.asset is OpenBoxClip openBox)
            {
                SuggestNextStage(track, clip, openBox.Data);
                ClipDurationSync.SetDuration(
                    clip,
                    OpenBoxSampler.EstimateClipDuration(openBox.Data, clip));
            }
            else
            {
                ClipDurationSync.SetDuration(clip, OpenBoxSampler.DefaultDuration);
            }

            UpdateDisplayNameIfNeeded(clip);
        }

        static void SuggestNextStage(TrackAsset track, TimelineClip clip, OpenBoxClipData data)
        {
            if (track == null || clip == null || data == null)
                return;

            TimelineClip previous = null;
            double bestStart = double.NegativeInfinity;
            foreach (TimelineClip other in track.GetClips())
            {
                if (other == null || other == clip || other.asset is not OpenBoxClip)
                    continue;
                if (other.start >= clip.start)
                    continue;
                if (other.start > bestStart)
                {
                    bestStart = other.start;
                    previous = other;
                }
            }

            if (previous?.asset is not OpenBoxClip prevClip ||
                prevClip.Data == null ||
                prevClip.Data.AnimatedCount != 1)
                return;

            OpenBoxClipData prev = prevClip.Data;
            data.FromCurrent = prev.FromCurrent;
            data.AnimateWall = false;
            data.AnimateBottomShort = false;
            data.AnimateBottomLong = false;
            data.AnimateTopShort = false;
            data.AnimateTopLong = false;

            float from;
            float to;
            if (prev.AnimateWall)
            {
                from = prev.FromWall;
                to = prev.Wall;
                data.AnimateBottomShort = true;
                data.FromBottomShort = from;
                data.BottomShort = to;
            }
            else if (prev.AnimateBottomShort)
            {
                from = prev.FromBottomShort;
                to = prev.BottomShort;
                data.AnimateBottomLong = true;
                data.FromBottomLong = from;
                data.BottomLong = to;
            }
            else if (prev.AnimateBottomLong)
            {
                from = prev.FromBottomLong;
                to = prev.BottomLong;
                data.AnimateTopShort = true;
                data.FromTopShort = from;
                data.TopShort = to;
            }
            else if (prev.AnimateTopShort)
            {
                from = prev.FromTopShort;
                to = prev.TopShort;
                data.AnimateTopLong = true;
                data.FromTopLong = from;
                data.TopLong = to;
            }
            else
            {
                data.AnimateWall = true;
                data.FromWall = prev.FromTopLong;
                data.Wall = prev.TopLong;
            }
        }

        public override void OnClipChanged(TimelineClip clip)
        {
            if (clip?.asset is not OpenBoxClip openBox || openBox.Data == null)
                return;

            UpdateDisplayNameIfNeeded(clip);

            if (!openBox.Data.AutoSyncDuration)
                return;

            ClipDurationSync.TryAutoSyncTrack(clip);
        }

        internal static void UpdateDisplayNameIfNeeded(TimelineClip clip)
        {
            if (clip?.asset is not OpenBoxClip openBox || openBox.Data == null)
                return;

            string name = OpenBoxSampler.FormatDisplayName(openBox.Data);
            if (OpenBoxSampler.IsInstant(openBox.Data))
                name += " (瞬间)";

            if (clip.displayName == name)
                return;

            clip.displayName = name;
        }
    }

    [CustomEditor(typeof(OpenBoxAnim), true)]
    public class OpenBoxAnimEditor : UnityEditor.Editor
    {
        bool m_showHelp;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var anim = (OpenBoxAnim)target;
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("状态", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("折痕绑定", anim.BindingCount.ToString());
            EditorGUILayout.LabelField("默认状态", anim.HasDefaultPose ? "已设置" : "未设置");
            OpenBoxPose pose = anim.CaptureCurrent();
            EditorGUILayout.LabelField(
                "当前姿态",
                $"侧壁 {pose.Wall:0.##}  底短 {pose.BottomShort:0.##}  底长 {pose.BottomLong:0.##}");
            EditorGUILayout.LabelField(
                " ",
                $"顶短 {pose.TopShort:0.##}  顶长 {pose.TopLong:0.##}");
            EditorGUILayout.EndVertical();

            if (GUILayout.Button("从当前姿态捕获默认状态"))
            {
                Undo.RecordObject(anim, "Capture Default OpenBox Pose");
                anim.CaptureDefaultPoseFromCurrent();
                EditorUtility.SetDirty(anim);
                serializedObject.Update();
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("全开（压扁）"))
            {
                Undo.RecordObject(anim, "Open Box Flat");
                anim.ApplyPose(OpenBoxPose.Flat);
                EditorUtility.SetDirty(anim);
            }

            if (GUILayout.Button("全闭（成型）"))
            {
                Undo.RecordObject(anim, "Open Box Closed");
                anim.ApplyPose(OpenBoxPose.Closed);
                EditorUtility.SetDirty(anim);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(2);
            m_showHelp = EditorGUILayout.Foldout(m_showHelp, "说明", true);
            if (m_showHelp)
            {
                EditorGUILayout.HelpBox(
                    "挂到纸箱根物体，Timeline 绑定本组件（FoldableCarton 也可）。\n" +
                    "五个阶段独立：侧壁 → 底盖短边 → 底盖长边 → 顶盖短边 → 顶盖长边。\n" +
                    "「默认状态」用于同轨无前序 OpenBoxClip 时的开场姿态。",
                    MessageType.None);
            }
        }
    }
}
