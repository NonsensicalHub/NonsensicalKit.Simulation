using System;
using UnityEditor;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation.Editor
{
    /// <summary>定长数组 Inspector：不画 Size 输入框，避免与运行时固定长度冲突。</summary>
    internal static class FixedSizeArrayGui
    {
        public static void EnsureSize(SerializedProperty arrayProp, int fixedSize)
        {
            if (arrayProp == null || !arrayProp.isArray || fixedSize < 0)
                return;
            if (arrayProp.arraySize != fixedSize)
                arrayProp.arraySize = fixedSize;
        }

        public static void Draw(
            SerializedProperty arrayProp,
            int fixedSize,
            GUIContent foldoutLabel,
            Func<int, GUIContent> elementLabel = null)
        {
            if (arrayProp == null || !arrayProp.isArray)
                return;

            EnsureSize(arrayProp, fixedSize);

            arrayProp.isExpanded = EditorGUILayout.Foldout(
                arrayProp.isExpanded, foldoutLabel, true);
            if (!arrayProp.isExpanded)
                return;

            EditorGUI.indentLevel++;
            for (int i = 0; i < fixedSize; i++)
            {
                SerializedProperty elem = arrayProp.GetArrayElementAtIndex(i);
                GUIContent label = elementLabel != null
                    ? elementLabel(i)
                    : new GUIContent($"Element {i}");
                EditorGUILayout.PropertyField(elem, label, true);
            }

            EditorGUI.indentLevel--;
        }
    }
}
