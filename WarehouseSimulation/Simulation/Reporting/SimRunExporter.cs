using System;
using System.IO;
using System.Text;
using UnityEngine;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Simulation
{
    /// <summary>将一次本地仿真运行导出为人类可读的 HTML 任务报告与开发者 Markdown 调试报告。</summary>
    public static class SimRunExporter
    {
        public static string DefaultExportDirectory =>
            Path.Combine(Application.streamingAssetsPath, "SimulationExports");

        /// <summary>生成面向使用者的仿真 HTML 报告文本。</summary>
        public static string BuildHtmlReport(
            WarehouseSimScenario scenario,
            StackerWarehouseSimulator simulator,
            SimRunResult result,
            string debugMarkdownFileName = null,
            SimRunExportDisplayInfo displayInfo = default) =>
            SimRunHtmlReportBuilder.Build(scenario, simulator, result, debugMarkdownFileName, displayInfo);

        /// <summary>生成面向开发者的调试信息 Markdown 报告文本。</summary>
        public static string BuildDebugMarkdownReport(
            WarehouseSimScenario scenario,
            StackerWarehouseSimulator simulator,
            SimRunResult result,
            string linkedUserReportFileName = null,
            SimRunExportDisplayInfo displayInfo = default) =>
            SimRunDebugMarkdownReportBuilder.Build(scenario, simulator, result, linkedUserReportFileName, displayInfo);

        /// <summary>写入 HTML 文件，返回最终路径。</summary>
        public static string ExportHtmlToFile(
            string html,
            string directory = null,
            string fileName = null)
        {
            if (string.IsNullOrEmpty(html))
            {
                throw new ArgumentException("报告内容为空。", nameof(html));
            }

            directory = ResolveDirectory(directory);
            Directory.CreateDirectory(directory);

            fileName ??= BuildDefaultFileName(null, "report", ".html");
            if (!fileName.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            {
                fileName += ".html";
            }

            var path = Path.GetFullPath(Path.Combine(directory, fileName));
            File.WriteAllText(path, html, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            return path;
        }

        /// <summary>写入 Markdown 文件，返回最终路径。</summary>
        public static string ExportMarkdownToFile(
            string markdown,
            string directory = null,
            string fileName = null)
        {
            if (string.IsNullOrEmpty(markdown))
            {
                throw new ArgumentException("报告内容为空。", nameof(markdown));
            }

            directory = ResolveDirectory(directory);
            Directory.CreateDirectory(directory);

            fileName ??= BuildDefaultFileName(null, "debug", ".md");
            if (!fileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            {
                fileName += ".md";
            }

            var path = Path.GetFullPath(Path.Combine(directory, fileName));
            File.WriteAllText(path, markdown, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            return path;
        }

        /// <summary>解析导出目录（须在主线程调用，会访问 Unity Application 路径）。</summary>
        public static string ResolveExportDirectory(string directory = null) => ResolveDirectory(directory);

        /// <summary>一步导出最近一次仿真运行（使用者 HTML 报告 + 开发者 Markdown 调试报告）。</summary>
        public static string ExportLastRun(
            WarehouseSimScenario scenario,
            StackerWarehouseSimulator simulator,
            SimRunResult result,
            string directory = null,
            string fileName = null)
        {
            var displayInfo = SimRunExportDisplayInfo.Capture(scenario);
            var (htmlPath, debugPath) = ExportLastRunAtDirectory(
                scenario,
                simulator,
                result,
                ResolveDirectory(directory),
                fileName,
                displayInfo);
            result.DebugReportPath = debugPath;
            return htmlPath;
        }

        /// <summary>
        /// 在已解析的绝对目录下导出报告；生成 HTML/Markdown 与写文件可在后台线程调用。
        /// </summary>
        public static (string HtmlPath, string DebugReportPath) ExportLastRunAtDirectory(
            WarehouseSimScenario scenario,
            StackerWarehouseSimulator simulator,
            SimRunResult result,
            string resolvedDirectory,
            string fileName = null,
            SimRunExportDisplayInfo displayInfo = default)
        {
            if (string.IsNullOrWhiteSpace(resolvedDirectory))
            {
                throw new ArgumentException("导出目录为空。", nameof(resolvedDirectory));
            }

            resolvedDirectory = Path.GetFullPath(resolvedDirectory);
            Directory.CreateDirectory(resolvedDirectory);

            var stamp = SimReportFormatting.FormatExportFileStamp();
            var scenarioName = displayInfo.ScenarioName;
            fileName ??= BuildDefaultFileName(scenarioName, "report", ".html", stamp);
            var userFileName = EnsureExtension(fileName, ".html");
            var debugFileName = BuildDefaultFileName(scenarioName, "debug", ".md", stamp);
            var debugMarkdown = BuildDebugMarkdownReport(scenario, simulator, result, userFileName, displayInfo);
            var debugPath = ExportMarkdownToFile(debugMarkdown, resolvedDirectory, debugFileName);
            var userHtml = BuildHtmlReport(scenario, simulator, result, debugFileName, displayInfo);
            var htmlPath = ExportHtmlToFile(userHtml, resolvedDirectory, userFileName);
            return (htmlPath, debugPath);
        }

        /// <summary>在系统文件管理器中打开报告导出目录（仅 PC）。</summary>
        public static void OpenExportDirectory(string directory = null)
        {
            directory = ResolveDirectory(directory);

            Directory.CreateDirectory(directory);

#if UNITY_EDITOR
            UnityEditor.EditorUtility.RevealInFinder(directory);
#elif UNITY_STANDALONE_WIN
            System.Diagnostics.Process.Start("explorer.exe", directory);
#elif UNITY_STANDALONE_OSX
            System.Diagnostics.Process.Start("open", directory);
#elif UNITY_STANDALONE_LINUX
            System.Diagnostics.Process.Start("xdg-open", directory);
#else
            Debug.LogWarning($"[WarehouseSimulation] 当前平台不支持打开报告目录：{directory}");
#endif
        }

        private static string ProjectRootDirectory =>
            Path.GetDirectoryName(Application.dataPath);

        private static string ResolveDirectory(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return Path.GetFullPath(DefaultExportDirectory);
            }

            return Path.IsPathRooted(directory)
                ? Path.GetFullPath(directory)
                : Path.GetFullPath(Path.Combine(ProjectRootDirectory, directory));
        }

        private static string BuildDefaultFileName(
            string scenarioName,
            string suffix,
            string extension,
            string timestampStamp = null)
        {
            var safeName = string.IsNullOrWhiteSpace(scenarioName)
                ? "inbound_sim"
                : SanitizeFileName(scenarioName);
            var stamp = timestampStamp ?? SimReportFormatting.FormatExportFileStamp();
            return $"{safeName}_{suffix}_{stamp}{extension}";
        }

        private static string EnsureExtension(string fileName, string extension)
        {
            if (!fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                return fileName + extension;
            }

            return fileName;
        }

        private static string SanitizeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }

            return name;
        }
    }
}
