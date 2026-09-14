using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation.Editor
{
    [CustomEditor(typeof(FilmWrapClip))]
    public class FilmWrapClipEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            ClipDataInspectorGui.DrawChildren(serializedObject.FindProperty("Data"));
            serializedObject.ApplyModifiedProperties();

            var clipAsset = (FilmWrapClip)target;
            Object binding = null;
            TimelineClip timelineClip = FindTimelineClip(clipAsset, out binding);

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "驱动 FilmWrapAnim：按分层螺旋上升裁切已有膜模型（先绕一圈，再升高）。\n" +
                "圈数 / 每圈上升高度在 FilmWrapAnim 上配置；Clip 只插值 0~1 进度。\n" +
                "默认可在 Timeline 上自由拖拽时长；勾选 AutoSyncDuration 后，" +
                "「按速度刷新全轨时长」会设为 DurationSeconds。",
                MessageType.Info);

            if (binding is FilmWrapAnim wrap && clipAsset.Data != null)
            {
                double clipDur = timelineClip != null ? timelineClip.duration : 0;
                float preview = FilmWrapSampler.EvaluateProgress(clipAsset.Data, 1f);
                EditorGUILayout.HelpBox(
                    $"目标 Renderer: {wrap.TargetCount}\n" +
                    $"From→To: {clipAsset.Data.FromProgress:F2} → {clipAsset.Data.ToProgress:F2}\n" +
                    $"结束预览进度: {preview:F2}\n" +
                    $"圈数: {wrap.ResolvedTurns:F2}  螺距: {wrap.ResolvedPitch:F3} m\n" +
                    $"当前时长: {clipDur:F3} s",
                    MessageType.Info);

                if (wrap.TargetCount == 0)
                {
                    EditorGUILayout.HelpBox(
                        "未收集到 Renderer。把 FilmWrapAnim 挂到膜模型根节点，或指定 Renderer。",
                        MessageType.Warning);
                }
            }
            else if (binding is ScriptAnimActor)
            {
                EditorGUILayout.HelpBox("FilmWrapClip 需要绑定 FilmWrapAnim。", MessageType.Warning);
            }
            else if (binding == null)
            {
                EditorGUILayout.HelpBox("Track 未绑定 FilmWrapAnim。", MessageType.Warning);
            }
        }

        static TimelineClip FindTimelineClip(FilmWrapClip asset, out Object binding)
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

    [CustomTimelineEditor(typeof(FilmWrapClip))]
    public class FilmWrapClipTimelineEditor : ClipEditor
    {
        static readonly Color s_wrapColor = new Color(0.45f, 0.88f, 0.95f, 1f);

        public override ClipDrawOptions GetClipOptions(TimelineClip clip)
        {
            ClipDrawOptions options = base.GetClipOptions(clip);
            options.highlightColor = s_wrapColor;
            return options;
        }

        public override void OnCreate(TimelineClip clip, TrackAsset track, TimelineClip clonedFrom)
        {
            if (clip == null || clonedFrom != null)
                return;

            if (clip.asset is FilmWrapClip wrap)
                ClipDurationSync.SetDuration(clip, FilmWrapSampler.EstimateDuration(wrap.Data));
            else
                ClipDurationSync.SetDuration(clip, FilmWrapSampler.DefaultDuration);

            UpdateDisplayName(clip);
        }

        public override void OnClipChanged(TimelineClip clip)
        {
            if (clip?.asset is not FilmWrapClip wrapClip || wrapClip.Data == null)
                return;

            UpdateDisplayName(clip);

            if (!wrapClip.Data.AutoSyncDuration)
                return;

            ClipDurationSync.TryAutoSyncTrack(clip);
        }

        static void UpdateDisplayName(TimelineClip clip)
        {
            if (clip?.asset is not FilmWrapClip wrap || wrap.Data == null)
                return;
            clip.displayName = $"缠膜 {wrap.Data.FromProgress:F1}→{wrap.Data.ToProgress:F1}";
        }
    }

    [CustomEditor(typeof(FilmWrapAnim))]
    public class FilmWrapAnimEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var wrap = (FilmWrapAnim)target;
            EditorGUILayout.Space();
            if (Shader.Find(FilmWrapAnim.WrapShaderName) == null)
            {
                EditorGUILayout.HelpBox(
                    "找不到 Shader「NonsensicalKit/ScriptAnimation/FilmWrap」。等待导入，或在组件上指定缠膜 Shader。",
                    MessageType.Error);
            }
            EditorGUILayout.HelpBox(
                "挂到膜模型根节点，Timeline 绑定本组件。\n" +
                "运行时用缠膜 Shader 实例裁切，不改原材质球资源；禁用组件会还原。\n" +
                "显示为分层螺旋：每一圈先绕货物一圈，再沿轴升高（圈数/每圈上升高度可配）。\n" +
                "选中时 Scene 里橙色螺旋为缠绕路径预览。方向不对就改顺时针或起始角。",
                MessageType.Info);

            EditorGUILayout.LabelField("已缓存 Renderer", wrap.TargetCount.ToString());
            EditorGUILayout.LabelField("当前进度", wrap.CurrentProgress.ToString("F3"));
            EditorGUILayout.LabelField("圈数", wrap.ResolvedTurns.ToString("F2"));
            EditorGUILayout.LabelField("螺距（米/圈）", wrap.ResolvedPitch.ToString("F3"));

            if (GUILayout.Button("重建缠膜目标"))
            {
                Undo.RecordObject(wrap, "Rebuild FilmWrap Targets");
                wrap.RebuildTargets();
                EditorUtility.SetDirty(wrap);
            }

            EditorGUI.BeginChangeCheck();
            float p = EditorGUILayout.Slider("预览进度", wrap.CurrentProgress, 0f, 1f);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(wrap, "Preview FilmWrap Progress");
                wrap.SetProgress(p);
                EditorUtility.SetDirty(wrap);
                SceneView.RepaintAll();
            }
        }
    }

    [InitializeOnLoad]
    static class FilmWrapSceneGuard
    {
        static FilmWrapSceneGuard()
        {
            EditorSceneManager.sceneSaving += OnSceneSaving;
            EditorSceneManager.sceneSaved += OnSceneSaved;
            AssemblyReloadEvents.beforeAssemblyReload += RestoreAll;
        }

        static void OnSceneSaving(Scene scene, string path) => RestoreAll();

        static void OnSceneSaved(Scene scene) => ReapplyPreview();

        static void RestoreAll()
        {
            FilmWrapAnim[] wraps = Object.FindObjectsByType<FilmWrapAnim>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < wraps.Length; i++)
            {
                if (wraps[i] != null)
                    wraps[i].RestoreOriginalMaterials();
            }
        }

        static void ReapplyPreview()
        {
            FilmWrapAnim[] wraps = Object.FindObjectsByType<FilmWrapAnim>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < wraps.Length; i++)
            {
                FilmWrapAnim wrap = wraps[i];
                if (wrap == null || !wrap.isActiveAndEnabled)
                    continue;
                if (!Application.isPlaying && !wrap.PreviewInEditMode)
                    continue;
                wrap.SetProgress(wrap.CurrentProgress);
            }
        }
    }
}
