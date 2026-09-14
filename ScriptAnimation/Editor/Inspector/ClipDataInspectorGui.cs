using System.Collections.Generic;
using UnityEditor;

namespace NonsensicalKit.ScriptAnimation.Editor
{
    /// <summary>
    /// 绘制 Clip.Data 的子字段，不包一层「参数 / Data」折叠
    /// （Timeline Clip 自身已可折叠，再套一级多余）。
    /// </summary>
    internal static class ClipDataInspectorGui
    {
        public static void DrawChildren(
            SerializedProperty data,
            ICollection<string> skipNames = null)
        {
            if (data == null)
                return;

            SerializedProperty iterator = data.Copy();
            SerializedProperty end = iterator.GetEndProperty();
            if (!iterator.NextVisible(true))
                return;

            do
            {
                if (SerializedProperty.EqualContents(iterator, end))
                    break;
                if (skipNames != null && skipNames.Contains(iterator.name))
                    continue;
                EditorGUILayout.PropertyField(iterator, true);
            } while (iterator.NextVisible(false));
        }
    }
}
