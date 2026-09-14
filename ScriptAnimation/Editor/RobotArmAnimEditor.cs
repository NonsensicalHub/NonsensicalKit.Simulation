using System.Text;
using UnityEditor;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation.Editor
{
    [CustomEditor(typeof(RobotArmAnim))]
    public class RobotArmAnimEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            RobotArmAnimInspectorGui.DrawSerializedFields(
                serializedObject, RobotArmAnim.JointCountFixed);
            serializedObject.ApplyModifiedProperties();

            var anim = (RobotArmAnim)target;
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "配置规则：\n" +
                "• 轴点空对象须父子串联：Root → J1 → J2 → … → 末端轴点。\n" +
                "• J1 轴点须为机械臂根的直接子对象，且建议 localPosition = (0,0,0)。\n" +
                "• 驱动关节留空 = 轴点自身旋转；连杆 = 子轴点相对驱动关节的偏移。\n" +
                "• 「末端保持向下」：J5→TCP 全程对齐世界下方（腕心 IK）；\n" +
                "  第 6 轴由 Timeline 点位「指定第六轴角」驱动，并与朝下同帧求解。\n" +
                "• Clip 不再单独配置末端朝下，避免双开关冲突导致预览姿态错乱。",
                MessageType.Info);

            DrawValidation(anim);
            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(!anim.AreAxisPointsAssigned() && !anim.AreJointsAssigned()))
            {
                if (GUILayout.Button("测量连杆（由轴点推算）"))
                {
                    Undo.RecordObject(anim, "Measure RobotArm Links");
                    if (!anim.HasRestPose)
                        anim.CaptureRestPoseFromCurrent();
                    int n = anim.MeasureLinkLengthsFromHierarchy();
                    EditorUtility.SetDirty(anim);
                    Debug.Log($"[RobotArmAnim] 已测量 {n} 节连杆（轴数 {anim.JointCount}）", anim);
                }
            }

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(anim.IkTarget == null))
            {
                if (GUILayout.Button("IK 预览到目标"))
                {
                    if (!anim.AreJointsAssigned())
                    {
                        EditorUtility.DisplayDialog("IK 预览", "请先指定全部轴点 / 驱动关节。", "确定");
                    }
                    else
                    {
                        Undo.RecordObject(anim, "RobotArm IK Preview");
                        RecordJointTransforms(anim, "RobotArm IK Preview");
                        anim.EnsureRuntimeSetup(captureRest: false);
                        bool ok = anim.TrySolveIkToTarget();
                        EditorUtility.SetDirty(anim);
                        if (!ok)
                            Debug.LogWarning("[RobotArmAnim] IK 未在容差内收敛（已应用最近解）。", anim);
                        else
                            Debug.Log("[RobotArmAnim] IK 预览成功。", anim);
                        SceneView.RepaintAll();
                    }
                }
            }

            using (new EditorGUI.DisabledScope(!anim.AreJointsAssigned()))
            {
                if (GUILayout.Button("还原初始姿态"))
                {
                    Undo.RecordObject(anim, "Restore RobotArm Home");
                    RecordJointTransforms(anim, "Restore RobotArm Home");
                    anim.ApplyHomePose();
                    EditorUtility.SetDirty(anim);
                    SceneView.RepaintAll();
                }
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("捕获 Rest"))
            {
                Undo.RecordObject(anim, "Capture RobotArm Rest");
                anim.CaptureRestPoseFromCurrent();
                EditorUtility.SetDirty(anim);
            }

            if (GUILayout.Button("捕获 Home"))
            {
                Undo.RecordObject(anim, "Capture RobotArm Home");
                anim.CaptureHomeFromCurrent();
                EditorUtility.SetDirty(anim);
            }

            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("打印当前姿态（Console）"))
                anim.LogCurrentPose();

            if (anim.DebugForcePlayback)
            {
                EditorGUILayout.HelpBox(
                    "已开启「强制播放（禁缓存）」：Timeline 预览将每帧完整重解 IK，" +
                    "并在 Console 输出 [RobotArm DBG] 日志。",
                    MessageType.Warning);
            }

            DrawLinkSummary(anim);
        }

        private static void RecordJointTransforms(RobotArmAnim anim, string undoName)
        {
            if (anim.Joints == null)
                return;
            foreach (var joint in anim.Joints)
            {
                Transform t = RobotArmAnim.ResolveDriveJoint(joint);
                if (t != null)
                    Undo.RecordObject(t, undoName);
            }
        }

        private static void DrawValidation(RobotArmAnim anim)
        {
            bool ok = anim.Validate(out var errors, out var warnings);
            if (!ok)
            {
                var sb = new StringBuilder();
                foreach (string e in errors)
                    sb.AppendLine("—" + e);
                EditorGUILayout.HelpBox("配置错误：\n" + sb, MessageType.Error);
            }
            else if (warnings.Count > 0)
            {
                var sb = new StringBuilder();
                foreach (string w in warnings)
                    sb.AppendLine("—" + w);
                EditorGUILayout.HelpBox("提示：\n" + sb, MessageType.Warning);
            }
            else
            {
                EditorGUILayout.HelpBox("配置校验通过。", MessageType.None);
            }
        }

        private static void DrawLinkSummary(RobotArmAnim anim)
        {
            if (!anim.AreJointsAssigned() && !anim.AreAxisPointsAssigned())
                return;

            var sb = new StringBuilder(160);
            sb.AppendLine($"轴数 {anim.JointCount}（定死六轴；臂 J1–J3 / 腕自 J{anim.WristStartIndex + 1}）");
            if (anim.KeepTipDown)
                sb.AppendLine($"朝下约束：J{anim.TipDownJointIndex + 1} 连杆方向 → 世界下方；J6 由 Clip 点位目标角驱动");
            var joints = anim.Joints;
            for (int i = 0; i < joints.Length; i++)
            {
                var s = joints[i];
                if (s == null) continue;
                string pointName = s.AxisPoint != null ? s.AxisPoint.name : "(未指定";
                sb.AppendLine($"J{i + 1} [{pointName}]: L={s.LinkLength:F3} m  dir={s.LinkDirectionLocal}");
            }

            sb.Append($"ToolOffset={anim.ToolOffsetLocal}");
            EditorGUILayout.LabelField("连杆摘要", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(sb.ToString().TrimEnd(), MessageType.None);
        }
    }
}
