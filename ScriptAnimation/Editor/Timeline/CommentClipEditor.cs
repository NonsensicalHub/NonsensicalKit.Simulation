using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation.Editor
{
    [CustomEditor(typeof(CommentClip))]
    public class CommentClipEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            ClipDataInspectorGui.DrawChildren(serializedObject.FindProperty("Data"));
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "注释 Clip 仅用于 Timeline 标注，不会改变绑定对象的位姿或动画。\n" +
                "可在轨道上自由拖拽时长；文字会同步显示在 Clip 条上。",
                MessageType.Info);
        }
    }

    [CustomTimelineEditor(typeof(CommentClip))]
    public class CommentClipTimelineEditor : ClipEditor
    {
        private static readonly Color s_commentColor = new Color(0.95f, 0.85f, 0.35f, 1f);

        public override ClipDrawOptions GetClipOptions(TimelineClip clip)
        {
            ClipDrawOptions options = base.GetClipOptions(clip);
            options.highlightColor = s_commentColor;
            return options;
        }

        public override void OnCreate(TimelineClip clip, TrackAsset track, TimelineClip clonedFrom)
        {
            if (clip == null || clonedFrom != null)
                return;

            ClipDurationSync.SetDuration(clip, (float)CommentClip.DefaultDuration);
            UpdateDisplayName(clip);
        }

        public override void OnClipChanged(TimelineClip clip)
        {
            UpdateDisplayName(clip);
        }

        internal static void UpdateDisplayName(TimelineClip clip)
        {
            if (clip?.asset is not CommentClip comment)
                return;

            clip.displayName = FormatDisplayName(comment.Data?.Text);
        }

        internal static string FormatDisplayName(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "注释";

            string line = text.Replace("\r\n", "\n").Replace('\r', '\n');
            int newline = line.IndexOf('\n');
            if (newline >= 0)
                line = line.Substring(0, newline);

            line = line.Trim();
            const int maxLen = 24;
            if (line.Length > maxLen)
                line = line.Substring(0, maxLen) + "…";

            return line;
        }
    }
}
