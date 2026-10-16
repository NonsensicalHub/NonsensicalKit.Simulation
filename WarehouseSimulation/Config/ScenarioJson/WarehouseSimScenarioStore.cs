using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Config
{
    /// <summary>
    /// 任务配置档 JSON 存档：PC / Editor 写本地目录；WebGL 写 persistentDataPath（IndexedDB），
    /// 另支持浏览器下载与从文本导入。
    /// </summary>
    public static class WarehouseSimScenarioStore
    {
        public const string RelativeDirectory = "WarehouseSim/Scenarios";
        public const string FileExtension = ".json";

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void WarehouseSimDownloadText(string fileName, string mimeType, string content);
#endif

        public static string DirectoryPath =>
            Path.Combine(Application.persistentDataPath, RelativeDirectory);

        public static string GetFilePath(string id)
        {
            var safe = WarehouseSimScenarioJson.SanitizeId(id);
            return Path.Combine(DirectoryPath, safe + FileExtension);
        }

        public static void EnsureDirectory()
        {
            if (!Directory.Exists(DirectoryPath))
            {
                Directory.CreateDirectory(DirectoryPath);
            }
        }

        public static IReadOnlyList<string> ListIds()
        {
            EnsureDirectory();
            var files = Directory.GetFiles(DirectoryPath, "*" + FileExtension);
            var ids = new List<string>(files.Length);
            for (var i = 0; i < files.Length; i++)
            {
                ids.Add(Path.GetFileNameWithoutExtension(files[i]));
            }

            ids.Sort(StringComparer.OrdinalIgnoreCase);
            return ids;
        }

        public static bool Exists(string id) => File.Exists(GetFilePath(id));

        public static string Save(WarehouseSimScenarioDto dto, out string error)
        {
            error = null;
            if (dto == null)
            {
                error = "配置为空。";
                return null;
            }

            WarehouseSimScenarioJson.Normalize(dto);
            try
            {
                EnsureDirectory();
                var path = GetFilePath(dto.Id);
                var json = WarehouseSimScenarioJson.ToJson(dto, indented: true);
                File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                return path;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        public static WarehouseSimScenarioDto Load(string id, out string error)
        {
            error = null;
            try
            {
                var path = GetFilePath(id);
                if (!File.Exists(path))
                {
                    error = $"找不到存档：{id}";
                    return null;
                }

                var json = File.ReadAllText(path, Encoding.UTF8);
                return WarehouseSimScenarioJson.FromJson(json);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        public static WarehouseSimScenarioDto LoadFromJsonText(string json, out string error)
        {
            error = null;
            try
            {
                return WarehouseSimScenarioJson.FromJson(json);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        public static bool Delete(string id, out string error)
        {
            error = null;
            try
            {
                var path = GetFilePath(id);
                if (!File.Exists(path))
                {
                    error = $"找不到存档：{id}";
                    return false;
                }

                File.Delete(path);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 导出到用户可读位置：PC 额外写一份到指定路径或下载目录旁；
        /// WebGL 触发浏览器下载；同时复制到剪贴板便于粘贴。
        /// </summary>
        public static bool ExportForShare(WarehouseSimScenarioDto dto, out string message)
        {
            message = null;
            if (dto == null)
            {
                message = "配置为空。";
                return false;
            }

            WarehouseSimScenarioJson.Normalize(dto);
            var json = WarehouseSimScenarioJson.ToJson(dto, indented: true);
            var fileName = dto.Id + FileExtension;

            try
            {
                GUIUtility.systemCopyBuffer = json;
            }
            catch
            {
                // 部分平台剪贴板不可用，忽略。
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            try
            {
                WarehouseSimDownloadText(fileName, "application/json", json);
                message = $"已触发浏览器下载：{fileName}（内容也已复制到剪贴板）";
                return true;
            }
            catch (Exception ex)
            {
                message = $"Web 下载失败：{ex.Message}。JSON 已尝试复制到剪贴板。";
                return false;
            }
#else
            try
            {
                EnsureDirectory();
                var path = Path.Combine(DirectoryPath, fileName);
                File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                message = $"已保存：{path}（并已复制到剪贴板）";
                return true;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return false;
            }
#endif
        }

#if !UNITY_WEBGL || UNITY_EDITOR
        /// <summary>PC：用系统对话框另存为（依赖 NonsensicalKit FileTool）。</summary>
        public static bool SaveAsDialog(WarehouseSimScenarioDto dto, out string path, out string error)
        {
            path = null;
            error = null;
            if (dto == null)
            {
                error = "配置为空。";
                return false;
            }

            WarehouseSimScenarioJson.Normalize(dto);
            try
            {
                var chosen = NonsensicalKit.Tools.FileTool.FileSaveSelector(
                    "任务配置 JSON",
                    DirectoryPath,
                    "json");
                if (string.IsNullOrEmpty(chosen))
                {
                    error = "已取消。";
                    return false;
                }

                if (!chosen.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    chosen += ".json";
                }

                var json = WarehouseSimScenarioJson.ToJson(dto, indented: true);
                var dir = Path.GetDirectoryName(chosen);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(chosen, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                path = chosen;

                var fileId = Path.GetFileNameWithoutExtension(chosen);
                dto.Id = WarehouseSimScenarioJson.SanitizeId(fileId);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>PC：用系统对话框打开 JSON。</summary>
        public static WarehouseSimScenarioDto LoadFromDialog(out string path, out string error)
        {
            path = null;
            error = null;
            try
            {
                var chosen = NonsensicalKit.Tools.FileTool.FileSelector("任务配置 JSON", "json");
                if (string.IsNullOrEmpty(chosen))
                {
                    error = "已取消。";
                    return null;
                }

                path = chosen;
                var json = File.ReadAllText(chosen, Encoding.UTF8);
                var dto = WarehouseSimScenarioJson.FromJson(json);
                if (string.IsNullOrWhiteSpace(dto.Id))
                {
                    dto.Id = WarehouseSimScenarioJson.SanitizeId(Path.GetFileNameWithoutExtension(chosen));
                }

                return dto;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }
#endif
    }
}