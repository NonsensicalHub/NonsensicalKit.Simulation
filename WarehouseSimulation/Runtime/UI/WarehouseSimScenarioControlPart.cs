using System;
using System.Collections.Generic;
using NaughtyAttributes;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Runtime
{
    /// <summary>
    /// 运行时任务配置档：新建 / 保存 / 加载 / 应用到 Runner。
    /// 可绑定 UGUI；若开启「绘制简易面板」则无需额外 UI 也能操作。
    /// </summary>
    public sealed class WarehouseSimScenarioControlPart : MonoBehaviour
    {
        [SerializeField, Label("仿真运行器")]
        private WarehouseSimRunner m_runner;

        [Header("可选 UGUI 绑定")]
        [SerializeField] private TMP_InputField m_input_id;
        [SerializeField] private TMP_InputField m_input_displayName;
        [SerializeField] private TMP_InputField m_input_quantity;
        [SerializeField] private TMP_Dropdown m_dropdown_direction;
        [SerializeField] private TMP_InputField m_input_occupancy;
        [SerializeField] private Toggle m_toggle_occupancyRandom;
        [SerializeField] private TMP_InputField m_input_hardwarePath;
        [SerializeField] private TMP_Dropdown m_dropdown_saved;
        [SerializeField] private TMP_InputField m_input_importJson;
        [SerializeField] private TextMeshProUGUI m_statusText;

        [SerializeField] private Button m_btn_new;
        [SerializeField] private Button m_btn_save;
        [SerializeField] private Button m_btn_load;
        [SerializeField] private Button m_btn_apply;
        [SerializeField] private Button m_btn_refresh;
        [SerializeField] private Button m_btn_exportShare;
        [SerializeField] private Button m_btn_importText;
#if !UNITY_WEBGL || UNITY_EDITOR
        [SerializeField] private Button m_btn_saveAs;
        [SerializeField] private Button m_btn_openFile;
#endif

        [Header("简易面板")]
        [SerializeField, Label("绘制简易面板（左上角）")]
        private bool m_drawSimplePanel = false;

        private WarehouseSimScenarioDto _draft;
        private string _status = "尚未操作。";
        private Vector2 _scroll;
        private string _importBuffer = "";
        private readonly List<string> _savedIds = new();

        private void Awake()
        {
            m_btn_new?.onClick.AddListener(OnNewClicked);
            m_btn_save?.onClick.AddListener(OnSaveClicked);
            m_btn_load?.onClick.AddListener(OnLoadSelectedClicked);
            m_btn_apply?.onClick.AddListener(OnApplyClicked);
            m_btn_refresh?.onClick.AddListener(RefreshSavedList);
            m_btn_exportShare?.onClick.AddListener(OnExportShareClicked);
            m_btn_importText?.onClick.AddListener(OnImportTextClicked);
#if !UNITY_WEBGL || UNITY_EDITOR
            m_btn_saveAs?.onClick.AddListener(OnSaveAsClicked);
            m_btn_openFile?.onClick.AddListener(OnOpenFileClicked);
#endif
        }

        private void OnDestroy()
        {
            m_btn_new?.onClick.RemoveListener(OnNewClicked);
            m_btn_save?.onClick.RemoveListener(OnSaveClicked);
            m_btn_load?.onClick.RemoveListener(OnLoadSelectedClicked);
            m_btn_apply?.onClick.RemoveListener(OnApplyClicked);
            m_btn_refresh?.onClick.RemoveListener(RefreshSavedList);
            m_btn_exportShare?.onClick.RemoveListener(OnExportShareClicked);
            m_btn_importText?.onClick.RemoveListener(OnImportTextClicked);
#if !UNITY_WEBGL || UNITY_EDITOR
            m_btn_saveAs?.onClick.RemoveListener(OnSaveAsClicked);
            m_btn_openFile?.onClick.RemoveListener(OnOpenFileClicked);
#endif
        }

        private void Start()
        {
            EnsureDraftFromRunner();
            RefreshSavedList();
            PushDraftToUi();
        }

        private void OnGUI()
        {
            if (!m_drawSimplePanel)
            {
                return;
            }

            const float w = 360f;
            var area = new Rect(12f, 12f, w, Mathf.Min(Screen.height - 24f, 520f));
            GUI.Box(area, "任务配置档 (JSON)");
            GUILayout.BeginArea(new Rect(area.x + 8f, area.y + 24f, area.width - 16f, area.height - 32f));
            _scroll = GUILayout.BeginScrollView(_scroll);

            EnsureDraftFromRunner();
            GUILayout.Label("存档 Id");
            _draft.Id = GUILayout.TextField(_draft.Id ?? "");
            GUILayout.Label("显示名");
            _draft.DisplayName = GUILayout.TextField(_draft.DisplayName ?? "");
            GUILayout.Label("硬件 Resources 路径");
            _draft.HardwareResourcesPath = GUILayout.TextField(_draft.HardwareResourcesPath ?? "");

            var qty = _draft.FlowPlan != null && _draft.FlowPlan.Length > 0
                ? _draft.FlowPlan[0].Quantity
                : 100;
            var dir = _draft.FlowPlan != null && _draft.FlowPlan.Length > 0
                ? _draft.FlowPlan[0].Direction
                : "Inbound";
            GUILayout.Label("首段数量");
            var qtyText = GUILayout.TextField(qty.ToString());
            if (int.TryParse(qtyText, out var parsedQty) && parsedQty >= 1)
            {
                qty = parsedQty;
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(string.Equals(dir, "Inbound", StringComparison.OrdinalIgnoreCase), "入库"))
            {
                dir = "Inbound";
            }

            if (GUILayout.Toggle(string.Equals(dir, "Outbound", StringComparison.OrdinalIgnoreCase), "出库"))
            {
                dir = "Outbound";
            }

            GUILayout.EndHorizontal();

            GUILayout.Label($"初始占用率 {_draft.InitialOccupancyRatio:0.##}");
            _draft.InitialOccupancyRatio = GUILayout.HorizontalSlider(_draft.InitialOccupancyRatio, 0f, 1f);
            _draft.InitialOccupancyRandom = GUILayout.Toggle(_draft.InitialOccupancyRandom, "随机占用");

            EnsureFlowHead(qty, dir);

            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("新建"))
            {
                OnNewClicked();
            }

            if (GUILayout.Button("保存"))
            {
                PullDraftFromUi();
                OnSaveClicked();
            }

            if (GUILayout.Button("应用并就绪"))
            {
                PullDraftFromUi();
                OnApplyClicked();
            }

            GUILayout.EndHorizontal();

            GUILayout.Label("已存档：");
            RefreshSavedListIfNeeded();
            for (var i = 0; i < _savedIds.Count; i++)
            {
                var id = _savedIds[i];
                GUILayout.BeginHorizontal();
                GUILayout.Label(id, GUILayout.Width(180f));
                if (GUILayout.Button("加载", GUILayout.Width(50f)))
                {
                    LoadId(id);
                }

                if (GUILayout.Button("删", GUILayout.Width(36f)))
                {
                    if (WarehouseSimScenarioStore.Delete(id, out var delErr))
                    {
                        SetStatus($"已删除 {id}");
                        RefreshSavedList();
                    }
                    else
                    {
                        SetStatus(delErr);
                    }
                }

                GUILayout.EndHorizontal();
            }

            GUILayout.Space(6f);
            if (GUILayout.Button("导出分享（下载/剪贴板）"))
            {
                PullDraftFromUi();
                OnExportShareClicked();
            }

#if !UNITY_WEBGL || UNITY_EDITOR
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("另存为…"))
            {
                PullDraftFromUi();
                OnSaveAsClicked();
            }

            if (GUILayout.Button("打开文件…"))
            {
                OnOpenFileClicked();
            }

            GUILayout.EndHorizontal();
#endif

            GUILayout.Label("从 JSON 文本导入：");
            _importBuffer = GUILayout.TextArea(_importBuffer ?? "", GUILayout.MinHeight(60f));
            if (GUILayout.Button("导入文本"))
            {
                m_input_importJson = null;
                ImportJsonText(_importBuffer);
            }

            GUILayout.Space(4f);
            GUILayout.Label(_status);
            GUILayout.Label($"目录：{WarehouseSimScenarioStore.DirectoryPath}");

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void EnsureFlowHead(int quantity, string direction)
        {
            _draft.FlowPlan ??= Array.Empty<SimFlowPlanEntryDto>();
            if (_draft.FlowPlan.Length == 0)
            {
                _draft.FlowPlan = new[]
                {
                    new SimFlowPlanEntryDto
                    {
                        Direction = direction,
                        Quantity = quantity,
                        ScheduleMode = "Instant",
                    },
                };
                return;
            }

            _draft.FlowPlan[0].Quantity = quantity;
            _draft.FlowPlan[0].Direction = direction;
        }

        private void EnsureDraftFromRunner()
        {
            if (_draft != null)
            {
                return;
            }

            if (m_runner != null && m_runner.Scenario != null)
            {
                _draft = m_runner.ExportCurrentScenarioDto();
            }
            else
            {
                _draft = WarehouseSimScenarioJson.CreateBlank();
            }
        }

        private void PullDraftFromUi()
        {
            EnsureDraftFromRunner();
            if (m_input_id != null)
            {
                _draft.Id = m_input_id.text;
            }

            if (m_input_displayName != null)
            {
                _draft.DisplayName = m_input_displayName.text;
            }

            if (m_input_hardwarePath != null)
            {
                _draft.HardwareResourcesPath = m_input_hardwarePath.text;
            }

            if (m_input_occupancy != null
                && float.TryParse(m_input_occupancy.text, out var occ))
            {
                _draft.InitialOccupancyRatio = Mathf.Clamp01(occ);
            }

            if (m_toggle_occupancyRandom != null)
            {
                _draft.InitialOccupancyRandom = m_toggle_occupancyRandom.isOn;
            }

            var qty = 100;
            if (m_input_quantity != null && int.TryParse(m_input_quantity.text, out var q) && q >= 1)
            {
                qty = q;
            }
            else if (_draft.FlowPlan != null && _draft.FlowPlan.Length > 0)
            {
                qty = _draft.FlowPlan[0].Quantity;
            }

            var dir = "Inbound";
            if (m_dropdown_direction != null && m_dropdown_direction.options.Count > 0)
            {
                dir = m_dropdown_direction.options[m_dropdown_direction.value].text;
            }
            else if (_draft.FlowPlan != null && _draft.FlowPlan.Length > 0)
            {
                dir = _draft.FlowPlan[0].Direction;
            }

            EnsureFlowHead(qty, dir);
            WarehouseSimScenarioJson.Normalize(_draft);
        }

        private void PushDraftToUi()
        {
            if (_draft == null)
            {
                return;
            }

            if (m_input_id != null)
            {
                m_input_id.text = _draft.Id;
            }

            if (m_input_displayName != null)
            {
                m_input_displayName.text = _draft.DisplayName;
            }

            if (m_input_hardwarePath != null)
            {
                m_input_hardwarePath.text = _draft.HardwareResourcesPath;
            }

            if (m_input_occupancy != null)
            {
                m_input_occupancy.text = _draft.InitialOccupancyRatio.ToString("0.###");
            }

            if (m_toggle_occupancyRandom != null)
            {
                m_toggle_occupancyRandom.isOn = _draft.InitialOccupancyRandom;
            }

            if (m_input_quantity != null)
            {
                var qty = _draft.FlowPlan != null && _draft.FlowPlan.Length > 0
                    ? _draft.FlowPlan[0].Quantity
                    : 100;
                m_input_quantity.text = qty.ToString();
            }

            if (m_dropdown_direction != null)
            {
                EnsureDirectionOptions();
                var dir = _draft.FlowPlan != null && _draft.FlowPlan.Length > 0
                    ? _draft.FlowPlan[0].Direction
                    : "Inbound";
                m_dropdown_direction.value = string.Equals(dir, "Outbound", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            }

            if (m_statusText != null)
            {
                m_statusText.text = _status;
            }
        }

        private void EnsureDirectionOptions()
        {
            if (m_dropdown_direction.options != null && m_dropdown_direction.options.Count >= 2)
            {
                return;
            }

            m_dropdown_direction.ClearOptions();
            m_dropdown_direction.AddOptions(new List<string> { "Inbound", "Outbound" });
        }

        private void OnNewClicked()
        {
            var hw = m_runner != null
                ? m_runner.HardwareResourcesPath
                : WarehouseSimScenarioJson.DefaultHardwareResourcesPath;
            _draft = WarehouseSimScenarioJson.CreateBlank("NewScenario", hw);
            PushDraftToUi();
            SetStatus("已新建空白配置（尚未保存）。");
        }

        private void OnSaveClicked()
        {
            PullDraftFromUi();
            var path = WarehouseSimScenarioStore.Save(_draft, out var error);
            if (path == null)
            {
                SetStatus($"保存失败：{error}");
                return;
            }

            RefreshSavedList();
            SetStatus($"已保存：{_draft.Id}");
        }

        private void OnApplyClicked()
        {
            if (m_runner == null)
            {
                SetStatus("未绑定仿真运行器。");
                return;
            }

            PullDraftFromUi();
            if (!m_runner.ApplyScenarioDto(_draft, out var error))
            {
                SetStatus($"应用失败：{error}");
                return;
            }

            SetStatus($"已应用「{_draft.DisplayName}」，可点运行仿真。");
        }

        private void OnLoadSelectedClicked()
        {
            if (m_dropdown_saved == null || m_dropdown_saved.options.Count == 0)
            {
                SetStatus("没有可加载的存档。");
                return;
            }

            LoadId(m_dropdown_saved.options[m_dropdown_saved.value].text);
        }

        private void LoadId(string id)
        {
            var dto = WarehouseSimScenarioStore.Load(id, out var error);
            if (dto == null)
            {
                SetStatus($"加载失败：{error}");
                return;
            }

            _draft = dto;
            PushDraftToUi();
            SetStatus($"已加载：{id}（再点「应用并就绪」才会用于仿真）");
        }

        private void OnExportShareClicked()
        {
            PullDraftFromUi();
            if (WarehouseSimScenarioStore.ExportForShare(_draft, out var message))
            {
                SetStatus(message);
            }
            else
            {
                SetStatus($"导出失败：{message}");
            }
        }

        private void OnImportTextClicked()
        {
            var text = m_input_importJson != null ? m_input_importJson.text : _importBuffer;
            ImportJsonText(text);
        }

        private void ImportJsonText(string text)
        {
            var dto = WarehouseSimScenarioStore.LoadFromJsonText(text, out var error);
            if (dto == null)
            {
                SetStatus($"导入失败：{error}");
                return;
            }

            _draft = dto;
            PushDraftToUi();
            SetStatus($"已从文本导入：{_draft.Id}");
        }

#if !UNITY_WEBGL || UNITY_EDITOR
        private void OnSaveAsClicked()
        {
            PullDraftFromUi();
            if (WarehouseSimScenarioStore.SaveAsDialog(_draft, out var path, out var error))
            {
                WarehouseSimScenarioStore.Save(_draft, out _);
                RefreshSavedList();
                PushDraftToUi();
                SetStatus($"另存为成功：{path}");
            }
            else
            {
                SetStatus(error);
            }
        }

        private void OnOpenFileClicked()
        {
            var dto = WarehouseSimScenarioStore.LoadFromDialog(out var path, out var error);
            if (dto == null)
            {
                SetStatus(error);
                return;
            }

            _draft = dto;
            PushDraftToUi();
            SetStatus($"已打开：{path}");
        }
#endif

        private void RefreshSavedList()
        {
            _savedIds.Clear();
            try
            {
                _savedIds.AddRange(WarehouseSimScenarioStore.ListIds());
            }
            catch (Exception ex)
            {
                SetStatus($"刷新列表失败：{ex.Message}");
            }

            if (m_dropdown_saved != null)
            {
                m_dropdown_saved.ClearOptions();
                m_dropdown_saved.AddOptions(new List<string>(_savedIds));
            }
        }

        private int _savedCountCache = -1;

        private void RefreshSavedListIfNeeded()
        {
            try
            {
                var count = WarehouseSimScenarioStore.ListIds().Count;
                if (count != _savedCountCache)
                {
                    _savedCountCache = count;
                    RefreshSavedList();
                }
            }
            catch
            {
                // ignore
            }
        }

        private void SetStatus(string text)
        {
            _status = text ?? "";
            if (m_statusText != null)
            {
                m_statusText.text = _status;
            }

            Debug.Log($"[WarehouseSimulation][ScenarioUI] {_status}", this);
        }
    }
}