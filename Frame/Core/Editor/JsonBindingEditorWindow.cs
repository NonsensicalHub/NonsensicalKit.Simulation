#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using Frame.Core.Binding;


namespace Frame.Core.Editor
{
public class JsonBindingEditorWindow : EditorWindow
{
    private enum LogLevel { Info, Success, Warning, Error }

    private struct LogEntry
    {
        public LogLevel Level;
        public string Message;
    }

    // ── UI 状态 ────────────────────────────────────────────────
    private GameObject _sourcePrefab;
    private TextAsset _jsonAsset;
    private string _outputDir = "Assets/Prefabs/Auto/UI";
    private string _outputName = "";

    private Vector2 _logScroll;
    private Vector2 _previewScroll;
    private bool _showPreview;
    private string _jsonPreview = "";

    private List<LogEntry> _logs = new List<LogEntry>();

    private GUIStyle _headerStyle, _boxStyle;
    private bool _stylesReady;

    private const string PREF_SOURCE_PREFAB_PATH = "JsonBindingEditor.SourcePrefabPath";
    private const string PREF_JSON_ASSET_PATH = "JsonBindingEditor.JsonAssetPath";
    private const string PREF_OUTPUT_DIR = "JsonBindingEditor.OutputDir";
    private const string PREF_OUTPUT_NAME = "JsonBindingEditor.OutputName";

    [MenuItem("Tools/JSON Binding Editor")]
    public static void Open()
    {
        var w = GetWindow<JsonBindingEditorWindow>("JSON Binding Editor");
        w.minSize = new Vector2(500, 620);
        w.Show();
    }

    private void OnEnable()
    {
        LoadSettings();
    }

    private void OnDisable()
    {
        SaveSettings();
    }

    private void LoadSettings()
    {
        _sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(EditorPrefs.GetString(PREF_SOURCE_PREFAB_PATH, ""));
        _jsonAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(EditorPrefs.GetString(PREF_JSON_ASSET_PATH, ""));
        _outputDir = EditorPrefs.GetString(PREF_OUTPUT_DIR, "Assets/Prefabs/Auto/UI");
        _outputName = EditorPrefs.GetString(PREF_OUTPUT_NAME, "");
    }

    private void SaveSettings()
    {
        if (_sourcePrefab != null)
            EditorPrefs.SetString(PREF_SOURCE_PREFAB_PATH, AssetDatabase.GetAssetPath(_sourcePrefab));
        if (_jsonAsset != null) EditorPrefs.SetString(PREF_JSON_ASSET_PATH, AssetDatabase.GetAssetPath(_jsonAsset));
        EditorPrefs.SetString(PREF_OUTPUT_DIR, _outputDir);
        EditorPrefs.SetString(PREF_OUTPUT_NAME, _outputName);
    }


    // ── 样式 ──────────────────────────────────────────────────
    private void EnsureStyles()
    {
        if (_stylesReady) return;
        _stylesReady = true;

        _headerStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 };
        _boxStyle = new GUIStyle(GUI.skin.box) { padding = new RectOffset(10, 10, 8, 8) };
    }

    // ── GUI ───────────────────────────────────────────────────
    private void OnGUI()
    {
        EnsureStyles();

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("🔗  JSON Binding Editor", _headerStyle);
        EditorGUILayout.Space(2);
        Divider();

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("输入", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(_boxStyle);
        DrawSourcePrefabField();
        DrawJsonField();
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("输出预制体", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(_boxStyle);
        DrawOutputFields();
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(4);
        _showPreview = EditorGUILayout.Foldout(_showPreview, "JSON 预览", true);
        if (_showPreview && !string.IsNullOrEmpty(_jsonPreview))
        {
            _previewScroll = EditorGUILayout.BeginScrollView(_previewScroll, GUILayout.Height(130));
            EditorGUILayout.TextArea(_jsonPreview, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        EditorGUILayout.Space(8);
        DrawButtons();

        EditorGUILayout.Space(6);
        Divider();
        DrawLogs();
    }

    // ── 字段绘制 ──────────────────────────────────────────────
    private void DrawSourcePrefabField()
    {
        EditorGUI.BeginChangeCheck();
        _sourcePrefab = (GameObject)EditorGUILayout.ObjectField(
            new GUIContent("源预制体", "绑定了 BindingToken 组件的模板预制体"),
            _sourcePrefab, typeof(GameObject), false);
        if (EditorGUI.EndChangeCheck() && _sourcePrefab != null)
            AnalyzePrefab(_sourcePrefab);
    }

    private void DrawJsonField()
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUI.BeginChangeCheck();
        _jsonAsset = (TextAsset)EditorGUILayout.ObjectField(
            new GUIContent("JSON 文件", "提供绑定数据的 JSON 文件"),
            _jsonAsset, typeof(TextAsset), false);
        if (EditorGUI.EndChangeCheck() && _jsonAsset != null)
            LoadJsonPreview();
        if (GUILayout.Button("浏览", GUILayout.Width(50)))
            BrowseJson();
        EditorGUILayout.EndHorizontal();
    }

    private void DrawOutputFields()
    {
        EditorGUILayout.BeginHorizontal();
        _outputDir = EditorGUILayout.TextField(new GUIContent("保存目录"), _outputDir);
        if (GUILayout.Button("…", GUILayout.Width(28)))
        {
            string abs = EditorUtility.OpenFolderPanel("选择保存目录", Application.dataPath, "");
            if (!string.IsNullOrEmpty(abs) && abs.StartsWith(Application.dataPath))
                _outputDir = "Assets" + abs.Substring(Application.dataPath.Length);
        }

        EditorGUILayout.EndHorizontal();

        _outputName = EditorGUILayout.TextField(
            new GUIContent("文件名（可选）", "留空则自动命名为 源名_Bound.prefab"), _outputName);
        EditorGUILayout.LabelField(GetOutputPath(), EditorStyles.miniLabel);
    }

    private void DrawButtons()
    {
        bool canBind = _sourcePrefab != null && _jsonAsset != null;

        EditorGUILayout.BeginHorizontal();

        // ── 正向：JSON → Prefab ──
        GUI.enabled = canBind;
        Color prev = GUI.backgroundColor;
        GUI.backgroundColor = canBind ? new Color(0.35f, 0.75f, 0.35f) : Color.gray;
        if (GUILayout.Button("▶  生成绑定预制体", GUILayout.Height(32)))
            RunCreateBoundPrefab();
        GUI.backgroundColor = prev;

        // ── 反向：Prefab → JSON ──
        GUI.enabled = _sourcePrefab != null;
        GUI.backgroundColor = _sourcePrefab != null ? new Color(0.35f, 0.55f, 0.85f) : Color.gray;
        if (GUILayout.Button("◀  导出 JSON", GUILayout.Height(32), GUILayout.Width(100)))
            RunExportJson();
        GUI.backgroundColor = prev;
        GUI.enabled = true;

        if (GUILayout.Button("🔍 分析", GUILayout.Height(32), GUILayout.Width(60)))
        {
            if (_sourcePrefab != null) AnalyzePrefab(_sourcePrefab);
            else AddLog(LogLevel.Warning, "请先选择源预制体");
        }

        if (GUILayout.Button("清除", GUILayout.Height(32), GUILayout.Width(50)))
            _logs.Clear();

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);
        EditorGUILayout.BeginHorizontal();

        GUI.enabled = _sourcePrefab != null;
        prev = GUI.backgroundColor;
        GUI.backgroundColor = _sourcePrefab != null ? new Color(0.55f, 0.4f, 0.85f) : Color.gray;
        if (GUILayout.Button("骨架  由模板生成 JSON 骨架", GUILayout.Height(28)))
            RunGenerateSkeleton();
        GUI.backgroundColor = prev;

        GUI.enabled = _sourcePrefab != null && _jsonAsset != null;
        GUI.backgroundColor = (_sourcePrefab != null && _jsonAsset != null)
            ? new Color(0.9f, 0.65f, 0.2f)
            : Color.gray;
        if (GUILayout.Button("校验  模板↔JSON", GUILayout.Height(28), GUILayout.Width(130)))
            RunValidateAgainstJson();
        GUI.backgroundColor = prev;
        GUI.enabled = true;

        EditorGUILayout.EndHorizontal();
    }

    private void DrawLogs()
    {
        EditorGUILayout.LabelField($"日志 ({_logs.Count})", EditorStyles.boldLabel);
        _logScroll = EditorGUILayout.BeginScrollView(_logScroll, GUILayout.ExpandHeight(true));
        foreach (var e in _logs)
        {
            Color c;
            string pfx;
            switch (e.Level)
            {
                case LogLevel.Success:
                    c = new Color(0.3f, 0.85f, 0.3f);
                    pfx = "✔ ";
                    break;
                case LogLevel.Warning:
                    c = new Color(1f, 0.8f, 0.2f);
                    pfx = "⚠ ";
                    break;
                case LogLevel.Error:
                    c = new Color(1f, 0.35f, 0.35f);
                    pfx = "✘ ";
                    break;
                default:
                    c = GUI.contentColor;
                    pfx = "  ";
                    break;
            }

            Color prev = GUI.contentColor;
            GUI.contentColor = c;
            EditorGUILayout.LabelField(pfx + e.Message, EditorStyles.wordWrappedLabel);
            GUI.contentColor = prev;
        }

        EditorGUILayout.EndScrollView();
    }

    private void Divider()
    {
        Rect r = GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(r, new Color(0.3f, 0.3f, 0.3f, 0.5f));
    }

    // ── 辅助 ──────────────────────────────────────────────────
    private void BrowseJson()
    {
        string path = EditorUtility.OpenFilePanel("选择 JSON 文件", Application.dataPath, "json");
        if (string.IsNullOrEmpty(path)) return;
        if (path.StartsWith(Application.dataPath))
            path = "Assets" + path.Substring(Application.dataPath.Length);
        _jsonAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
        if (_jsonAsset != null) LoadJsonPreview();
    }

    private void LoadJsonPreview()
    {
        _jsonPreview = "";
        try { _jsonPreview = JToken.Parse(_jsonAsset.text).ToString(Formatting.Indented); }
        catch (Exception e) { AddLog(LogLevel.Error, $"JSON 解析失败: {e.Message}"); }
    }

    private string GetOutputPath()
    {
        string dir = string.IsNullOrWhiteSpace(_outputDir) ? "Assets" : _outputDir.TrimEnd('/');
        string name = string.IsNullOrWhiteSpace(_outputName)
            ? (_sourcePrefab != null ? _sourcePrefab.name + "_Bound" : "Bound")
            : _outputName.Replace(".prefab", "");
        return $"{dir}/{name}.prefab";
    }

    private void AnalyzePrefab(GameObject prefab)
    {
        _logs.Clear();
        AddLog(LogLevel.Info, $"分析预制体: {prefab.name}");

        var bos = prefab.GetComponentsInChildren<BindingToken>(true);
        AddLog(LogLevel.Info, $"BindingToken × {bos.Length}");
        foreach (var b in bos)
            AddLog(LogLevel.Info, $"  [{b.GetType().Name}] {NodePath(b.gameObject, prefab)}  Key=\"{b.Key}\"");

        var issues = JsonBindingSchemaHelper.ValidateTemplateKeys(prefab);
        if (issues.Count == 0)
        {
            AddLog(LogLevel.Success, "Key 校验通过（无空 Key / 第一层重复 Key）");
        }
        else
        {
            AddLog(LogLevel.Warning, $"Key 校验发现问题 × {issues.Count}");
            foreach (var issue in issues)
            {
                string path = issue.Binding != null
                    ? NodePath(issue.Binding.gameObject, prefab)
                    : "?";
                string type = issue.Binding != null ? issue.Binding.GetType().Name : "?";
                var level = issue.IsError ? LogLevel.Error : LogLevel.Warning;
                AddLog(level, $"  [{type}] {path}  {issue.Message}");
            }
        }

        Repaint();
    }

    private void RunGenerateSkeleton()
    {
        _logs.Clear();
        if (_sourcePrefab == null)
        {
            AddLog(LogLevel.Warning, "请先选择源预制体");
            return;
        }

        AddLog(LogLevel.Info, $"生成 JSON 骨架: {_sourcePrefab.name}");

        string prefabPath = AssetDatabase.GetAssetPath(_sourcePrefab);
        using (var scope = new PrefabUtility.EditPrefabContentsScope(prefabPath))
        {
            GameObject rootGO = scope.prefabContentsRoot;
            var issues = JsonBindingSchemaHelper.ValidateTemplateKeys(rootGO);
            foreach (var issue in issues)
            {
                string path = issue.Binding != null
                    ? NodePath(issue.Binding.gameObject, rootGO)
                    : "?";
                AddLog(issue.IsError ? LogLevel.Error : LogLevel.Warning,
                    $"[{issue.Binding?.GetType().Name}] {path}  {issue.Message}");
            }

            JObject skeleton = JsonBindingSchemaHelper.BuildSkeleton(rootGO);
            string json = skeleton.ToString(Formatting.Indented);
            _jsonPreview = json;
            _showPreview = true;

            string exportPath = GetSkeletonJsonPath();
            File.WriteAllText(exportPath, json);
            AssetDatabase.Refresh();

            AddLog(LogLevel.Success, $"骨架字段 × {skeleton.Count}，已写入: {exportPath}");
            AddLog(LogLevel.Info, json);

            string assetPath = exportPath.Replace('\\', '/');
            if (assetPath.StartsWith(Application.dataPath.Replace('\\', '/')))
                assetPath = "Assets" + assetPath.Substring(Application.dataPath.Replace('\\', '/').Length);

            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath);
            if (asset != null)
            {
                _jsonAsset = asset;
                EditorGUIUtility.PingObject(asset);
            }
        }

        Repaint();
    }

    private void RunValidateAgainstJson()
    {
        _logs.Clear();
        if (_sourcePrefab == null || _jsonAsset == null)
        {
            AddLog(LogLevel.Warning, "请同时选择源预制体与 JSON 文件");
            return;
        }

        JObject rootJson;
        try
        {
            var token = JToken.Parse(_jsonAsset.text);
            if (token.Type != JTokenType.Object)
            {
                AddLog(LogLevel.Error, "JSON 根节点必须是 Object");
                return;
            }

            rootJson = (JObject)token;
        }
        catch (Exception e)
        {
            AddLog(LogLevel.Error, $"JSON 解析失败: {e.Message}");
            return;
        }

        string prefabPath = AssetDatabase.GetAssetPath(_sourcePrefab);
        using (var scope = new PrefabUtility.EditPrefabContentsScope(prefabPath))
        {
            GameObject rootGO = scope.prefabContentsRoot;
            var issues = JsonBindingSchemaHelper.ValidateTemplateKeys(rootGO);
            foreach (var issue in issues)
            {
                string path = issue.Binding != null
                    ? NodePath(issue.Binding.gameObject, rootGO)
                    : "?";
                AddLog(issue.IsError ? LogLevel.Error : LogLevel.Warning,
                    $"[{issue.Binding?.GetType().Name}] {path}  {issue.Message}");
            }

            var diff = JsonBindingSchemaHelper.DiffAgainstJson(rootGO, rootJson);
            foreach (var key in diff.MissingInJson)
                AddLog(LogLevel.Warning, $"模板有、JSON 缺: \"{key}\"");
            foreach (var key in diff.ExtraInJson)
                AddLog(LogLevel.Info, $"JSON 有、模板无（绑定会忽略）: \"{key}\"");

            bool ok = issues.Count == 0 && diff.MissingInJson.Count == 0;
            if (ok)
                AddLog(LogLevel.Success, "校验通过：第一层 Key 完整匹配");
            else
                AddLog(LogLevel.Warning,
                    $"校验结束：问题×{issues.Count}  缺失×{diff.MissingInJson.Count}  多余×{diff.ExtraInJson.Count}");
        }

        Repaint();
    }

    private string GetSkeletonJsonPath()
    {
        string dir = string.IsNullOrWhiteSpace(_outputDir) ? "Assets" : _outputDir.TrimEnd('/');
        string name = string.IsNullOrWhiteSpace(_outputName)
            ? (_sourcePrefab != null ? _sourcePrefab.name + "_Skeleton" : "Skeleton")
            : _outputName.Replace(".prefab", "").Replace(".json", "") + "_Skeleton";
        string fullDir = Path.Combine(Application.dataPath.Replace("Assets", ""), dir);
        Directory.CreateDirectory(fullDir);
        return $"{fullDir}/{name}.json";
    }

    private void RunCreateBoundPrefab()
    {
        _logs.Clear();

        JObject rootJson;
        try
        {
            var token = JToken.Parse(_jsonAsset.text);
            if (token.Type != JTokenType.Object)
            {
                AddLog(LogLevel.Error, "JSON 根节点必须是 Object");
                return;
            }

            rootJson = (JObject)token;
        }
        catch (Exception e)
        {
            AddLog(LogLevel.Error, $"JSON 解析失败: {e.Message}");
            return;
        }

        string outputPath = GetOutputPath();
        string outputDir = Path.GetDirectoryName(outputPath);

        if (!AssetDatabase.IsValidFolder(outputDir))
        {
            Directory.CreateDirectory(outputDir);
            AssetDatabase.Refresh();
        }

        // ── 关键改动：区分「首次创建」与「原地更新」──────────────
        bool alreadyExists = AssetDatabase.LoadAssetAtPath<GameObject>(outputPath) != null;

        if (!alreadyExists)
        {
            // 首次：复制资产（产生新 GUID，可接受）
            string sourcePath = AssetDatabase.GetAssetPath(_sourcePrefab);
            if (!AssetDatabase.CopyAsset(sourcePath, outputPath))
            {
                AddLog(LogLevel.Error, $"复制预制体失败: {sourcePath} → {outputPath}");
                return;
            }

            AssetDatabase.Refresh();
            AddLog(LogLevel.Info, $"首次创建预制体: {outputPath}");
        }
        else
        {
            // 再次生成：原地同步结构，保留 GUID / 场景引用
            AddLog(LogLevel.Info, $"预制体已存在，原地更新（保留引用）: {outputPath}");
            if (!SyncPrefabStructure(outputPath))
                return;
        }

        int boundCount = 0, missCount = 0;
        int emptyKeyCount = 0, errorCount = 0;

        using (var scope = new PrefabUtility.EditPrefabContentsScope(outputPath))
        {
            GameObject rootGO = scope.prefabContentsRoot;
            DoBind(rootGO, rootJson, ref boundCount, ref missCount, ref emptyKeyCount, ref errorCount);

            int removed = RemoveAllBindingComponents(rootGO);
            AddLog(LogLevel.Info, $"已移除 {removed} 个绑定组件");
            AddLog(LogLevel.Success,
                $"完成：绑定成功×{boundCount}  未命中×{missCount}  空Key×{emptyKeyCount}  异常×{errorCount}  删除组件×{removed}");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        
        var result = AssetDatabase.LoadAssetAtPath<GameObject>(outputPath);
        EditorGUIUtility.PingObject(result);
        Selection.activeObject = result;
        Repaint();
    }

    /// <summary>
    /// 将源预制体的 GameObject 层级结构同步到目标预制体中，
    /// 同时保留目标预制体的 GUID（即保留所有场景 / Prefab 引用）。
    /// </summary>
    private bool SyncPrefabStructure(string outputPath)
    {
        // 1. 把源预制体实例化为一个临时 GameObject（仅在内存中）
        string sourcePath = AssetDatabase.GetAssetPath(_sourcePrefab);
        GameObject sourceInstance = PrefabUtility.LoadPrefabContents(sourcePath);
        if (sourceInstance == null)
        {
            AddLog(LogLevel.Error, "无法加载源预制体内容");
            return false;
        }

        try
        {
            using (var scope = new PrefabUtility.EditPrefabContentsScope(outputPath))
            {
                GameObject rootGO = scope.prefabContentsRoot;

                // 2. 清空目标根节点下的所有子物体
                //    （反向遍历，避免索引变化）
                for (int i = rootGO.transform.childCount - 1; i >= 0; i--)
                    DestroyImmediate(rootGO.transform.GetChild(i).gameObject);

                // 3. 把源实例的所有子物体移入目标根节点
                //    用 while 而非 foreach，因为 childCount 会动态变化
                while (sourceInstance.transform.childCount > 0)
                {
                    Transform child = sourceInstance.transform.GetChild(0);
                    child.SetParent(rootGO.transform, false);
                }

                // 4. 同步根节点自身的组件（先移除旧的，再从源复制）
                SyncComponents(sourceInstance, rootGO);
            }
        }
        finally
        {
            // 必须释放 LoadPrefabContents 加载的内容
            PrefabUtility.UnloadPrefabContents(sourceInstance);
        }

        AddLog(LogLevel.Info, "结构同步完成");
        return true;
    }

    /// <summary>
    /// 将 src 上的所有组件同步到 dst（跳过 Transform）。
    /// 策略：移除 dst 上源没有的组件，然后复制 / 更新已有组件。
    /// </summary>
    private void SyncComponents(GameObject src, GameObject dst)
    {
        // 移除 dst 上多余的组件（源上不存在的）
        var dstComps = new List<UnityEngine.Component>(dst.GetComponents<UnityEngine.Component>());
        var srcTypes = new HashSet<Type>();
        foreach (var c in src.GetComponents<UnityEngine.Component>())
            if (c != null)
                srcTypes.Add(c.GetType());

        foreach (var c in dstComps)
        {
            if (c == null || c is Transform) continue;
            if (!srcTypes.Contains(c.GetType()))
            {
                DestroyImmediate(c, true);
                AddLog(LogLevel.Info, $"  移除旧组件: {c.GetType().Name}");
            }
        }

        // 复制源组件到目标（UnityEditorInternal 提供的内部 API）
        foreach (var srcComp in src.GetComponents<UnityEngine.Component>())
        {
            if (srcComp == null || srcComp is Transform) continue;

            var dstComp = dst.GetComponent(srcComp.GetType())
                          ?? dst.AddComponent(srcComp.GetType());

            // EditorUtility.CopySerialized 会完整复制序列化字段
            EditorUtility.CopySerialized(srcComp, dstComp);
        }
    }

    /// <summary>
    /// 在 rootGO 下查找第一层 BindingToken，按 key 从 rootJson 取值并绑定。
    /// 每个 BindingToken 子类自己负责内部递归（Array / Pages），此处不再 if-else 分支。
    /// </summary>
    private void DoBind(GameObject rootGO, JObject rootJson, ref int boundCount, ref int missCount, ref int emptyKeyCount, ref int errorCount)
    {
        var report = CreateBindingReport(rootGO);

        JsonBindingHelper.BindFirstLevel(rootGO, rootJson, report);

        boundCount = report.BoundCount;
        missCount = report.MissingCount;
        emptyKeyCount = report.EmptyKeyCount;
        errorCount = report.ErrorCount;
    }

    // ── 反向：Prefab → JSON ────────────────────────────────────
    private void RunExportJson()
    {
        _logs.Clear();
        AddLog(LogLevel.Info, $"开始从预制体导出 JSON: {_sourcePrefab.name}");

        // 在临时场景实例中操作，避免污染源预制体
        string prefabPath = AssetDatabase.GetAssetPath(_sourcePrefab);

        using (var scope = new PrefabUtility.EditPrefabContentsScope(prefabPath))
        {
            GameObject rootGO = scope.prefabContentsRoot;
            var report = CreateBindingReport(rootGO);
            JObject result = JsonBindingHelper.CollectFirstChildren(rootGO, report);

            string json = result.ToString(Formatting.Indented);
            AddLog(LogLevel.Info, $"导出统计：警告×{report.WarningCount}  异常×{report.ErrorCount}");
            AddLog(LogLevel.Success, "导出结果：");
            AddLog(LogLevel.Info, json);

            // 可选：写到文件
            string exportPath = GetExportJsonPath();
            File.WriteAllText(exportPath, json);
            AssetDatabase.Refresh();
            AddLog(LogLevel.Success, $"已写入: {exportPath}");
        }

        Repaint();
    }

    private JsonBindingReport CreateBindingReport(GameObject rootGO)
    {
        return new JsonBindingReport
        {
            OnLog = (level, binding, message) =>
            {
                string prefix = "";
                if (binding != null)
                    prefix = $"[{binding.GetType().Name}] {NodePath(binding.gameObject, rootGO)} ";

                // 成功只计入 BoundCount，不逐条输出
                if (level == JsonBindingReportLevel.Success)
                    return;

                switch (level)
                {
                    case JsonBindingReportLevel.Warning:
                        AddLog(LogLevel.Warning, prefix + message);
                        break;
                    case JsonBindingReportLevel.Error:
                        AddLog(LogLevel.Error, prefix + message);
                        break;
                    default:
                        AddLog(LogLevel.Info, prefix + message);
                        break;
                }
            }
        };
    }

    private string GetExportJsonPath()
    {
        string dir = string.IsNullOrWhiteSpace(_outputDir) ? "Assets" : _outputDir.TrimEnd('/');
        string name = string.IsNullOrWhiteSpace(_outputName)
            ? (_sourcePrefab != null ? _sourcePrefab.name + "_Exported" : "Exported")
            : _outputName.Replace(".prefab", "");
        string fullDir = Path.Combine(Application.dataPath.Replace("Assets", ""), dir);
        Directory.CreateDirectory(fullDir);
        return $"{fullDir}/{name}.json";
    }

    // ── 移除绑定组件 ──────────────────────────────────────────
    private static int RemoveAllBindingComponents(GameObject rootGO)
    {
        int count = 0;
        foreach (var c in rootGO.GetComponentsInChildren<BindingToken>(true))
        {
            DestroyImmediate(c, true);
            count++;
        }

        return count;
    }

    // ── 工具 ──────────────────────────────────────────────────
    private static string NodePath(GameObject go, GameObject root)
    {
        var parts = new List<string>();
        Transform t = go.transform;
        while (t != null && t.gameObject != root)
        {
            parts.Insert(0, t.name);
            t = t.parent;
        }

        parts.Insert(0, root.name);
        return string.Join("/", parts);
    }

    private void AddLog(LogLevel level, string msg) =>
        _logs.Add(new LogEntry { Level = level, Message = msg });
}
}
#endif
