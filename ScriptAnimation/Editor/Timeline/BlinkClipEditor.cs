using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation.Editor
{
    [CustomEditor(typeof(BlinkClip))]
    public class BlinkClipEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            ClipDataInspectorGui.DrawChildren(serializedObject.FindProperty("Data"));
            serializedObject.ApplyModifiedProperties();

            var clipAsset = (BlinkClip)target;
            Object binding = null;
            TimelineClip timelineClip = FindTimelineClip(clipAsset, out binding);

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "驱动 BlinkAnim：在 Clip 时长内按「切换次数」均匀切换目标显隐。\n" +
                "切换次数 × 间隔 = 建议 Clip 时长；勾选自动同步时长后，" +
                "「按速度刷新全轨时长」会按该公式设置时长。\n" +
                "目标建议在 BlinkAnim 上指定子物体；目标为组件自身时隐藏走 Renderer.enabled。",
                MessageType.Info);

            if (binding is BlinkAnim blink && clipAsset.Data != null)
            {
                double clipDur = timelineClip != null ? timelineClip.duration : 0;
                float est = BlinkSampler.EstimateDuration(clipAsset.Data);
                bool preview = BlinkSampler.EvaluateVisible(clipAsset.Data, 1f, holdEnd: true);
                EditorGUILayout.HelpBox(
                    $"目标: {(blink.Target != null ? blink.Target.name : "（空）")}\n" +
                    $"切换 {clipAsset.Data.ToggleCount} 次 × {clipAsset.Data.IntervalSeconds:F3}s ≈ {est:F3}s\n" +
                    $"Clip 结束时可见: {(preview ? "是" : "否")}\n" +
                    $"当前时长: {clipDur:F3} s",
                    MessageType.Info);
            }
            else if (binding is ScriptAnimActor)
            {
                EditorGUILayout.HelpBox("BlinkClip 需要绑定 BlinkAnim。", MessageType.Warning);
            }
            else if (binding == null)
            {
                EditorGUILayout.HelpBox("Track 未绑定 BlinkAnim。", MessageType.Warning);
            }
        }

        private static TimelineClip FindTimelineClip(BlinkClip asset, out Object binding)
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

    [CustomTimelineEditor(typeof(BlinkClip))]
    public class BlinkClipTimelineEditor : ClipEditor
    {
        private static readonly Color s_blinkColor = new Color(0.95f, 0.85f, 0.35f, 1f);

        public override ClipDrawOptions GetClipOptions(TimelineClip clip)
        {
            ClipDrawOptions options = base.GetClipOptions(clip);
            options.highlightColor = s_blinkColor;
            return options;
        }

        public override void OnCreate(TimelineClip clip, TrackAsset track, TimelineClip clonedFrom)
        {
            if (clip == null || clonedFrom != null)
                return;

            if (clip.asset is BlinkClip blink)
                ClipDurationSync.SetDuration(clip, BlinkSampler.EstimateDuration(blink.Data));
            else
                ClipDurationSync.SetDuration(clip, BlinkSampler.DefaultDuration);

            UpdateDisplayName(clip);
        }

        public override void OnClipChanged(TimelineClip clip)
        {
            if (clip?.asset is not BlinkClip blinkClip || blinkClip.Data == null)
                return;

            UpdateDisplayName(clip);

            if (!blinkClip.Data.AutoSyncDuration)
                return;

            ClipDurationSync.TryAutoSyncTrack(clip);
        }

        private static void UpdateDisplayName(TimelineClip clip)
        {
            if (clip?.asset is not BlinkClip blink || blink.Data == null)
                return;

            clip.displayName = $"Blink ×{blink.Data.ToggleCount}";
        }
    }

    [CustomEditor(typeof(BlinkAnim))]
    public class BlinkAnimEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var blink = (BlinkAnim)target;
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "挂到父物体上，Timeline 绑定本组件；「目标」指定被切换显隐的子物体。\n" +
                "若目标为本物体，隐藏时使用 Renderer.enabled，避免 SetActive 中断 Timeline 采样。",
                MessageType.Info);

            EditorGUILayout.LabelField("当前可见", blink.IsVisible ? "是" : "否");

            using (new EditorGUI.DisabledScope(!Application.isPlaying && blink.Target == null))
            {
                EditorGUI.BeginChangeCheck();
                bool visible = EditorGUILayout.Toggle("预览可见", blink.IsVisible);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(blink, "Preview Blink Visibility");
                    blink.SetVisible(visible);
                    EditorUtility.SetDirty(blink);
                }
            }
        }
    }
}
