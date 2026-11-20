using CineShot.Core;
using UnityEditor;
using UnityEngine;

namespace CineShot.Editor
{
    [CustomEditor(typeof(ShotAsset))]
    public sealed class ShotAssetEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            using (new EditorGUI.DisabledScope(true))
                EditorGUI.PropertyField(CineHelp.Line(CineTips.ShotAsset), serializedObject.FindProperty("m_Id"), new GUIContent("编号"));

            Draw("displayName", "显示名称");
            Draw("rigKind", "机位类型");
            EditorGUILayout.PropertyField(serializedObject.FindProperty("rigParams"), new GUIContent("机位参数"), true);
            Draw("defaultDuration", "默认时长（秒）");
            EditorGUILayout.PropertyField(serializedObject.FindProperty("targetBinding"), new GUIContent("目标偏移", "相对片段上绑定的注视 / 跟随物体的局部偏移。"), true);
            Draw("compositionOffset", "构图偏移", "相对机位的构图平移微调。");
            Draw("fov", "视场角");
            Draw("nearClip", "近裁剪面");
            Draw("farClip", "远裁剪面");
            Draw("focusMode", "焦点模式", CineTips.FocusMode);
            Draw("focusDistance", "焦点距离", CineTips.FocusDistance);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("focusSettings"), new GUIContent("焦点配置"), true);
            Draw("easeIn", "混入曲线", CineTips.EaseIn);
            Draw("easeOut", "淡出曲线", CineTips.EaseOut);
            Draw("priority", "优先级", CineTips.Priority);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("tags"), new GUIContent("标签", CineTips.Tags), true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("handheld"), new GUIContent("手持层", "叠在任意机位上的抖动；主机位选「手持」时用机位参数本身。"), true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("matchPoint"), new GUIContent("匹配帧锚点", "转场=匹配帧时，进入开头对齐到此点。"), true);
            Draw("parentGuid", "父变体编号", "变体父镜头 GUID；空=独立。");

            serializedObject.ApplyModifiedProperties();
        }

        void Draw(string prop, string label)
        {
            Draw(prop, label, null);
        }

        void Draw(string prop, string label, string tip)
        {
            var p = serializedObject.FindProperty(prop);
            if (p == null)
                return;
            EditorGUILayout.PropertyField(p, new GUIContent(label, tip));
        }
    }
}
