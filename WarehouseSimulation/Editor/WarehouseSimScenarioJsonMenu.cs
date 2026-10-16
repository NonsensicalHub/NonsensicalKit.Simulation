#if UNITY_EDITOR

using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Tests;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Editor
{
    /// <summary>Scenario 资产 ↔ JSON 导入导出。</summary>
    public static class WarehouseSimScenarioJsonMenu
    {
        [MenuItem("Assets/Warehouse Simulation/导出 Scenario 为 JSON", true)]
        private static bool ValidateExport() => Selection.activeObject is WarehouseSimScenario;

        [MenuItem("Assets/Warehouse Simulation/导出 Scenario 为 JSON")]
        public static void ExportSelected()
        {
            var scenario = Selection.activeObject as WarehouseSimScenario;
            if (scenario == null)
            {
                return;
            }

            var assetPath = AssetDatabase.GetAssetPath(scenario);
            var defaultName = Path.GetFileNameWithoutExtension(assetPath) + ".json";
            var savePath = EditorUtility.SaveFilePanel(
                "导出任务配置 JSON",
                Application.persistentDataPath,
                defaultName,
                "json");
            if (string.IsNullOrEmpty(savePath))
            {
                return;
            }

            var hardwarePath = TryFindResourcesPath(scenario.Hardware);
            var strategyPath = TryFindResourcesPath(scenario.Strategy);
            var dto = WarehouseSimScenarioJson.FromScenario(
                scenario,
                Path.GetFileNameWithoutExtension(savePath),
                hardwarePath,
                strategyPath);
            var json = WarehouseSimScenarioJson.ToJson(dto, indented: true);
            File.WriteAllText(savePath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            // 同步写入运行时存档目录，便于 Play 模式立刻加载。
            WarehouseSimScenarioStore.Save(dto, out _);
            EditorUtility.RevealInFinder(savePath);
            Debug.Log($"[WarehouseSimulation] 已导出 Scenario JSON：{savePath}");
        }

        [MenuItem("Assets/Warehouse Simulation/从 JSON 导入到 Scenario", true)]
        private static bool ValidateImport() => Selection.activeObject is WarehouseSimScenario;

        [MenuItem("Assets/Warehouse Simulation/从 JSON 导入到 Scenario")]
        public static void ImportIntoSelected()
        {
            var scenario = Selection.activeObject as WarehouseSimScenario;
            if (scenario == null)
            {
                return;
            }

            var openPath = EditorUtility.OpenFilePanel("导入任务配置 JSON", Application.persistentDataPath, "json");
            if (string.IsNullOrEmpty(openPath))
            {
                return;
            }

            try
            {
                var json = File.ReadAllText(openPath, Encoding.UTF8);
                var dto = WarehouseSimScenarioJson.FromJson(json);
                ApplyDtoToAsset(scenario, dto);
                EditorUtility.SetDirty(scenario);
                AssetDatabase.SaveAssets();
                Debug.Log($"[WarehouseSimulation] 已从 JSON 导入到 {AssetDatabase.GetAssetPath(scenario)}");
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog("导入失败", ex.Message, "OK");
            }
        }

        [MenuItem("Assets/Create/Warehouse Simulation/Scenario From JSON…", priority = 21)]
        public static void CreateScenarioFromJson()
        {
            var openPath = EditorUtility.OpenFilePanel("选择任务配置 JSON", Application.persistentDataPath, "json");
            if (string.IsNullOrEmpty(openPath))
            {
                return;
            }

            try
            {
                var json = File.ReadAllText(openPath, Encoding.UTF8);
                var dto = WarehouseSimScenarioJson.FromJson(json);
                var folder = "Assets";
                if (Selection.activeObject != null)
                {
                    var selPath = AssetDatabase.GetAssetPath(Selection.activeObject);
                    if (AssetDatabase.IsValidFolder(selPath))
                    {
                        folder = selPath;
                    }
                    else if (!string.IsNullOrEmpty(selPath))
                    {
                        folder = Path.GetDirectoryName(selPath)?.Replace('\\', '/') ?? "Assets";
                    }
                }

                var assetPath = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{dto.Id}.asset");
                var scenario = ScriptableObject.CreateInstance<WarehouseSimScenario>();
                ApplyDtoToAsset(scenario, dto);
                AssetDatabase.CreateAsset(scenario, assetPath);
                AssetDatabase.SaveAssets();
                Selection.activeObject = scenario;
                Debug.Log($"[WarehouseSimulation] 已从 JSON 创建 Scenario：{assetPath}");
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog("创建失败", ex.Message, "OK");
            }
        }

        [MenuItem("Tools/Warehouse Simulation/Run Scenario JSON Self-Tests")]
        public static void RunJsonSelfTests()
        {
            var report = WarehouseSimScenarioJsonSelfTests.RunAll();
            if (report.Contains("ALL PASSED") && !report.Contains("FAILED:"))
            {
                Debug.Log(report);
                EditorUtility.DisplayDialog("Scenario JSON", "ALL PASSED", "OK");
            }
            else
            {
                Debug.LogError(report);
                EditorUtility.DisplayDialog("Scenario JSON", "有失败项，详见 Console。", "OK");
            }
        }

        private static void ApplyDtoToAsset(WarehouseSimScenario scenario, WarehouseSimScenarioDto dto)
        {
            WarehouseSimScenarioJson.Normalize(dto);

            if (WarehouseSimScenarioJson.TryResolveHardware(dto.HardwareResourcesPath, out var hardware, out _))
            {
                scenario.Hardware = hardware;
            }

            var runtime = WarehouseSimScenarioJson.ToRuntimeScenario(dto, out var error);
            if (runtime == null)
            {
                throw new System.InvalidOperationException(error ?? "无法构建 Scenario。");
            }

            scenario.FlowPlan = runtime.FlowPlan;
            scenario.FlowRandomSeed = runtime.FlowRandomSeed;
            scenario.InitialOccupancyRatio = runtime.InitialOccupancyRatio;
            scenario.InitialOccupancyRandom = runtime.InitialOccupancyRandom;

            // 策略：若能找到 Resources 资产则引用它，否则把内联值写到现有 Strategy 或新建默认资产旁。
            if (!string.IsNullOrWhiteSpace(dto.StrategyResourcesPath))
            {
                var strategy = Resources.Load<WarehouseSimStrategyProfile>(dto.StrategyResourcesPath);
                if (strategy != null)
                {
                    scenario.Strategy = strategy;
                }
            }

            if (scenario.Strategy == null)
            {
                scenario.Strategy = runtime.Strategy;
            }
            else if (dto.Strategy != null)
            {
                scenario.Strategy.StackerPlacementStrategy = runtime.Strategy.StackerPlacementStrategy;
                scenario.Strategy.InfeedPortSelectionStrategy = runtime.Strategy.InfeedPortSelectionStrategy;
                scenario.Strategy.ConveyorRoutingStrategy = runtime.Strategy.ConveyorRoutingStrategy;
                scenario.Strategy.RouteDistanceWeight = runtime.Strategy.RouteDistanceWeight;
                scenario.Strategy.RouteCongestedCargoWeight = runtime.Strategy.RouteCongestedCargoWeight;
                scenario.Strategy.RouteTotalCargoWeight = runtime.Strategy.RouteTotalCargoWeight;
                EditorUtility.SetDirty(scenario.Strategy);
            }

            if (scenario.Strategy != runtime.Strategy && runtime.Strategy != null)
            {
                Object.DestroyImmediate(runtime.Strategy);
            }

            Object.DestroyImmediate(runtime);
        }

        private static string TryFindResourcesPath(Object asset)
        {
            if (asset == null)
            {
                return null;
            }

            var path = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            const string marker = "/Resources/";
            var idx = path.Replace('\\', '/').IndexOf(marker, System.StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                return null;
            }

            var relative = path.Substring(idx + marker.Length);
            return Path.ChangeExtension(relative, null)?.Replace('\\', '/');
        }
    }
}

#endif