using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation.Editor
{
    [CustomEditor(typeof(SwingFlipClip))]
    public class SwingFlipClipEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            ClipDataInspectorGui.DrawChildren(serializedObject.FindProperty("Data"));
            serializedObject.ApplyModifiedProperties();

            var clipAsset = (SwingFlipClip)target;
            Object binding = null;
            TimelineClip timelineClip = FindTimelineClip(clipAsset, out binding);

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "驱动 SwingFlipAnim：摇摆 → 翻转 → 再摇摆 → 翻回。\n" +
                "运行时使用本 Clip 参数；新增时从组件写入默认值，可再改。\n" +
                "勾选 AutoSyncDuration 后，「按速度刷新全轨时长」会按序列估算时长。",
                MessageType.Info);

            if (binding is SwingFlipAnim anim && clipAsset.Data != null)
            {
                var p = SwingFlipSampler.Resolve(anim, clipAsset.Data);
                double clipDur = timelineClip != null ? timelineClip.duration : 0;
                EditorGUILayout.HelpBox(
                    $"序列: delay {p.StartDelay:F2} + sway {p.SwayDuration:F2} + flip {p.FlipDuration:F2} " +
                    $"+ sway {p.SwayDuration:F2} + flip {p.FlipDuration:F2}\n" +
                    $"估算时长: {p.TotalDuration:F3} s\n" +
                    $"当前时长: {clipDur:F3} s\n" +
                    $"摇摆 {p.SwayAngle:F1}° / 翻转 {p.FlipAngle:F1}°",
                    MessageType.Info);
            }
            else if (binding is ScriptAnimActor)
            {
                EditorGUILayout.HelpBox("SwingFlipClip 需要绑定 SwingFlipAnim。", MessageType.Warning);
            }
            else if (binding == null)
            {
                EditorGUILayout.HelpBox("Track 未绑定 SwingFlipAnim。", MessageType.Warning);
            }
        }

        private static TimelineClip FindTimelineClip(SwingFlipClip asset, out Object binding)
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

    [CustomTimelineEditor(typeof(SwingFlipClip))]
    public class SwingFlipClipTimelineEditor : ClipEditor
    {
        private static readonly Color s_color = new Color(0.95f, 0.7f, 0.35f, 1f);

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

            SwingFlipAnim anim = ResolveBinding(track);
            if (clip.asset is SwingFlipClip swing)
            {
                if (anim != null && swing.Data != null)
                {
                    Undo.RecordObject(swing, "Seed SwingFlipClip Defaults");
                    anim.ApplyClipDefaults(swing.Data);
                    EditorUtility.SetDirty(swing);
                }

                ClipDurationSync.SetDuration(clip, SwingFlipSampler.EstimateDuration(anim, swing.Data));
            }
            else
                ClipDurationSync.SetDuration(clip, 1.7f);

            UpdateDisplayName(clip, anim);
        }

        public override void OnClipChanged(TimelineClip clip)
        {
            if (clip?.asset is not SwingFlipClip swingClip || swingClip.Data == null)
                return;

            SwingFlipAnim anim = ResolveBinding(clip.GetParentTrack());
            UpdateDisplayName(clip, anim);

            if (!swingClip.Data.AutoSyncDuration)
                return;

            ClipDurationSync.TryAutoSyncTrack(clip);
        }

        private static SwingFlipAnim ResolveBinding(TrackAsset track)
        {
            var director = TimelineEditor.inspectedDirector;
            if (director == null || track == null)
                return null;
            return director.GetGenericBinding(track) as SwingFlipAnim;
        }

        private static void UpdateDisplayName(TimelineClip clip, SwingFlipAnim anim)
        {
            if (clip?.asset is not SwingFlipClip swing || swing.Data == null)
                return;

            var p = SwingFlipSampler.Resolve(anim, swing.Data);
            clip.displayName = $"SwingFlip {p.SwayAngle:F0}°/{p.FlipAngle:F0}°";
        }
    }

    [CustomEditor(typeof(SwingFlipAnim))]
    public class SwingFlipAnimEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var anim = (SwingFlipAnim)target;
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "挂到需要摇摆翻转的物体上，Timeline 绑定本组件。\n" +
                "也可在运行时调用 Trigger() / TriggerRestart() 播放完整序列。\n" +
                "摇摆到一侧最大角时触发对应 UnityEvent。",
                MessageType.Info);

            float estimate = SwingFlipSampler.EstimateDuration(anim, null);
            EditorGUILayout.LabelField("序列估算时长", $"{estimate:F3} s");
            EditorGUILayout.LabelField("播放中", anim.IsPlaying ? "是" : "否");

            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("触发"))
                    anim.Trigger();
                if (GUILayout.Button("重新开始"))
                    anim.TriggerRestart();
                if (GUILayout.Button("停止"))
                    anim.Stop();
                EditorGUILayout.EndHorizontal();
            }
        }
    }
}
