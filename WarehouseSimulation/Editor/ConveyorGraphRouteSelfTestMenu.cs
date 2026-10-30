using UnityEditor;
using UnityEngine;
using NonsensicalKit.Simulation.WarehouseSimulation.Tests;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Editor
{
    /// <summary>输送 zone 内核与场景 JSON 自测（不依赖 Test Framework 包）。</summary>
    public static class ConveyorGraphRouteSelfTestMenu
    {
        [MenuItem("Tools/Warehouse Simulation/Run Zone Kernel Self-Tests")]
        public static void RunZoneKernel()
        {
            Show("Zone Kernel", "=== ZoneKernel ===\n" + ConveyorZoneKernelSelfTests.RunAll());
        }

        [MenuItem("Tools/Warehouse Simulation/Run All Warehouse Sim Self-Tests")]
        public static void RunAll()
        {
            var report = "=== ZoneKernel ===\n" + ConveyorZoneKernelSelfTests.RunAll()
                         + "\n=== ScenarioJSON ===\n" + WarehouseSimScenarioJsonSelfTests.RunAll();
            Show("Warehouse Sim Self-Tests", report);
        }

        /// <summary>每个 "=== Name ===" 段都必须以 ALL PASSED 结束且不含 FAILED。</summary>
        private static void Show(string title, string report)
        {
            var sections = System.Text.RegularExpressions.Regex.Split(report, @"=== \w+ ===");
            var ok = false;
            foreach (var s in sections)
            {
                if (string.IsNullOrWhiteSpace(s))
                {
                    continue;
                }

                ok = true;
                if (!s.Contains("ALL PASSED") || s.Contains("FAILED:"))
                {
                    ok = false;
                    break;
                }
            }

            if (ok)
            {
                Debug.Log(report);
                EditorUtility.DisplayDialog(title, "ALL PASSED", "OK");
            }
            else
            {
                Debug.LogError(report);
                EditorUtility.DisplayDialog(title, "有失败项，详见 Console。", "OK");
            }
        }
    }
}
