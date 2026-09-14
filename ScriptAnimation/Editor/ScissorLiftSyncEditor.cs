using UnityEditor;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation.Editor
{
    [CustomEditor(typeof(ScissorLiftSync))]
    public class ScissorLiftSyncEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_bottomHinge"), new GUIContent("底铰点"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_topHinge"), new GUIContent("顶铰点"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_platform"), new GUIContent("平台"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_base"), new GUIContent("底盘"));

            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_stageCount"), new GUIContent("层数"));
            serializedObject.ApplyModifiedProperties();

            var sync = (ScissorLiftSync)target;
            sync.EnsureStageArray();
            serializedObject.Update();

            SerializedProperty stages = serializedObject.FindProperty("m_stages");
            for (int i = 0; i < stages.arraySize; i++)
            {
                SerializedProperty stage = stages.GetArrayElementAtIndex(i);
                EditorGUILayout.LabelField($"第 {i + 1} 层", EditorStyles.boldLabel);
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(stage.FindPropertyRelative("LeftNear"), new GUIContent("左近", "近侧 \\"));
                EditorGUILayout.PropertyField(stage.FindPropertyRelative("LeftFar"), new GUIContent("左远", "远侧 \\"));
                EditorGUILayout.PropertyField(stage.FindPropertyRelative("RightNear"), new GUIContent("右近", "近侧 /"));
                EditorGUILayout.PropertyField(stage.FindPropertyRelative("RightFar"), new GUIContent("右远", "远侧 /"));
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_liftAxisLocal"), new GUIContent("抬升轴"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_foldAxisLocal"), new GUIContent("展开轴"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_rotationAxisLocal"), new GUIContent("旋转轴"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_armLength"), new GUIContent("杆长"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_foldSpan"), new GUIContent("展开间距"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_sideSpan"), new GUIContent("两侧间距"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_shrinkSpanWithLift"), new GUIContent("升起时收缩间距"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_pivotAlongArm"), new GUIContent("原点沿杆"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_applyOnce"), new GUIContent("仅初始运算", "开启后只运算一次，之后不再每帧更新。适合静止物体摆姿态。"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_driveInEditMode"), new GUIContent("编辑模式跟随"));
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "平台继续用 LatentAgvAnim / ForkliftAnim 抬升。本组件只同步剪叉：\n" +
                "1. 填层数，每一层固定四根：左近 / 左远 / 右近 / 右远。\n" +
                "2. 底铰点挂底盘，顶铰点挂平台。\n" +
                "3. 收拢姿态捕获 Rest。杆长、展开间距、两侧间距分开调。\n" +
                "4. 静止摆姿可勾选「仅初始运算」，避免每帧同步。",
                MessageType.Info);

            if (sync.TopHinge == null || sync.BottomHinge == null)
                EditorGUILayout.HelpBox("需要底铰点和顶铰点（或指定 Platform 作为顶铰点）。", MessageType.Warning);
            else if (!sync.HasAnyArm())
                EditorGUILayout.HelpBox("还没有指定任何剪叉 Transform。", MessageType.Warning);
            else if (!sync.HasRest)
                EditorGUILayout.HelpBox("尚未捕获 Rest，播放时会用当前姿态当作收拢姿态。", MessageType.Warning);

            EditorGUILayout.LabelField("平台高度", $"{sync.ReadHeight():F3} m");
            EditorGUILayout.LabelField("张角 / 杆长", $"{sync.ReadOpenAngleDegrees():F1}°  /  {sync.ArmLength:F3} m");
            EditorGUILayout.LabelField("展开间距", $"{sync.FoldSpan:F3} m");
            EditorGUILayout.LabelField("两侧间距", $"{sync.SideSpan:F3} m");

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("件 LatentAgv/Forklift/Stacker 绑定平台"))
                {
                    Undo.RecordObject(sync, "Bind Scissor Platform");
                    if (!sync.TryAutoBindPlatform())
                        EditorUtility.DisplayDialog("Scissor Lift Sync", "同物体上没有 LatentAgvAnim.Platform、ForkliftAnim.Fork 或 StackerAnim.LiftAxis。", "确定");
                    EditorUtility.SetDirty(sync);
                }

                if (GUILayout.Button("捕获 Rest"))
                {
                    RecordArms(sync, "Capture Scissor Rest");
                    Undo.RecordObject(sync, "Capture Scissor Rest");
                    sync.CaptureRestPose();
                    EditorUtility.SetDirty(sync);
                }

                if (GUILayout.Button("恢复 Rest"))
                {
                    RecordArms(sync, "Restore Scissor Rest");
                    Undo.RecordObject(sync, "Restore Scissor Rest");
                    sync.RestoreRestPose();
                    EditorUtility.SetDirty(sync);
                }

                if (GUILayout.Button("立即运算"))
                {
                    RecordArms(sync, "Apply Scissor Pose");
                    Undo.RecordObject(sync, "Apply Scissor Pose");
                    sync.ApplyPoseNow();
                    EditorUtility.SetDirty(sync);
                }
            }
        }

        private static void RecordArms(ScissorLiftSync sync, string undo)
        {
            if (sync.Stages == null)
                return;
            foreach (var stage in sync.Stages)
            {
                if (stage == null)
                    continue;
                Record(stage.LeftNear, undo);
                Record(stage.LeftFar, undo);
                Record(stage.RightNear, undo);
                Record(stage.RightFar, undo);
            }
        }

        private static void Record(Transform arm, string undo)
        {
            if (arm != null)
                Undo.RecordObject(arm, undo);
        }
    }
}
