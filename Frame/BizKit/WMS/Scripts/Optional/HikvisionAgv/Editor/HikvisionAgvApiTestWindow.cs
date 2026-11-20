using Frame.BizKit.WMS.Hikvision;
using Frame.Core.Config;
using HikvisionAgv;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NonsensicalKit.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace HikvisionAgv.Editor
{
    /// <summary>
    /// 海康 RCS 接口测试，以及按真实链路手动注入孪生数据。
    /// 注入走 IOCC.Publish(EventKey, …) → HikvisionDataBridge → DataHub / MotionPartUpdate。
    /// </summary>
    public sealed class HikvisionAgvApiTestWindow : EditorWindow
    {
        private const string PrefPrefix = "HikvisionAgvApiTest.";
        private const string UrlBaseConfigPath = "Assets/Resources/CusConfigs/URLBaseConfig.asset";
        private const string SiteConfigPath = "Assets/Resources/CusConfigs/HikvisionAgvSiteConfig.asset";
        private const string ServerKey = "海康接口";
        private const string PathAgvStatus = "/rcms-dps/rest/queryAgvStatus";
        private const string PathTaskStatus = "/rcms/services/rest/hikRpcService/queryTaskStatus";
        private const int MaxLogCount = 80;

        private static readonly JsonSerializerSettings JsonSetting = new()
        {
            NullValueHandling = NullValueHandling.Ignore
        };

        private enum Tab
        {
            Api = 0,
            Twin = 1,
            Record = 2
        }

        private enum ApiKind
        {
            AgvStatus = 0,
            TaskStatus = 1
        }

        private Tab _tab;
        private ApiKind _apiKind;
        private ApiKind _twinKind;

        private string _baseUrl = "192.168.10.216:8182";
        private string _clientCode = "";
        private string _tokenCode = "";
        private bool _ensureHttp = true;
        private string _mapShortName = "AA";
        private string _agvCode = "12497";

        private string _requestJson = "";
        private string _twinJson = "";
        private string _lastResponseJson = "";

        private readonly List<LogEntry> _logs = new();
        private LogEntry _selected;
        private Vector2 _windowScroll;
        private Vector2 _logScroll;
        private Vector2 _requestScroll;
        private Vector2 _twinScroll;
        private Vector2 _detailScroll;

        private bool _busy;
        private string _statusText = "就绪";
        private GUIStyle _okStyle;
        private GUIStyle _failStyle;
        private GUIStyle _mutedStyle;
        private GUIStyle _headerStyle;

        private float _recordInterval = 0.5f;
        private bool _recordAllMaps = true;
        private string _recordDir = "";
        private bool _recording;
        private StreamWriter _recordWriter;
        private string _recordFilePath = "";
        private bool _recordRequesting;
        private string[] _recordMaps = Array.Empty<string>();
        private int _recordMapIndex;
        private double _recordNextTime;
        private int _recordOk;
        private int _recordFail;
        private int _recordFrames;

        private bool _playing;
        private bool _playbackLoop;
        private float _playbackSpeed = 1f;
        private string _playbackPath = "";
        private List<HikvisionAgvStatusTape.Frame> _playbackFrames;
        private int _playbackIndex;
        private double _playbackStartRealtime;
        private long _playbackOriginUnixMs;

        [MenuItem("Tools/海康AGV/接口与孪生测试")]
        public static void Open()
        {
            var window = GetWindow<HikvisionAgvApiTestWindow>("海康接口测试");
            window.minSize = new Vector2(640, 520);
            window.Show();
        }

        private void OnEnable()
        {
            LoadPrefs();
            BindSiteFromAsset();
            if (string.IsNullOrEmpty(_recordDir))
                _recordDir = DefaultRecordDir();
            if (string.IsNullOrEmpty(_requestJson))
                FillRequestTemplate();
            if (string.IsNullOrEmpty(_twinJson))
                FillTwinTemplate();
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            StopRecording(false);
            StopPlayback(false);
            SavePrefs();
        }

        private void OnEditorUpdate()
        {
            TickRecord();
            TickPlayback();
        }

        private void OnGUI()
        {
            EnsureStyles();
            DrawToolbar();
            _windowScroll = EditorGUILayout.BeginScrollView(_windowScroll);
            DrawSettings();
            if (_tab == Tab.Api)
                DrawApiPanel();
            else if (_tab == Tab.Twin)
                DrawTwinPanel();
            else
                DrawRecordPanel();
            DrawLogs();
            DrawDetail();
            EditorGUILayout.EndScrollView();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Toggle(_tab == Tab.Api, "接口测试", EditorStyles.toolbarButton, GUILayout.Width(80)))
                _tab = Tab.Api;
            if (GUILayout.Toggle(_tab == Tab.Twin, "驱动孪生", EditorStyles.toolbarButton, GUILayout.Width(80)))
                _tab = Tab.Twin;
            if (GUILayout.Toggle(_tab == Tab.Record, "录制回放", EditorStyles.toolbarButton, GUILayout.Width(80)))
                _tab = Tab.Record;

            if (GUILayout.Button("读取配置", EditorStyles.toolbarButton, GUILayout.Width(80)))
                TryLoadConfigs(true);

            GUILayout.FlexibleSpace();
            var playing = Application.isPlaying;
            var old = GUI.color;
            GUI.color = playing ? new Color(0.35f, 0.85f, 0.45f) : new Color(0.95f, 0.75f, 0.2f);
            string playHint = _tab == Tab.Record
                ? (playing ? "Play 中（仅回放需要）" : "未 Play（录制不需要）")
                : (playing ? "Play 中" : "未 Play（注入需进 Play）");
            GUILayout.Label(playHint, EditorStyles.miniLabel, GUILayout.Width(170));
            GUI.color = old;
            GUILayout.Label(_busy ? "请求中…" : _statusText, EditorStyles.miniLabel, GUILayout.MaxWidth(260));
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSettings()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            DrawInlineLabel("BaseUrl", 52);
            _baseUrl = EditorGUILayout.TextField(_baseUrl);
            _ensureHttp = GUILayout.Toggle(_ensureHttp, "补全 http://", GUILayout.Width(96));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            DrawInlineLabel("client", 52);
            _clientCode = EditorGUILayout.TextField(_clientCode);
            DrawInlineLabel("token", 40);
            _tokenCode = EditorGUILayout.TextField(_tokenCode);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            DrawInlineLabel("地图", 52);
            DrawMapPopup();
            DrawInlineLabel("车号", 32);
            DrawRobotPopup();
            if (GUILayout.Button("按现场配置刷新模板", GUILayout.Width(140)))
            {
                BindSiteFromAsset();
                FillRequestTemplate();
                FillTwinTemplate();
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField(
                "queryAgvStatus 一层一次；queryTaskStatus 必须带车号。注入走 Bridge 同一条 EventKey。",
                _mutedStyle);
            EditorGUILayout.EndVertical();
        }

        private void DrawApiPanel()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            int apiIdx = GUILayout.Toolbar((int)_apiKind, new[] { "queryAgvStatus", "queryTaskStatus" });
            if (apiIdx != (int)_apiKind)
            {
                _apiKind = (ApiKind)apiIdx;
                FillRequestTemplate();
            }

            GUILayout.Label(BuildUrl(_apiKind) ?? "", _mutedStyle);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("填入请求模板", GUILayout.Width(108)))
                FillRequestTemplate();
            if (GUILayout.Button("刷新 reqCode", GUILayout.Width(96)))
                RefreshReqCode(ref _requestJson);
            GUI.enabled = !_busy && !_recording && !string.IsNullOrWhiteSpace(_baseUrl);
            if (GUILayout.Button(_busy ? "请求中…" : "发送 POST", GUILayout.Width(96)))
                SendApi();
            GUI.enabled = true;
            GUI.enabled = CanInject(_lastResponseJson, _apiKind);
            if (GUILayout.Button("注入本次响应", GUILayout.Width(108)))
                InjectJson(_lastResponseJson, _apiKind, "接口响应");
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            _requestScroll = EditorGUILayout.BeginScrollView(_requestScroll, GUILayout.MinHeight(140), GUILayout.MaxHeight(220));
            _requestJson = EditorGUILayout.TextArea(_requestJson, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawTwinPanel()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("编辑下面的 RCS 应答模板，再注入。格式与接口 data[] 一致。", _mutedStyle);
            EditorGUILayout.BeginHorizontal();
            int twinIdx = GUILayout.Toolbar((int)_twinKind, new[] { "AGV 状态", "任务状态" });
            if (twinIdx != (int)_twinKind)
            {
                _twinKind = (ApiKind)twinIdx;
                FillTwinTemplate();
            }
            if (GUILayout.Button("填入模板", GUILayout.Width(84)))
                FillTwinTemplate();
            GUI.enabled = CanInject(_twinJson, _twinKind);
            if (GUILayout.Button("注入运行时", GUILayout.Width(96)))
                InjectJson(_twinJson, _twinKind, "手动模板");
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            if (!Application.isPlaying)
                EditorGUILayout.HelpBox("需要 Play，且场景里有已启用的 HikvisionDataBridge。", MessageType.Warning);
            else if (!HasBridge())
                EditorGUILayout.HelpBox("当前场景没有 HikvisionDataBridge，发布后无人接收。", MessageType.Warning);

            _twinScroll = EditorGUILayout.BeginScrollView(_twinScroll, GUILayout.MinHeight(180), GUILayout.MaxHeight(280));
            _twinJson = EditorGUILayout.TextArea(_twinJson, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawRecordPanel()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("监听录制", _headerStyle);
            EditorGUILayout.LabelField(
                "编辑器直接 POST RCS，不需要 Play。每层一次 queryAgvStatus，记下该层全部车。上一拍未结束则跳过。",
                _mutedStyle);

            EditorGUILayout.BeginHorizontal();
            DrawInlineLabel("周期(秒)", 56);
            GUI.enabled = !_recording;
            _recordInterval = Mathf.Max(0.05f, EditorGUILayout.FloatField(_recordInterval, GUILayout.Width(60)));
            _recordAllMaps = GUILayout.Toggle(_recordAllMaps, "全部楼层", GUILayout.Width(80));
            if (!_recordAllMaps)
            {
                DrawInlineLabel("仅", 16);
                DrawMapPopup();
            }

            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            DrawInlineLabel("保存目录", 56);
            GUI.enabled = !_recording;
            _recordDir = EditorGUILayout.TextField(_recordDir);
            if (GUILayout.Button("浏览", GUILayout.Width(52)))
            {
                string picked = EditorUtility.OpenFolderPanel("AGV 状态录制目录", _recordDir, "");
                if (!string.IsNullOrEmpty(picked))
                    _recordDir = picked;
            }

            EditorGUILayout.EndHorizontal();
            GUI.enabled = true;

            EditorGUILayout.BeginHorizontal();
            GUI.enabled = !_recording && !_playing;
            if (GUILayout.Button("开始录制", GUILayout.Width(88)))
                StartRecording();
            GUI.enabled = _recording;
            if (GUILayout.Button("停止录制", GUILayout.Width(88)))
                StopRecording(true);
            GUI.enabled = true;
            GUILayout.Label(
                _recording
                    ? $"录制中  帧 {_recordFrames}  成功 {_recordOk}  失败 {_recordFail}"
                    : (string.IsNullOrEmpty(_recordFilePath) ? "未录制" : "上次: " + Path.GetFileName(_recordFilePath)),
                _mutedStyle);
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_recordFilePath))
                EditorGUILayout.LabelField(_recordFilePath, _mutedStyle);

            EditorGUILayout.Space(8);
            GUILayout.Label("回放", _headerStyle);
            EditorGUILayout.LabelField(
                "按时间戳顺序 IOCC.Publish(StatusRaw, map, data[])，与运行时 StatusRequest 同一条链路。",
                _mutedStyle);

            EditorGUILayout.BeginHorizontal();
            DrawInlineLabel("记录文件", 56);
            _playbackPath = EditorGUILayout.TextField(_playbackPath);
            if (GUILayout.Button("浏览", GUILayout.Width(52)))
            {
                string picked = EditorUtility.OpenFilePanel("打开 AGV 录制", _recordDir, "jsonl;json");
                if (!string.IsNullOrEmpty(picked))
                    _playbackPath = picked;
            }

            if (GUILayout.Button("加载", GUILayout.Width(52)))
                LoadPlaybackFile();
            EditorGUILayout.EndHorizontal();

            int total = _playbackFrames?.Count ?? 0;
            EditorGUILayout.BeginHorizontal();
            DrawInlineLabel("速度", 56);
            _playbackSpeed = EditorGUILayout.Slider(_playbackSpeed, 0.1f, 8f);
            _playbackLoop = GUILayout.Toggle(_playbackLoop, "循环", GUILayout.Width(48));
            EditorGUILayout.EndHorizontal();

            if (total > 0)
            {
                var first = _playbackFrames[0];
                var last = _playbackFrames[total - 1];
                double spanSec = Math.Max(0, last.unixMs - first.unixMs) / 1000.0;
                int displayIndex = Mathf.Clamp(_playbackIndex, 0, total);
                GUILayout.Label(
                    $"已加载 {total} 帧  时长 {spanSec:0.0}s  进度 {displayIndex}/{total}",
                    _mutedStyle);

                EditorGUILayout.BeginHorizontal();
                DrawInlineLabel("起点", 56);
                GUI.enabled = !_recording && total > 0;
                EditorGUI.BeginChangeCheck();
                int maxIdx = Mathf.Max(0, total - 1);
                int seek = Mathf.Clamp(_playbackIndex, 0, maxIdx);
                seek = EditorGUILayout.IntSlider(seek, 0, maxIdx);
                if (EditorGUI.EndChangeCheck())
                    SeekPlaybackTo(seek, true);
                GUI.enabled = true;

                double atSec = Math.Max(0, _playbackFrames[Mathf.Clamp(seek, 0, maxIdx)].unixMs - first.unixMs) / 1000.0;
                GUILayout.Label($"{atSec:0.0}s", _mutedStyle, GUILayout.Width(52));
                EditorGUILayout.EndHorizontal();
            }

            if (!Application.isPlaying)
                EditorGUILayout.HelpBox("回放需要先 Play，且场景有 HikvisionDataBridge。", MessageType.Warning);
            else if (!HasBridge())
                EditorGUILayout.HelpBox("当前场景没有 HikvisionDataBridge。", MessageType.Warning);
            else
                EditorGUILayout.HelpBox("回放时请关掉 HikvisionAgvStatusRequest，避免真车轮询覆盖回放数据。", MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            GUI.enabled = !_recording && total > 0 && !_playing;
            string playLabel = _playbackIndex > 0 ? "继续回放" : "开始回放";
            if (GUILayout.Button(playLabel, GUILayout.Width(88)))
                StartPlayback();
            GUI.enabled = _playing;
            if (GUILayout.Button("暂停", GUILayout.Width(64)))
                PausePlayback();
            GUI.enabled = _playing || (_playbackFrames != null && _playbackIndex > 0);
            if (GUILayout.Button("停止回放", GUILayout.Width(88)))
                StopPlayback(true);
            GUI.enabled = !_recording && !_playing && total > 0;
            if (GUILayout.Button("单帧步进", GUILayout.Width(88)))
                StepPlayback();
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawLogs()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.MinHeight(140));
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label($"记录 {_logs.Count}", _headerStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("清空", GUILayout.Width(52)))
            {
                _logs.Clear();
                _selected = null;
            }

            EditorGUILayout.EndHorizontal();

            DrawLogHeader();
            _logScroll = EditorGUILayout.BeginScrollView(_logScroll, GUILayout.MinHeight(100), GUILayout.MaxHeight(180));
            for (int i = _logs.Count - 1; i >= 0; i--)
                DrawLogRow(_logs[i]);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawLogHeader()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("时间", EditorStyles.miniLabel, GUILayout.Width(64));
            GUILayout.Label("来源", EditorStyles.miniLabel, GUILayout.Width(88));
            GUILayout.Label("摘要", EditorStyles.miniLabel, GUILayout.MinWidth(80));
            GUILayout.Label("结果", EditorStyles.miniLabel, GUILayout.Width(72));
            EditorGUILayout.EndHorizontal();
        }

        private void DrawLogRow(LogEntry entry)
        {
            Rect row = EditorGUILayout.BeginHorizontal(GUILayout.Height(20));
            if (ReferenceEquals(_selected, entry) && Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(row, new Color(0.24f, 0.42f, 0.72f, 0.35f));

            if (Event.current.type == EventType.MouseDown && row.Contains(Event.current.mousePosition))
            {
                _selected = entry;
                Event.current.Use();
                Repaint();
            }

            GUILayout.Label($"{entry.Time:HH:mm:ss}", EditorStyles.miniLabel, GUILayout.Width(64));
            GUILayout.Label(entry.Source ?? "", EditorStyles.miniLabel, GUILayout.Width(88));
            GUILayout.Label(entry.Summary ?? "", EditorStyles.miniLabel, GUILayout.MinWidth(80));
            GUILayout.Label(entry.Ok ? "成功" : "失败", entry.Ok ? _okStyle : _failStyle, GUILayout.Width(72));
            EditorGUILayout.EndHorizontal();
        }

        private void DrawDetail()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("详情", _headerStyle);
            if (_selected == null)
            {
                GUILayout.Label("点一条记录查看请求/响应。", _mutedStyle);
                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUILayout.LabelField("结果", string.IsNullOrEmpty(_selected.Warning) ? _selected.Summary : _selected.Warning);
            _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll, GUILayout.Height(120));
            EditorGUILayout.TextArea(_selected.Body ?? "", GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawMapPopup()
        {
            string[] maps = HikvisionAgvSite.MapShortNames;
            if (maps == null || maps.Length == 0)
            {
                _mapShortName = EditorGUILayout.TextField(_mapShortName, GUILayout.MinWidth(60));
                return;
            }

            int idx = Mathf.Max(0, Array.FindIndex(maps, m => string.Equals(m, _mapShortName, StringComparison.OrdinalIgnoreCase)));
            int next = EditorGUILayout.Popup(idx, maps, GUILayout.MinWidth(60));
            if (next != idx || string.IsNullOrEmpty(_mapShortName))
            {
                _mapShortName = maps[next];
                string[] robots = HikvisionAgvSite.RobotsOnMap(_mapShortName);
                if (robots.Length > 0)
                    _agvCode = robots[0];
            }
        }

        private void DrawRobotPopup()
        {
            string[] robots = HikvisionAgvSite.RobotsOnMap(_mapShortName);
            if (robots == null || robots.Length == 0)
                robots = HikvisionAgvSite.RobotCodes;
            if (robots == null || robots.Length == 0)
            {
                _agvCode = EditorGUILayout.TextField(_agvCode, GUILayout.MinWidth(80));
                return;
            }

            int idx = Mathf.Max(0, Array.FindIndex(robots, r => r == _agvCode));
            int next = EditorGUILayout.Popup(idx, robots, GUILayout.MinWidth(80));
            _agvCode = robots[next];
        }

        private static void DrawInlineLabel(string text, float width)
        {
            GUILayout.Label(text, EditorStyles.miniLabel, GUILayout.Width(width));
        }

        private void FillRequestTemplate()
        {
            BindSiteFromAsset();
            if (_apiKind == ApiKind.AgvStatus)
            {
                _requestJson = Pretty(new Dictionary<string, string>
                {
                    ["reqCode"] = NewReqCode(),
                    ["reqTime"] = "",
                    ["clientCode"] = _clientCode ?? "",
                    ["tokenCode"] = _tokenCode ?? "",
                    ["mapShortName"] = string.IsNullOrEmpty(_mapShortName) ? "AA" : _mapShortName
                });
            }
            else
            {
                _requestJson = Pretty(new Dictionary<string, object>
                {
                    ["reqCode"] = NewReqCode(),
                    ["reqTime"] = "",
                    ["clientCode"] = _clientCode ?? "",
                    ["tokenCode"] = _tokenCode ?? "",
                    ["agvCode"] = string.IsNullOrEmpty(_agvCode) ? "12497" : _agvCode,
                    ["taskCodes"] = Array.Empty<string>()
                });
            }
        }

        private void FillTwinTemplate()
        {
            BindSiteFromAsset();
            if (_twinKind == ApiKind.AgvStatus)
                _twinJson = BuildStatusResponseTemplate(_mapShortName);
            else
                _twinJson = BuildTaskResponseTemplate(_agvCode);
        }

        private string BuildStatusResponseTemplate(string map)
        {
            if (string.IsNullOrEmpty(map))
                map = "AA";

            string[] codes = HikvisionAgvSite.RobotsOnMap(map);
            if (codes == null || codes.Length == 0)
                codes = new[] { string.IsNullOrEmpty(_agvCode) ? "12497" : _agvCode };

            var data = new List<HikAgvStatusDto>(codes.Length);
            for (int i = 0; i < codes.Length; i++)
            {
                string code = codes[i];
                int site = Mathf.Max(0, HikvisionAgvSite.SiteIndexOf(code));
                data.Add(new HikAgvStatusDto
                {
                    battery = "88",
                    exclType = "0",
                    mapCode = map,
                    podCode = "0",
                    podDir = "0",
                    posX = (20000 + site * 500).ToString(),
                    posY = (20000 + site * 300).ToString(),
                    robotCode = code,
                    robotDir = "0",
                    robotIp = "",
                    speed = "0.5",
                    status = "4",
                    stop = "0"
                });
            }

            return Pretty(new HikApiResponse<HikAgvStatusDto>
            {
                code = 0,
                message = "成功",
                reqCode = "TEST",
                data = data
            });
        }

        private static string BuildTaskResponseTemplate(string agvCode)
        {
            if (string.IsNullOrEmpty(agvCode))
                agvCode = "12497";

            return Pretty(new HikApiResponse<HikTaskStatusDto>
            {
                code = 0,
                message = "成功",
                reqCode = "TEST",
                data = new List<HikTaskStatusDto>
                {
                    new HikTaskStatusDto
                    {
                        agvCode = agvCode,
                        taskCode = "SIM_TASK_" + agvCode,
                        taskStatus = "2",
                        taskTyp = "A01"
                    }
                }
            });
        }

        private void SendApi()
        {
            string url = BuildUrl(_apiKind);
            if (string.IsNullOrEmpty(url))
            {
                _statusText = "BaseUrl 为空";
                return;
            }

            string body = _requestJson;
            RefreshReqCode(ref body);
            _requestJson = body;

            _busy = true;
            _statusText = "请求中…";
            PostJson(url, body, (ok, text, error) =>
            {
                _busy = false;
                string pretty = TryPretty(text);
                _lastResponseJson = pretty;
                string summary;
                string warning = null;
                if (!ok)
                {
                    summary = error;
                    warning = error + (string.IsNullOrEmpty(text) ? "" : "\n" + text);
                    _statusText = "请求失败";
                }
                else
                {
                    summary = DescribeResponse(_apiKind, pretty, out warning);
                    _statusText = warning == null ? "请求完成" : warning;
                }

                AddLog(ok && warning == null, KindLabel(_apiKind), summary,
                    "POST " + url + "\n\n" + body + "\n\n" + pretty, warning);
                Repaint();
            });
        }

        private void PostJson(string url, string body, Action<bool, string, string> done)
        {
            var req = new UnityWebRequest(url, "POST");
            byte[] raw = Encoding.UTF8.GetBytes(body ?? "");
            req.uploadHandler = new UploadHandlerRaw(raw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = 8;
            var op = req.SendWebRequest();
            op.completed += _ =>
            {
                bool ok = req.result == UnityWebRequest.Result.Success;
                string text = req.downloadHandler != null ? req.downloadHandler.text : "";
                string error = ok ? null : req.error;
                req.Dispose();
                done?.Invoke(ok, text, error);
            };
        }

        private void InjectJson(string json, ApiKind kind, string source)
        {
            if (!Application.isPlaying)
            {
                EditorUtility.DisplayDialog("海康接口测试", "注入需要先进入 Play，且场景挂有 HikvisionDataBridge。", "确定");
                return;
            }

            if (!HasBridge())
            {
                EditorUtility.DisplayDialog("海康接口测试", "场景里没有 HikvisionDataBridge，无法走真实链路。", "确定");
                return;
            }

            try
            {
                if (kind == ApiKind.AgvStatus)
                {
                    if (!TryParseStatus(json, out string map, out var list, out string warn))
                    {
                        AddLog(false, "注入状态", warn, json, warn);
                        return;
                    }

                    if (string.IsNullOrEmpty(map))
                        map = _mapShortName;
                    IOCC.Publish(HikvisionAgvEvents.StatusRaw, map, list);
                    string summary = $"{source} → {map}  {list.Count} 台";
                    AddLog(true, "注入状态", summary, json, null);
                    _statusText = summary;
                }
                else
                {
                    if (!TryParseTask(json, out string agv, out var list, out string warn))
                    {
                        AddLog(false, "注入任务", warn, json, warn);
                        return;
                    }

                    if (string.IsNullOrEmpty(agv))
                        agv = _agvCode;
                    IOCC.Publish(HikvisionAgvEvents.TaskRaw, agv, list);
                    string summary = $"{source} → {agv}  {list.Count} 条";
                    AddLog(true, "注入任务", summary, json, null);
                    _statusText = summary;
                }
            }
            catch (Exception e)
            {
                AddLog(false, "注入", e.Message, json, e.Message);
            }
        }

        private static bool TryParseStatus(string json, out string map, out List<HikAgvStatusDto> list, out string warning)
        {
            map = null;
            list = null;
            warning = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                warning = "JSON 为空";
                return false;
            }

            try
            {
                var envelope = JsonConvert.DeserializeObject<HikApiResponse<HikAgvStatusDto>>(json, JsonSetting);
                if (envelope != null)
                {
                    if (envelope.code != 0)
                    {
                        warning = $"code={envelope.code} {envelope.message}";
                        return false;
                    }

                    list = envelope.data;
                }

                if (list == null)
                    list = JsonConvert.DeserializeObject<List<HikAgvStatusDto>>(json, JsonSetting);

                if (list == null)
                {
                    var one = JsonConvert.DeserializeObject<HikAgvStatusDto>(json, JsonSetting);
                    if (one != null && !string.IsNullOrEmpty(one.robotCode))
                        list = new List<HikAgvStatusDto> { one };
                }

                if (list == null || list.Count == 0)
                {
                    warning = "无法解析为 queryAgvStatus 的 data[]";
                    return false;
                }

                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i] != null && !string.IsNullOrEmpty(list[i].mapCode))
                    {
                        map = list[i].mapCode;
                        break;
                    }
                }

                return true;
            }
            catch (Exception e)
            {
                warning = e.Message;
                return false;
            }
        }

        private static bool TryParseTask(string json, out string agv, out List<HikTaskStatusDto> list, out string warning)
        {
            agv = null;
            list = null;
            warning = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                warning = "JSON 为空";
                return false;
            }

            try
            {
                var envelope = JsonConvert.DeserializeObject<HikApiResponse<HikTaskStatusDto>>(json, JsonSetting);
                if (envelope != null)
                {
                    if (envelope.code != 0)
                    {
                        warning = $"code={envelope.code} {envelope.message}";
                        return false;
                    }

                    list = envelope.data;
                }

                if (list == null)
                    list = JsonConvert.DeserializeObject<List<HikTaskStatusDto>>(json, JsonSetting);

                if (list == null)
                {
                    var one = JsonConvert.DeserializeObject<HikTaskStatusDto>(json, JsonSetting);
                    if (one != null && (!string.IsNullOrEmpty(one.agvCode) || !string.IsNullOrEmpty(one.taskCode)))
                        list = new List<HikTaskStatusDto> { one };
                }

                if (list == null || list.Count == 0)
                {
                    warning = "无法解析为 queryTaskStatus 的 data[]";
                    return false;
                }

                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i] != null && !string.IsNullOrEmpty(list[i].agvCode))
                    {
                        agv = list[i].agvCode;
                        break;
                    }
                }

                return true;
            }
            catch (Exception e)
            {
                warning = e.Message;
                return false;
            }
        }

        private static bool CanInject(string json, ApiKind kind)
        {
            if (string.IsNullOrWhiteSpace(json))
                return false;
            return kind == ApiKind.AgvStatus
                ? TryParseStatus(json, out _, out _, out _)
                : TryParseTask(json, out _, out _, out _);
        }

        private string DescribeResponse(ApiKind kind, string json, out string warning)
        {
            warning = null;
            if (kind == ApiKind.AgvStatus)
            {
                if (!TryParseStatus(json, out string map, out var list, out warning))
                    return warning;
                var codes = new List<string>();
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i] != null && !string.IsNullOrEmpty(list[i].robotCode))
                        codes.Add(list[i].robotCode);
                }

                return $"code=0  map={map}  车={string.Join(",", codes)}";
            }

            if (!TryParseTask(json, out string agv, out var tasks, out warning))
                return warning;
            return $"code=0  agv={agv}  任务 {tasks.Count} 条";
        }

        private string BuildUrl(ApiKind kind)
        {
            string baseUrl = (_baseUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl))
                return null;
            if (_ensureHttp &&
                !baseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                baseUrl = "http://" + baseUrl;
            }

            return baseUrl + (kind == ApiKind.AgvStatus ? PathAgvStatus : PathTaskStatus);
        }

        private static string KindLabel(ApiKind kind) =>
            kind == ApiKind.AgvStatus ? "queryAgvStatus" : "queryTaskStatus";

        private void TryLoadConfigs(bool notify)
        {
            var urlCfg = AssetDatabase.LoadAssetAtPath<URLBaseConfig>(UrlBaseConfigPath);
            if (urlCfg?.data?.Servers != null)
            {
                foreach (var entry in urlCfg.data.Servers)
                {
                    if (entry != null && entry.Key == ServerKey && !string.IsNullOrEmpty(entry.BaseUrl))
                    {
                        _baseUrl = entry.BaseUrl.Trim().TrimEnd('/');
                        break;
                    }
                }
            }
            else if (notify)
            {
                EditorUtility.DisplayDialog("海康接口测试", "未找到 URLBaseConfig。", "确定");
            }

            BindSiteFromAsset();
            string[] maps = HikvisionAgvSite.MapShortNames;
            if (maps.Length > 0 && Array.IndexOf(maps, _mapShortName) < 0)
                _mapShortName = maps[0];
            string[] robots = HikvisionAgvSite.RobotsOnMap(_mapShortName);
            if (robots.Length > 0 && Array.IndexOf(robots, _agvCode) < 0)
                _agvCode = robots[0];

            FillRequestTemplate();
            FillTwinTemplate();
            SavePrefs();
            _statusText = "已读取配置  " + (_baseUrl ?? "");
            if (notify)
                ShowNotification(new GUIContent(_statusText));
        }

        private static void BindSiteFromAsset()
        {
            var site = AssetDatabase.LoadAssetAtPath<HikvisionAgvSiteConfig>(SiteConfigPath);
            if (site?.data != null)
                HikvisionAgvSite.Bind(site.data);
        }

        private static bool HasBridge()
        {
            return FindFirstObjectByType<HikvisionDataBridge>() != null;
        }

        private void AddLog(bool ok, string source, string summary, string body, string warning)
        {
            var entry = new LogEntry
            {
                Time = DateTime.Now,
                Ok = ok,
                Source = source,
                Summary = summary,
                Body = body,
                Warning = warning
            };
            _logs.Add(entry);
            _selected = entry;
            while (_logs.Count > MaxLogCount)
                _logs.RemoveAt(0);
        }

        private static string NewReqCode()
        {
            return Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        }

        private static void RefreshReqCode(ref string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return;
            try
            {
                var jo = JObject.Parse(json);
                jo["reqCode"] = NewReqCode();
                json = jo.ToString(Formatting.Indented);
            }
            catch
            {
                // keep original
            }
        }

        private static string Pretty(object obj)
        {
            return JsonConvert.SerializeObject(obj, Formatting.Indented, JsonSetting);
        }

        private static string TryPretty(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return json ?? "";
            try
            {
                return JToken.Parse(json).ToString(Formatting.Indented);
            }
            catch
            {
                return json;
            }
        }

        private void EnsureStyles()
        {
            if (_okStyle != null)
                return;
            _okStyle = new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = new Color(0.35f, 0.8f, 0.4f) } };
            _failStyle = new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = new Color(1f, 0.4f, 0.35f) } };
            _mutedStyle = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
            _headerStyle = new GUIStyle(EditorStyles.boldLabel);
        }

        private void TickRecord()
        {
            if (!_recording || _recordRequesting)
                return;

            double now = EditorApplication.timeSinceStartup;
            if (now < _recordNextTime)
                return;

            _recordNextTime = now + Mathf.Max(0.05f, _recordInterval);
            StartRecordCycle();
        }

        private void StartRecording()
        {
            if (_recording)
                return;

            BindSiteFromAsset();
            if (string.IsNullOrWhiteSpace(_recordDir))
                _recordDir = DefaultRecordDir();

            try
            {
                Directory.CreateDirectory(_recordDir);
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("录制", "无法创建目录: " + e.Message, "确定");
                return;
            }

            string name = $"agv-status-{DateTime.Now:yyyyMMdd-HHmmss}.jsonl";
            _recordFilePath = Path.Combine(_recordDir, name);
            try
            {
                _recordWriter = HikvisionAgvStatusTape.OpenWrite(_recordFilePath, new HikvisionAgvStatusTape.Session
                {
                    startedAt = DateTimeOffset.Now.ToString("o"),
                    intervalSeconds = _recordInterval,
                    maps = ResolveRecordMaps(),
                    baseUrl = BuildUrl(ApiKind.AgvStatus)
                });
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("录制", "无法写文件: " + e.Message, "确定");
                _recordWriter = null;
                return;
            }

            _recordOk = 0;
            _recordFail = 0;
            _recordFrames = 0;
            _recordRequesting = false;
            _recording = true;
            _recordNextTime = EditorApplication.timeSinceStartup;
            _playbackPath = _recordFilePath;
            _statusText = "录制中  " + name;
            AddLog(true, "录制", "开始 " + name, _recordFilePath, null);
        }

        private void StopRecording(bool notify)
        {
            if (!_recording && _recordWriter == null)
                return;

            _recording = false;
            _recordRequesting = false;
            try
            {
                _recordWriter?.Flush();
                _recordWriter?.Dispose();
            }
            catch
            {
                // ignored
            }

            _recordWriter = null;
            if (notify)
            {
                _statusText = "录制结束  帧 " + _recordFrames;
                AddLog(true, "录制", _statusText, _recordFilePath, null);
            }
        }

        private string[] ResolveRecordMaps()
        {
            if (_recordAllMaps)
            {
                string[] maps = HikvisionAgvSite.MapShortNames;
                if (maps != null && maps.Length > 0)
                    return maps;
            }

            return new[] { string.IsNullOrEmpty(_mapShortName) ? "AA" : _mapShortName };
        }

        private void StartRecordCycle()
        {
            if (_recordRequesting)
                return;

            _recordMaps = ResolveRecordMaps();
            if (_recordMaps.Length == 0)
                return;

            _recordRequesting = true;
            _recordMapIndex = 0;
            FetchNextRecordMap();
        }

        private void FetchNextRecordMap()
        {
            if (!_recording)
            {
                _recordRequesting = false;
                return;
            }

            if (_recordMapIndex >= _recordMaps.Length)
            {
                _recordRequesting = false;
                return;
            }

            string map = _recordMaps[_recordMapIndex++];
            string url = BuildUrl(ApiKind.AgvStatus);
            if (string.IsNullOrEmpty(url))
            {
                AppendRecordFrame(HikvisionAgvStatusTape.MakeFrame(map, false, null, "BaseUrl 为空"));
                FetchNextRecordMap();
                return;
            }

            _recordRequesting = true;
            var body = new Dictionary<string, string>
            {
                ["reqCode"] = NewReqCode(),
                ["reqTime"] = string.Empty,
                ["clientCode"] = _clientCode ?? string.Empty,
                ["tokenCode"] = _tokenCode ?? string.Empty,
                ["mapShortName"] = map ?? string.Empty
            };
            string json = JsonConvert.SerializeObject(body, JsonSetting);
            PostJson(url, json, (ok, text, error) =>
            {
                if (!_recording)
                {
                    _recordRequesting = false;
                    return;
                }

                var frame = HikvisionAgvStatusTape.MakeFrame(map, ok, text, error);
                AppendRecordFrame(frame);
                FetchNextRecordMap();
                Repaint();
            });
        }

        private void AppendRecordFrame(HikvisionAgvStatusTape.Frame frame)
        {
            if (frame == null || !_recording)
                return;

            _recordFrames++;
            if (frame.ok)
                _recordOk++;
            else
                _recordFail++;

            try
            {
                HikvisionAgvStatusTape.WriteFrame(_recordWriter, frame);
            }
            catch (Exception e)
            {
                AddLog(false, "录制", "写盘失败 " + e.Message, null, e.Message);
                StopRecording(false);
                return;
            }

            int n = frame.data?.Count ?? 0;
            bool noisy = frame.ok && (_recordFrames % 20 != 0);
            if (!noisy)
            {
                AddLog(frame.ok, "录制 " + frame.map,
                    frame.ok ? $"{frame.time}  {n} 台" : frame.message,
                    JsonConvert.SerializeObject(frame, Formatting.Indented, JsonSetting),
                    frame.ok ? null : frame.message);
            }
        }

        private void LoadPlaybackFile()
        {
            if (string.IsNullOrWhiteSpace(_playbackPath))
            {
                EditorUtility.DisplayDialog("回放", "请选择录制文件。", "确定");
                return;
            }

            var frames = HikvisionAgvStatusTape.Load(_playbackPath, out _, out string error);
            if (!string.IsNullOrEmpty(error) && frames.Count == 0)
            {
                EditorUtility.DisplayDialog("回放", error, "确定");
                return;
            }

            _playbackFrames = frames;
            _playbackIndex = 0;
            _playing = false;
            _statusText = $"已加载 {frames.Count} 帧";
            AddLog(true, "回放", _statusText, _playbackPath, error);
        }

        private void StartPlayback()
        {
            if (_playbackFrames == null || _playbackFrames.Count == 0)
            {
                LoadPlaybackFile();
                if (_playbackFrames == null || _playbackFrames.Count == 0)
                    return;
            }

            if (!Application.isPlaying || !HasBridge())
            {
                EditorUtility.DisplayDialog("回放", "需要 Play，且场景挂有 HikvisionDataBridge。", "确定");
                return;
            }

            if (_playbackIndex >= _playbackFrames.Count)
                _playbackIndex = 0;

            _playing = true;
            SyncPlaybackClockFromIndex();
            _statusText = $"回放中  自 {_playbackIndex}/{_playbackFrames.Count}";
        }

        /// <summary>跳到指定帧，可作为回放起点；播放中跳转会重同步时间轴。</summary>
        private void SeekPlaybackTo(int index, bool preview)
        {
            if (_playbackFrames == null || _playbackFrames.Count == 0)
                return;

            _playbackIndex = Mathf.Clamp(index, 0, _playbackFrames.Count - 1);
            if (_playing)
                SyncPlaybackClockFromIndex();

            if (preview && Application.isPlaying && HasBridge())
                PublishPlaybackFrame(_playbackFrames[_playbackIndex]);

            _statusText = $"定位 {_playbackIndex}/{_playbackFrames.Count}";
            Repaint();
        }

        private void SyncPlaybackClockFromIndex()
        {
            if (_playbackFrames == null || _playbackFrames.Count == 0)
                return;

            int i = Mathf.Clamp(_playbackIndex, 0, _playbackFrames.Count - 1);
            _playbackOriginUnixMs = _playbackFrames[i].unixMs;
            _playbackStartRealtime = EditorApplication.timeSinceStartup;
        }

        private void PausePlayback()
        {
            if (!_playing)
                return;
            _playing = false;
            _statusText = $"回放暂停  {_playbackIndex}/{_playbackFrames?.Count ?? 0}";
        }

        private void StopPlayback(bool notify)
        {
            _playing = false;
            _playbackIndex = 0;
            if (notify)
                _statusText = "回放停止";
        }

        private void StepPlayback()
        {
            if (!Application.isPlaying || !HasBridge())
            {
                EditorUtility.DisplayDialog("回放", "需要 Play，且场景挂有 HikvisionDataBridge。", "确定");
                return;
            }

            if (_playbackFrames == null || _playbackIndex >= _playbackFrames.Count)
                return;

            PublishPlaybackFrame(_playbackFrames[_playbackIndex]);
            _playbackIndex++;
            if (_playbackIndex >= _playbackFrames.Count && _playbackLoop)
                _playbackIndex = 0;
            Repaint();
        }

        private void TickPlayback()
        {
            if (!_playing || _playbackFrames == null || _playbackFrames.Count == 0)
                return;

            if (!Application.isPlaying || !HasBridge())
            {
                PausePlayback();
                return;
            }

            double elapsedMs = (EditorApplication.timeSinceStartup - _playbackStartRealtime) * 1000.0 *
                               Mathf.Max(0.1f, _playbackSpeed);
            long target = _playbackOriginUnixMs + (long)elapsedMs;
            bool any = false;
            while (_playbackIndex < _playbackFrames.Count &&
                   _playbackFrames[_playbackIndex].unixMs <= target)
            {
                PublishPlaybackFrame(_playbackFrames[_playbackIndex]);
                _playbackIndex++;
                any = true;
            }

            if (_playbackIndex >= _playbackFrames.Count)
            {
                if (_playbackLoop)
                {
                    _playbackIndex = 0;
                    _playbackOriginUnixMs = _playbackFrames[0].unixMs;
                    _playbackStartRealtime = EditorApplication.timeSinceStartup;
                }
                else
                {
                    _playing = false;
                    _statusText = "回放结束";
                }
            }

            if (any)
                Repaint();
        }

        private void PublishPlaybackFrame(HikvisionAgvStatusTape.Frame frame)
        {
            if (frame == null || !frame.ok || frame.data == null || frame.data.Count == 0)
                return;

            string map = frame.map;
            if (string.IsNullOrEmpty(map))
                map = _mapShortName;
            IOCC.Publish(HikvisionAgvEvents.StatusRaw, map, frame.data);
            _statusText = $"回放 {_playbackIndex + 1}/{_playbackFrames.Count}  {map}  {frame.data.Count} 台";
        }

        private static string DefaultRecordDir()
        {
            string root = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            return Path.Combine(root, "AgvStatusRecords");
        }

        private void LoadPrefs()
        {
            _baseUrl = EditorPrefs.GetString(PrefPrefix + "BaseUrl", _baseUrl);
            _clientCode = EditorPrefs.GetString(PrefPrefix + "Client", _clientCode);
            _tokenCode = EditorPrefs.GetString(PrefPrefix + "Token", _tokenCode);
            _mapShortName = EditorPrefs.GetString(PrefPrefix + "Map", _mapShortName);
            _agvCode = EditorPrefs.GetString(PrefPrefix + "Agv", _agvCode);
            _ensureHttp = EditorPrefs.GetBool(PrefPrefix + "Http", _ensureHttp);
            _recordInterval = EditorPrefs.GetFloat(PrefPrefix + "RecInterval", _recordInterval);
            _recordAllMaps = EditorPrefs.GetBool(PrefPrefix + "RecAllMaps", _recordAllMaps);
            _recordDir = EditorPrefs.GetString(PrefPrefix + "RecDir", _recordDir);
            _playbackPath = EditorPrefs.GetString(PrefPrefix + "PlayPath", _playbackPath);
            _playbackSpeed = EditorPrefs.GetFloat(PrefPrefix + "PlaySpeed", _playbackSpeed);
            _playbackLoop = EditorPrefs.GetBool(PrefPrefix + "PlayLoop", _playbackLoop);
        }

        private void SavePrefs()
        {
            EditorPrefs.SetString(PrefPrefix + "BaseUrl", _baseUrl ?? "");
            EditorPrefs.SetString(PrefPrefix + "Client", _clientCode ?? "");
            EditorPrefs.SetString(PrefPrefix + "Token", _tokenCode ?? "");
            EditorPrefs.SetString(PrefPrefix + "Map", _mapShortName ?? "");
            EditorPrefs.SetString(PrefPrefix + "Agv", _agvCode ?? "");
            EditorPrefs.SetBool(PrefPrefix + "Http", _ensureHttp);
            EditorPrefs.SetFloat(PrefPrefix + "RecInterval", _recordInterval);
            EditorPrefs.SetBool(PrefPrefix + "RecAllMaps", _recordAllMaps);
            EditorPrefs.SetString(PrefPrefix + "RecDir", _recordDir ?? "");
            EditorPrefs.SetString(PrefPrefix + "PlayPath", _playbackPath ?? "");
            EditorPrefs.SetFloat(PrefPrefix + "PlaySpeed", _playbackSpeed);
            EditorPrefs.SetBool(PrefPrefix + "PlayLoop", _playbackLoop);
        }

        private sealed class LogEntry
        {
            public DateTime Time;
            public bool Ok;
            public string Source;
            public string Summary;
            public string Body;
            public string Warning;
        }
    }
}
