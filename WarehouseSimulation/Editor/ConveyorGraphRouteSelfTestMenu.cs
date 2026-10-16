using UnityEditor;
using UnityEngine;
using NonsensicalKit.Simulation.WarehouseSimulation.Tests;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Editor
{
    /// <summary>阶段1/2/3/5 纯 C# 自测菜单（不依赖 Test Framework 包）。</summary>
    public static class ConveyorGraphRouteSelfTestMenu
    {
        [MenuItem("Tools/Warehouse Simulation/Run Phase1 Graph+Route Self-Tests")]
        public static void RunPhase1()
        {
            Show("Phase1 Graph+Route", "=== Phase1 ===\n" + ConveyorGraphRouteSelfTests.RunAll());
        }

        [MenuItem("Tools/Warehouse Simulation/Run Phase2 ResourceCalendar Self-Tests")]
        public static void RunPhase2()
        {
            Show("Phase2 ResourceCalendar", "=== Phase2 ===\n" + ResourceCalendarSelfTests.RunAll());
        }

        [MenuItem("Tools/Warehouse Simulation/Run Phase3 DES Kernel Self-Tests")]
        public static void RunPhase3()
        {
            Show("Phase3 DES Kernel", "=== Phase3 ===\n" + ConveyorKernelSelfTests.RunAll());
        }

        [MenuItem("Tools/Warehouse Simulation/Run Phase5 Zone Kernel Self-Tests")]
        public static void RunPhase5()
        {
            Show("Phase5 Zone Kernel", "=== Phase5 ===\n" + ConveyorZoneKernelSelfTests.RunAll());
        }

        [MenuItem("Tools/Warehouse Simulation/Run All Phase Self-Tests")]
        public static void RunAllPhases()
        {
            var report = "=== Phase1 ===\n" + ConveyorGraphRouteSelfTests.RunAll()
                         + "\n=== Phase2 ===\n" + ResourceCalendarSelfTests.RunAll()
                         + "\n=== Phase3 ===\n" + ConveyorKernelSelfTests.RunAll()
                         + "\n=== Phase5 ===\n" + ConveyorZoneKernelSelfTests.RunAll()
                         + "\n=== ScenarioJSON ===\n" + WarehouseSimScenarioJsonSelfTests.RunAll();
            Show("All Phases", report);
        }

        /// <summary>每个 "=== PhaseN ===" 段都必须以 ALL PASSED 结束且不含 FAILED。</summary>
        private static void Show(string title, string report)
        {
            var sections = System.Text.RegularExpressions.Regex.Split(report, @"=== (?:Phase|Scenario)\w* ===");
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
