using CineShot.Core;
using UnityEditor;
using UnityEngine;

namespace CineShot.Editor
{
    [CustomEditor(typeof(EasingAsset))]
    public sealed class EasingAssetEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            CineHelp.Prop(CineTips.Easing, serializedObject.FindProperty("kind"), new GUIContent("缓动类型"));
            var kind = (EasingAsset.EaseKind)serializedObject.FindProperty("kind").enumValueIndex;
            using (new EditorGUI.DisabledScope(kind != EasingAsset.EaseKind.Custom))
                EditorGUILayout.PropertyField(serializedObject.FindProperty("custom"), new GUIContent("自定义曲线"));
            if (GUILayout.Button("重建查找表"))
            {
                foreach (var t in targets)
                {
                    var ease = t as EasingAsset;
                    if (ease != null)
                    {
                        ease.Rebuild();
                        EditorUtility.SetDirty(ease);
                    }
                }
            }
            serializedObject.ApplyModifiedProperties();
            if (GUI.changed)
            {
                foreach (var t in targets)
                {
                    var ease = t as EasingAsset;
                    if (ease != null)
                        ease.Rebuild();
                }
            }
        }
    }

    [CustomEditor(typeof(CineSpline))]
    public sealed class CineSplineEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("tension"), new GUIContent("张力", "0~1，越大拐弯越硬。"));
            CineHelp.Prop(CineTips.Spline, serializedObject.FindProperty("knots"), new GUIContent("控制点"));
            serializedObject.ApplyModifiedProperties();
        }
    }

    [CustomEditor(typeof(CineSequenceAsset))]
    public sealed class CineSequenceAssetEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            CineHelp.Prop(CineTips.Sequence, serializedObject.FindProperty("timeline"), new GUIContent("关联 Timeline"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("rows"), new GUIContent("分镜行"), true);
            serializedObject.ApplyModifiedProperties();
            if (GUILayout.Button("打开分镜表窗口"))
                StoryboardWindow.Open();
        }
    }

}
