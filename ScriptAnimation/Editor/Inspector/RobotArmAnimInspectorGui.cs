using UnityEditor;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation.Editor
{
    /// <summary>六轴 / 五轴机械臂 Inspector 共用绘制（定长数组不露 Size）。</summary>
    internal static class RobotArmAnimInspectorGui
    {
        public static void DrawSerializedFields(SerializedObject serializedObject, int jointCount)
        {
            SerializedProperty iterator = serializedObject.GetIterator();
            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;

                if (iterator.propertyPath == "m_Script")
                {
                    using (new EditorGUI.DisabledScope(true))
                        EditorGUILayout.PropertyField(iterator);
                    continue;
                }

                switch (iterator.name)
                {
                    case "m_joints":
                        EditorGUILayout.Space(2f);
                        EditorGUILayout.LabelField(
                            "轴链（轴点须父子串联：J1 → J2 → … → 末端）",
                            EditorStyles.boldLabel);
                        DrawJoints(iterator, jointCount);
                        break;
                    case "m_homeAngles":
                        EditorGUILayout.Space(2f);
                        EditorGUILayout.LabelField(
                            "Home（无前序 / 未指定起点时的回退关节角）",
                            EditorStyles.boldLabel);
                        FixedSizeArrayGui.Draw(
                            iterator,
                            jointCount,
                            new GUIContent(
                                $"Home 关节角（固定 {jointCount} 轴）",
                                "无前序 / 未指定起点时的回退关节角（度）"),
                            i => new GUIContent($"J{i + 1}"));
                        break;
                    case "m_restLocalRotations":
                        FixedSizeArrayGui.Draw(
                            iterator,
                            jointCount,
                            new GUIContent(
                                $"Rest 本地旋转（固定 {jointCount} 轴）",
                                "由「捕获 Rest」写入，一般无需手改"),
                            i => new GUIContent($"J{i + 1}"));
                        break;
                    case "m_hasHome":
                    case "m_hasRestPose":
                        using (new EditorGUI.DisabledScope(true))
                            EditorGUILayout.PropertyField(iterator, true);
                        break;
                    default:
                        EditorGUILayout.PropertyField(iterator, true);
                        break;
                }
            }
        }

        private static void DrawJoints(SerializedProperty joints, int jointCount)
        {
            FixedSizeArrayGui.EnsureSize(joints, jointCount);

            joints.isExpanded = EditorGUILayout.Foldout(
                joints.isExpanded,
                new GUIContent(
                    $"关节列表（固定 {jointCount} 轴）",
                    "轴数由组件类型决定，不可增减"),
                true);
            if (!joints.isExpanded)
                return;

            EditorGUI.indentLevel++;
            for (int i = 0; i < jointCount; i++)
                DrawJointElement(joints.GetArrayElementAtIndex(i), i);
            EditorGUI.indentLevel--;
        }

        private static void DrawJointElement(SerializedProperty elem, int index)
        {
            elem.isExpanded = EditorGUILayout.Foldout(
                elem.isExpanded, new GUIContent($"J{index + 1}"), true);
            if (!elem.isExpanded)
                return;

            EditorGUI.indentLevel++;
            DrawRelative(elem, "AxisPoint");
            DrawRelative(elem, "DriveJoint");
            DrawRelative(elem, "AxisLocal");

            using (new EditorGUI.DisabledScope(true))
            {
                DrawRelative(elem, "LinkLength");
                DrawRelative(elem, "LinkDirectionLocal");
            }

            DrawRelative(elem, "MinAngle");
            DrawRelative(elem, "MaxAngle");
            EditorGUI.indentLevel--;
        }

        private static void DrawRelative(SerializedProperty parent, string relativeName)
        {
            SerializedProperty prop = parent.FindPropertyRelative(relativeName);
            if (prop != null)
                EditorGUILayout.PropertyField(prop, true);
        }
    }
}
