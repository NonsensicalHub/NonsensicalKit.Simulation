using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation.Editor
{
    [CustomEditor(typeof(SequentialPositionClip))]
    public class SequentialPositionClipEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            ClipDataInspectorGui.DrawChildren(serializedObject.FindProperty("Data"));
            serializedObject.ApplyModifiedProperties();

            var clipAsset = (SequentialPositionClip)target;
            Object binding = null;
            TimelineClip timelineClip = FindTimelineClip(clipAsset, out binding);

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "驱动 SequentialPositionAnim：路点间隔/控制对象均在组件上配置。\n" +
                "Clip 播放中显示，播放前结束后隐藏。\n" +
                "勾选 AutoSyncDuration 后，「按速度刷新全轨时长」会按组件间隔总和估算时长。",
                MessageType.Info);

            if (binding is SequentialPositionAnim anim)
            {
                Transform control = anim.ControlTarget;
                int count = SequentialPositionSampler.GetWaypointCount(anim);
                float estimate = SequentialPositionSampler.EstimateDuration(anim);
                double clipDur = timelineClip != null ? timelineClip.duration : 0;

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"控制对象: {(control != null ? control.name : "(null)")}");
                sb.AppendLine($"路点数 {count}");
                sb.AppendLine($"估算时长: {estimate:F3} s");
                sb.AppendLine($"当前时长: {clipDur:F3} s");
                for (int i = 0; i < count; i++)
                {
                    Transform wp = anim.Waypoints[i];
                    float hold = SequentialPositionSampler.GetInterval(anim, i);
                    if (wp != null)
                    {
                        Vector3 p = wp.position;
                        sb.AppendLine(
                            $"  [{i}] {wp.name} ({p.x:F2}, {p.y:F2}, {p.z:F2}) 停留 {hold:F2}s");
                    }
                    else
                    {
                        sb.AppendLine($"  [{i}] (未指定 停留 {hold:F2}s");
                    }
                }

                EditorGUILayout.HelpBox(sb.ToString().TrimEnd(), MessageType.Info);

                if (!SequentialPositionSampler.HasValidDuration(anim))
                {
                    EditorGUILayout.HelpBox(
                        "组件估算时长为 0：请先配置路点与间隔后再添加/同步 Clip。",
                        MessageType.Warning);
                }
            }
            else if (binding is ScriptAnimActor)
            {
                EditorGUILayout.HelpBox("SequentialPositionClip 需要绑定 SequentialPositionAnim。", MessageType.Warning);
            }
            else if (binding == null)
            {
                EditorGUILayout.HelpBox("Track 未绑定 SequentialPositionAnim。", MessageType.Warning);
            }
        }

        private static TimelineClip FindTimelineClip(SequentialPositionClip asset, out Object binding)
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

    [CustomTimelineEditor(typeof(SequentialPositionClip))]
    public class SequentialPositionClipTimelineEditor : ClipEditor
    {
        private static readonly Color s_color = new Color(0.55f, 0.75f, 0.95f, 1f);

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

            SequentialPositionAnim anim = ResolveBinding(track);
            float duration = SequentialPositionSampler.EstimateDuration(anim);
            if (duration > 1e-6f)
                ClipDurationSync.SetDuration(clip, duration);

            UpdateDisplayName(clip, anim);
        }

        public override void OnClipChanged(TimelineClip clip)
        {
            if (clip?.asset is not SequentialPositionClip seqClip || seqClip.Data == null)
                return;

            SequentialPositionAnim anim = ResolveBinding(clip.GetParentTrack());
            UpdateDisplayName(clip, anim);

            if (!seqClip.Data.AutoSyncDuration)
                return;

            ClipDurationSync.TryAutoSyncTrack(clip);
        }

        private static SequentialPositionAnim ResolveBinding(TrackAsset track)
        {
            var director = TimelineEditor.inspectedDirector;
            if (director == null || track == null)
                return null;
            return director.GetGenericBinding(track) as SequentialPositionAnim;
        }

        private static void UpdateDisplayName(TimelineClip clip, SequentialPositionAnim anim)
        {
            if (clip?.asset is not SequentialPositionClip)
                return;

            int count = SequentialPositionSampler.GetWaypointCount(anim);
            float dur = SequentialPositionSampler.EstimateDuration(anim);
            clip.displayName = $"SeqPos ×{count} ({dur:F2}s)";
        }
    }

    [CustomEditor(typeof(SequentialPositionAnim))]
    public class SequentialPositionAnimEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var anim = (SequentialPositionAnim)target;
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "挂到 Timeline 绑定用。\n" +
                "在此配置控制对象、路点列表与间隔；控制对象同时用于移动与 SetActive 显隐。\n" +
                "估算时长须大于 0 才能添加 SequentialPosition Clip。\n" +
                "Clip 外自动隐藏；播放中显示并瞬移到路点世界坐标。",
                MessageType.Info);

            float estimate = SequentialPositionSampler.EstimateDuration(anim);
            EditorGUILayout.LabelField("控制对象", anim.ControlTarget != null ? anim.ControlTarget.name : "(null)");
            EditorGUILayout.LabelField("路点数", SequentialPositionSampler.GetWaypointCount(anim).ToString());
            EditorGUILayout.LabelField("估算时长", $"{estimate:F3} s");
            EditorGUILayout.LabelField("当前可见", anim.IsVisible ? "是" : "否");

            if (!SequentialPositionSampler.HasValidDuration(anim))
            {
                EditorGUILayout.HelpBox(
                    "估算时长为 0，无法添加依次换位 Clip。请配置路点与大于 0 的间隔。",
                    MessageType.Warning);
            }
        }
    }
}
