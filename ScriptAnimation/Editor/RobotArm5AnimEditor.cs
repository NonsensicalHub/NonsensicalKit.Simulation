using System.Text;
using UnityEditor;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation.Editor
{
    [CustomEditor(typeof(RobotArm5Anim))]
    public class RobotArm5AnimEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            RobotArmAnimInspectorGui.DrawSerializedFields(
                serializedObject, RobotArm5Anim.JointCountFixed);
            serializedObject.ApplyModifiedProperties();

            var anim = (RobotArm5Anim)target;
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "五轴构型（Rest 时世界轴）：\n" +
                "• J1 = Y（基座回转）\n" +
                "• J2 / J3 / J4 = X（互相平行，始终水平）\n" +
                "• J5 = Y（末端扭转）\n" +
                "• 「末端保持向下」：保持第 5 轴在目标正上方（J5→TCP → 世界下方）；\n" +
                "  俯仰由第 4 轴完成（轴始终水平，一定可朝下）；J5 仅扭转。\n" +
                "• 轴点空对象须父子串联：Root → J1 → J2 → … → 末端轴点。\n" +
                "• 与六轴 RobotArmAnim 独立，勿混用 Clip。",
                MessageType.Info);

            DrawValidation(anim);
            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(!anim.AreAxisPointsAssigned() && !anim.AreJointsAssigned()))
            {
                if (GUILayout.Button("测量连杆（由轴点推算）"))
                {
                    Undo.RecordObject(anim, "Measure RobotArm5 Links");
                    if (!anim.HasRestPose)
                        anim.CaptureRestPoseFromCurrent();
                    int n = anim.MeasureLinkLengthsFromHierarchy();
                    EditorUtility.SetDirty(anim);
                    Debug.Log($"[RobotArm5Anim] 已测量 {n} 节连杆（轴数 {anim.JointCount}）", anim);
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
                        Undo.RecordObject(anim, "RobotArm5 IK Preview");
                        RecordJointTransforms(anim, "RobotArm5 IK Preview");
                        anim.EnsureRuntimeSetup(captureRest: false);
                        bool ok = anim.TrySolveIkToTarget();
                        EditorUtility.SetDirty(anim);
                        if (!ok)
                            Debug.LogWarning("[RobotArm5Anim] IK 未在容差内收敛（已应用最近解）。", anim);
                        else
                            Debug.Log("[RobotArm5Anim] IK 预览成功。", anim);
                        SceneView.RepaintAll();
                    }
                }
            }

            using (new EditorGUI.DisabledScope(!anim.AreJointsAssigned()))
            {
                if (GUILayout.Button("还原初始姿态"))
                {
                    Undo.RecordObject(anim, "Restore RobotArm5 Home");
                    RecordJointTransforms(anim, "Restore RobotArm5 Home");
                    anim.ApplyHomePose();
                    EditorUtility.SetDirty(anim);
                    SceneView.RepaintAll();
                }
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("捕获 Rest"))
            {
                Undo.RecordObject(anim, "Capture RobotArm5 Rest");
                anim.CaptureRestPoseFromCurrent();
                EditorUtility.SetDirty(anim);
            }

            if (GUILayout.Button("捕获 Home"))
            {
                Undo.RecordObject(anim, "Capture RobotArm5 Home");
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
                    "并在 Console 输出 [RobotArm5 DBG] 日志。",
                    MessageType.Warning);
            }

            DrawLinkSummary(anim);
        }

        private static void RecordJointTransforms(RobotArm5Anim anim, string undoName)
        {
            if (anim.Joints == null)
                return;
            foreach (var joint in anim.Joints)
            {
                Transform t = RobotArm5Anim.ResolveDriveJoint(joint);
                if (t != null)
                    Undo.RecordObject(t, undoName);
            }
        }

        private static void DrawValidation(RobotArm5Anim anim)
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

        private static void DrawLinkSummary(RobotArm5Anim anim)
        {
            if (!anim.AreJointsAssigned() && !anim.AreAxisPointsAssigned())
                return;

            var sb = new StringBuilder(160);
            sb.AppendLine($"轴数 {anim.JointCount}（定死五轴；臂 J1–J3 / 俯仰 J4 / 扭转 J5 在目标正上方）");
            if (anim.KeepTipDown)
                sb.AppendLine("朝下约束：J5→TCP → 世界下方（轴5在目标正上方）；J4 俯仰锁朝下；J5 由 Clip 点位目标角驱动");
            var joints = anim.Joints;
            for (int i = 0; i < joints.Length; i++)
            {
                var s = joints[i];
                if (s == null) continue;
                string pointName = s.AxisPoint != null ? s.AxisPoint.name : "(未指定";
                sb.AppendLine($"J{i + 1} [{pointName}]: L={s.LinkLength:F3} m  axis={s.AxisLocal}  dir={s.LinkDirectionLocal}");
            }

            sb.Append($"ToolOffset={anim.ToolOffsetLocal}");
            EditorGUILayout.LabelField("连杆摘要", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(sb.ToString().TrimEnd(), MessageType.None);
        }
    }
}
