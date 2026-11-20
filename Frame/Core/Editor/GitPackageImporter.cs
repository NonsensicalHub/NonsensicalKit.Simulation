using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System;

namespace Frame.Core.Editor
{
public class GitPackageImporter : EditorWindow
{
    private List<string> gitPackageUrls = new List<string>();
    private string newPackageUrl = "";
    private Vector2 scrollPosition;
    private AddRequest currentRequest;
    private int currentIndex = 0;
    private bool isImporting = false;
    private List<string> importResults = new List<string>();
    private bool isCheckingUPMAvailability = false;
    
    // 全局配置文件路径
    private static string GlobalConfigPath => 
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
                     "Unity", "GitPackageImporter", "packages.json");

    [MenuItem("Tools/Frame/Git Package Importer")]
    public static void ShowWindow()
    {
        GetWindow<GitPackageImporter>("Git Package Importer");
    }

    private void OnGUI()
    {
        GUILayout.Label("批量导入Git包", EditorStyles.boldLabel);
        
        // 添加新的Git包URL
        EditorGUILayout.BeginHorizontal();
        newPackageUrl = EditorGUILayout.TextField("Git URL", newPackageUrl);
        if (GUILayout.Button("添加", GUILayout.Width(80)))
        {
            if (!string.IsNullOrEmpty(newPackageUrl) && !gitPackageUrls.Contains(newPackageUrl))
            {
                gitPackageUrls.Add(newPackageUrl);
                newPackageUrl = "";
                SaveGlobalPackageUrls();
            }
        }
        EditorGUILayout.EndHorizontal();

        // 显示已添加的URL列表
        EditorGUILayout.LabelField("已添加的包:");
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        
        for (int i = 0; i < gitPackageUrls.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(gitPackageUrls[i], EditorStyles.wordWrappedLabel);
            
            if (GUILayout.Button("X", GUILayout.Width(25)))
            {
                gitPackageUrls.RemoveAt(i);
                SaveGlobalPackageUrls();
                break;
            }
            EditorGUILayout.EndHorizontal();
        }
        
        EditorGUILayout.EndScrollView();

        // 导入按钮
        EditorGUI.BeginDisabledGroup(isImporting || isCheckingUPMAvailability);
        if (GUILayout.Button("开始导入", GUILayout.Height(30)))
        {
            StartImportProcess();
        }
        EditorGUI.EndDisabledGroup();

        // 全局包管理按钮
        EditorGUILayout.Space();
        if (GUILayout.Button("在所有项目中使用这些包", GUILayout.Height(25)))
        {
            ShowGlobalUsageInstructions();
        }

        // 显示导入进度
        if (isImporting || isCheckingUPMAvailability)
        {
            EditorGUILayout.Space();
            
            if (isCheckingUPMAvailability)
            {
                EditorGUILayout.LabelField("等待UPM就绪...", EditorStyles.boldLabel);
                EditorGUILayout.Space();
            }
            
            if (currentRequest != null)
            {
                EditorGUILayout.LabelField("导入进度:", EditorStyles.boldLabel);
                
                string packageName = currentRequest.Result != null 
                    ? currentRequest.Result.packageId 
                    : gitPackageUrls[currentIndex];
                
                float progress = currentRequest.IsCompleted ? 1f : 0.5f;
                
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"[{currentIndex + 1}/{gitPackageUrls.Count}] {packageName}", 
                    EditorStyles.miniLabel);
                EditorGUILayout.EndHorizontal();
                
                Rect progressRect = GUILayoutUtility.GetRect(100, 18);
                EditorGUI.ProgressBar(progressRect, progress, 
                    currentRequest.Status.ToString());
            }
            
            // 显示已完成的结果
            if (importResults.Count > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("已完成:", EditorStyles.boldLabel);
                foreach (var result in importResults)
                {
                    EditorGUILayout.LabelField(result, EditorStyles.miniLabel);
                }
            }
            
            Repaint();
        }
    }

    private void StartImportProcess()
    {
        if (gitPackageUrls.Count == 0)
        {
            EditorUtility.DisplayDialog("提示", "请先添加Git包URL", "确定");
            return;
        }

        isImporting = true;
        currentIndex = 0;
        importResults.Clear();
        
        // 先检查UPM是否可用
        isCheckingUPMAvailability = true;
        CheckUPMAvailability();
    }

    private void CheckUPMAvailability()
    {
        var listRequest = Client.List();
        
        EditorApplication.update += WaitForListRequest;
        
        void WaitForListRequest()
        {
            if (!listRequest.IsCompleted)
                return;
                
            EditorApplication.update -= WaitForListRequest;
            isCheckingUPMAvailability = false;
            
            if (listRequest.Status == StatusCode.Failure)
            {
                EditorUtility.DisplayDialog("错误", 
                    $"无法检查UPM状态: {listRequest.Error.message}", "确定");
                isImporting = false;
                return;
            }
            
            // UPM就绪，开始导入
            ImportNextPackage();
        }
    }

    private void ImportNextPackage()
    {
        if (currentIndex >= gitPackageUrls.Count)
        {
            FinalizeImportProcess();
            return;
        }

        string packageUrl = gitPackageUrls[currentIndex];
        currentRequest = Client.Add(packageUrl);
        EditorApplication.update += CheckImportProgress;
    }

    private void CheckImportProgress()
    {
        if (currentRequest == null || !currentRequest.IsCompleted)
            return;

        EditorApplication.update -= CheckImportProgress;
        
        // 记录导入结果
        string packageId = currentRequest.Result != null 
            ? currentRequest.Result.packageId 
            : gitPackageUrls[currentIndex];
        
        string resultMessage = currentRequest.Status == StatusCode.Success 
            ? $"✓ {packageId}" 
            : $"✗ {packageId}: {currentRequest.Error.message}";
        
        importResults.Add(resultMessage);
        
        // 继续导入下一个
        currentIndex++;
        
        // 导入下一个包前再次检查UPM状态
        isCheckingUPMAvailability = true;
        CheckUPMAvailability();
    }

    private void FinalizeImportProcess()
    {
        isImporting = false;
        currentRequest = null;
        
        string logMessage = "导入完成!\n";
        foreach (var result in importResults)
        {
            logMessage += result + "\n";
        }
        
        Debug.Log(logMessage);
        EditorUtility.DisplayDialog("导入完成", logMessage, "确定");
    }

    // 保存包URL到全局配置文件
    private void SaveGlobalPackageUrls()
    {
        try
        {
            // 创建配置目录（如果不存在）
            string configDir = Path.GetDirectoryName(GlobalConfigPath);
            if (!Directory.Exists(configDir))
                Directory.CreateDirectory(configDir);
                
            // 保存URL列表到JSON
            string json = JsonUtility.ToJson(new PackageUrlList { urls = gitPackageUrls });
            File.WriteAllText(GlobalConfigPath, json);
            
            Debug.Log($"已保存包配置到: {GlobalConfigPath}");
        }
        catch (Exception e)
        {
            Debug.LogError($"保存全局配置失败: {e.Message}");
        }
    }

    // 从全局配置文件加载包URL
    private void LoadGlobalPackageUrls()
    {
        try
        {
            if (File.Exists(GlobalConfigPath))
            {
                string json = File.ReadAllText(GlobalConfigPath);
                var urlList = JsonUtility.FromJson<PackageUrlList>(json);
                gitPackageUrls = urlList.urls;
                
                Debug.Log($"已从全局配置加载 {gitPackageUrls.Count} 个包URL");
            }
            else
            {
                Debug.Log("未找到全局配置文件，使用空列表");
                gitPackageUrls = new List<string>();
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"加载全局配置失败: {e.Message}");
            gitPackageUrls = new List<string>();
        }
    }

    // 显示在所有项目中使用这些包的说明
    private void ShowGlobalUsageInstructions()
    {
        string instructions = @"
要在任何Unity项目中使用这些Git包，请按照以下步骤操作：

1. 打开Unity项目
2. 打开 Package Manager (Window > Package Manager)
3. 点击左上角的 + 按钮
4. 选择 ""Add package from git URL...""
5. 从下面的列表中复制Git URL
6. 粘贴到输入框中并点击 ""Add""

可用的Git包URL：
" + string.Join("\n", gitPackageUrls);

        EditorUtility.DisplayDialog("在所有项目中使用这些包", instructions, "确定");
    }

    private void OnEnable()
    {
        LoadGlobalPackageUrls();
    }

    private class PackageUrlList
    {
        public List<string> urls = new List<string>();
    }
}}
