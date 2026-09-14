using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation.Editor
{
    [CustomEditor(typeof(FadeClip))]
    public class FadeClipEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            ClipDataInspectorGui.DrawChildren(serializedObject.FindProperty("Data"));
            serializedObject.ApplyModifiedProperties();

            var clipAsset = (FadeClip)target;
            Object binding = null;
            TimelineClip timelineClip = FindTimelineClip(clipAsset, out binding);

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "驱动 FadeAnim：批量改自身与子节点 Renderer 的颜色 alpha。\n" +
                "默认可在 Timeline 上自由拖拽时长；勾选 AutoSyncDuration 后，" +
                "「按速度刷新全轨时长」会设为 DurationSeconds。\n" +
                "材质需为 Transparent/Fade；否则改 alpha 可能看不见。",
                MessageType.Info);

            if (binding is FadeAnim fade && clipAsset.Data != null)
            {
                if (fade.TargetCount <= 0)
                    fade.RebuildTargets();

                double clipDur = timelineClip != null ? timelineClip.duration : 0;
                float preview = FadeSampler.EvaluateAlpha(clipAsset.Data, 1f);
                EditorGUILayout.HelpBox(
                    $"目标数 {fade.TargetCount}\n" +
                    $"From→To: {clipAsset.Data.FromAlpha:F2} → {clipAsset.Data.ToAlpha:F2}\n" +
                    $"结束预览 alpha: {preview:F2}\n" +
                    $"当前时长: {clipDur:F3} s",
                    MessageType.Info);

                if (fade.TargetCount == 0)
                {
                    EditorGUILayout.HelpBox(
                        "未收集到可改色的 Renderer。检查子节点材质是否含 _BaseColor / _Color。",
                        MessageType.Warning);
                }
            }
            else if (binding is ScriptAnimActor)
            {
                EditorGUILayout.HelpBox("FadeClip 需要绑定 FadeAnim。", MessageType.Warning);
            }
            else if (binding == null)
            {
                EditorGUILayout.HelpBox("Track 未绑定 FadeAnim。", MessageType.Warning);
            }
        }

        private static TimelineClip FindTimelineClip(FadeClip asset, out Object binding)
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

    [CustomTimelineEditor(typeof(FadeClip))]
    public class FadeClipTimelineEditor : ClipEditor
    {
        private static readonly Color s_fadeColor = new Color(0.55f, 0.75f, 0.95f, 1f);

        public override ClipDrawOptions GetClipOptions(TimelineClip clip)
        {
            ClipDrawOptions options = base.GetClipOptions(clip);
            options.highlightColor = s_fadeColor;
            return options;
        }

        public override void OnCreate(TimelineClip clip, TrackAsset track, TimelineClip clonedFrom)
        {
            if (clip == null || clonedFrom != null)
                return;

            if (clip.asset is FadeClip fade)
                ClipDurationSync.SetDuration(clip, FadeSampler.EstimateDuration(fade.Data));
            else
                ClipDurationSync.SetDuration(clip, FadeSampler.DefaultDuration);

            UpdateDisplayName(clip);
        }

        public override void OnClipChanged(TimelineClip clip)
        {
            if (clip?.asset is not FadeClip fadeClip || fadeClip.Data == null)
                return;

            UpdateDisplayName(clip);

            if (!fadeClip.Data.AutoSyncDuration)
                return;

            ClipDurationSync.TryAutoSyncTrack(clip);
        }

        private static void UpdateDisplayName(TimelineClip clip)
        {
            if (clip?.asset is not FadeClip fade || fade.Data == null)
                return;
            clip.displayName = $"Fade {fade.Data.FromAlpha:F1}→{fade.Data.ToAlpha:F1}";
        }
    }

    [CustomEditor(typeof(FadeAnim))]
    public class FadeAnimEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var fade = (FadeAnim)target;
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "挂到根物体上，Timeline 绑定本组件。\n" +
                "用 MaterialPropertyBlock 写 alpha，不改 sharedMaterial。\n" +
                "子物体增删或材质更换后点下方重建。",
                MessageType.Info);

            EditorGUILayout.LabelField("已缓存目标", fade.TargetCount.ToString());
            EditorGUILayout.LabelField("当前透明度", fade.CurrentAlpha.ToString("F3"));

            if (GUILayout.Button("重建透明度目标"))
            {
                Undo.RecordObject(fade, "Rebuild Fade Targets");
                fade.RebuildTargets();
                EditorUtility.SetDirty(fade);
            }

            using (new EditorGUI.DisabledScope(!Application.isPlaying && fade.TargetCount == 0))
            {
                EditorGUI.BeginChangeCheck();
                float a = EditorGUILayout.Slider("预览 Alpha", fade.CurrentAlpha, 0f, 1f);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(fade, "Preview Fade Alpha");
                    if (fade.TargetCount == 0)
                        fade.RebuildTargets();
                    fade.SetAlpha(a);
                    EditorUtility.SetDirty(fade);
                }
            }
        }
    }
}
