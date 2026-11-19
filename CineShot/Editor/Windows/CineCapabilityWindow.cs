using System.Text;
using CineShot.Compat;
using CineShot.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CineShot.Editor
{
    public sealed class CineCapabilityWindow : EditorWindow
    {
        [MenuItem("Tools/Cine/兼容性与能力报告")]
        public static void Open()
        {
            GetWindow<CineCapabilityWindow>("能力报告");
        }

        void OnEnable()
        {
            CineCapabilityProbe.RunOnce();
        }

        void OnGUI()
        {
            var c = CineCapabilityProbe.Current;
            EditorGUILayout.LabelField("Cinemachine " + c.Version + " · " + (c.CinemachineV3 ? "CM3" : "CM2"), EditorStyles.boldLabel);
            EditorGUILayout.LabelField("缺少的能力自动降级为空实现，不阻断编译。", EditorStyles.miniLabel);
            DrawRow("Cinemachine", c.CinemachinePresent ? (c.CinemachineV3 ? "CM3" : "CM2") : "缺失", "运镜写入哪种虚拟相机", "缺失时运镜不输出");
            DrawRow("URP 景深", c.UrpVolumeDof ? "可写" : "空实现", "能否用 Volume 写 DepthOfField", "不可写时焦点只算不显示");
            DrawRow("渲染管线", PipelineName(), "当前 Graphics 设置里的管线", "非 URP 时没有景深");
            EditorGUILayout.Space();
            if (GUILayout.Button("复制诊断信息"))
                EditorGUIUtility.systemCopyBuffer = BuildReport(in c);
        }

        static void DrawRow(string name, string state, string plain, string impact)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("[" + state + "] " + name, GUILayout.Width(220));
                EditorGUILayout.LabelField(plain + " → " + impact);
            }
        }

        static string PipelineName()
        {
            var rp = GraphicsSettings.currentRenderPipeline;
            return rp != null ? rp.GetType().Name : "Built-in";
        }

        static string BuildReport(in CineCapabilities c)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# CineShot 能力报告");
            sb.AppendLine("- Unity: " + Application.unityVersion);
            sb.AppendLine("- Cinemachine: " + c.Version + (c.CinemachineV3 ? " (CM3)" : " (CM2)"));
            sb.AppendLine("- 管线: " + PipelineName());
            sb.AppendLine("- URP 景深: " + c.UrpVolumeDof);
            return sb.ToString();
        }
    }

    public static class CineMenus
    {
        [MenuItem("Tools/Cine/复制为变体 %#&d")]
        public static void DuplicateVariant()
        {
            var shot = Selection.activeObject as ShotAsset;
            if (shot == null)
            {
                EditorUtility.DisplayDialog("CineShot", "请先选中 ShotAsset。", "确定");
                return;
            }
            Selection.activeObject = ShotLibrary.DuplicateAsVariant(shot);
        }
    }
}
