using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation.Editor
{
    [CustomEditor(typeof(ObjectSwitchClip))]
    public class ObjectSwitchClipEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("Data"), new GUIContent("参数"), true);
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "放在专用轨（ScriptDedicatedTrack）上，绑定 ObjectSwitchAnim。\n" +
                "Clip 中填写显示索引（从 0 开始）；生效时只显示该索引对象，其余隐藏。\n" +
                "无 Clip 覆盖时全部隐藏（不 hold 结束态）。",
                MessageType.Info);
        }
    }

    [CustomTimelineEditor(typeof(ObjectSwitchClip))]
    public class ObjectSwitchClipTimelineEditor : ClipEditor
    {
        static readonly Color s_color = new Color(0.35f, 0.65f, 0.95f, 1f);

        public override ClipDrawOptions GetClipOptions(TimelineClip clip)
        {
            ClipDrawOptions options = base.GetClipOptions(clip);
            options.highlightColor = s_color;
            UpdateDisplayName(clip);
            return options;
        }

        public override void OnCreate(TimelineClip clip, TrackAsset track, TimelineClip clonedFrom)
        {
            if (clip == null || clonedFrom != null)
                return;

            clip.duration = ObjectSwitchClip.DefaultDuration;
            UpdateDisplayName(clip);
        }

        public override void OnClipChanged(TimelineClip clip)
        {
            UpdateDisplayName(clip);
        }

        static void UpdateDisplayName(TimelineClip clip)
        {
            if (clip?.asset is not ObjectSwitchClip switchClip || switchClip.Data == null)
                return;

            clip.displayName = $"显示 #{switchClip.Data.VisibleIndex}";
        }
    }

    [CustomEditor(typeof(ObjectSwitchAnim))]
    public class ObjectSwitchAnimEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var anim = (ObjectSwitchAnim)target;
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "挂到父物体上，专用轨绑定本组件。\n" +
                "候选对象默认收集直接子物体（不含自身）。\n" +
                "子物体增删后点下方按钮重新收集。\n" +
                "注意：不会 GatherProperties(m_IsActive)，避免 Timeline 预览盖掉 SetActive。",
                MessageType.Info);

            EditorGUILayout.LabelField("当前目标数量", anim.TargetCount.ToString());

            if (GUILayout.Button("收集子物体"))
            {
                Undo.RecordObject(anim, "Collect ObjectSwitch Children");
                anim.CollectChildren();
                EditorUtility.SetDirty(anim);
            }
        }
    }
}
