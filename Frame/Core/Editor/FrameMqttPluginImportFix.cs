using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Frame.Core.Editor
{
    /// <summary>
    /// 保证 Frame/Plugins/MQTTnet.dll 作为唯一启用的 MQTTnet 程序集。
    /// 若被标成 RoslynAnalyzer，Unity 会以 -analyzer 而不是 -r 引用，导致 CS0246。
    /// </summary>
    [InitializeOnLoad]
    internal static class FrameMqttPluginImportFix
    {
        const string PackagePluginPath = "Packages/com.nonsensicallab.nonsensicalkit.simulation/Frame/Plugins/MQTTnet.dll";

        static FrameMqttPluginImportFix()
        {
            EditorApplication.delayCall += Fix;
        }

        private static void Fix()
        {
            var asset = AssetDatabase.LoadMainAssetAtPath(PackagePluginPath);
            if (asset == null)
            {
                return;
            }

            var labels = AssetDatabase.GetLabels(asset);
            if (labels.Any(l => l == "RoslynAnalyzer"))
            {
                AssetDatabase.SetLabels(asset, labels.Where(l => l != "RoslynAnalyzer").ToArray());
                Debug.Log("Frame: 已从 MQTTnet.dll 移除 RoslynAnalyzer 标签，使其作为普通程序集引用。");
            }

            var importer = PluginImporter.GetAtPath(PackagePluginPath) as PluginImporter;
            if (importer != null)
            {
                importer.SetCompatibleWithAnyPlatform(true);
                importer.SetCompatibleWithEditor(true);
            }

            DisableOtherMqttPlugins();
        }

        private static void DisableOtherMqttPlugins()
        {
            foreach (var path in AssetDatabase.GetAllAssetPaths())
            {
                if (!path.EndsWith("/MQTTnet.dll") || path == PackagePluginPath)
                {
                    continue;
                }

                var importer = PluginImporter.GetAtPath(path) as PluginImporter;
                if (importer == null)
                {
                    continue;
                }

                importer.SetCompatibleWithAnyPlatform(false);
                importer.SetCompatibleWithEditor(false);
            }
        }
    }
}
