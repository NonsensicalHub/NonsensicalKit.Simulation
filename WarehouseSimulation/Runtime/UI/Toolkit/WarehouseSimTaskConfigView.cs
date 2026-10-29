using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Runtime
{
/// <summary>
/// 仓储仿真任务的运行时编辑：流程计划、初始占用和策略。写入当前 Scenario，下一次运行生效。
/// </summary>
public sealed class WarehouseSimTaskConfigView
{
    private static readonly StackerSlotPlacementStrategy[] PlacementValues =
    {
        StackerSlotPlacementStrategy.NearestToPickup,
        StackerSlotPlacementStrategy.FillColumnFirst,
    };

    private static readonly string[] PlacementLabels =
    {
        "就近放置",
        "按列放置",
    };

    private static readonly InfeedPortSelectionStrategy[] PortValues =
    {
        InfeedPortSelectionStrategy.RoundRobin,
        InfeedPortSelectionStrategy.ShortestQueue,
    };

    private static readonly string[] PortLabels =
    {
        "轮询",
        "最短队列",
    };

    private static readonly ConveyorRoutingStrategy[] RoutingValues =
    {
        ConveyorRoutingStrategy.ShortestDistance,
        ConveyorRoutingStrategy.LeastCongestion,
        ConveyorRoutingStrategy.PreferAssignedStacker,
    };

    private static readonly string[] RoutingLabels =
    {
        "最短时间",
        "最少拥堵（同最短时间）",
        "优先已分配堆垛机",
    };

    private static readonly string[] DirectionLabels = { "入库", "出库" };
    private static readonly string[] ScheduleLabels = { "瞬间释放", "分次释放" };

    private readonly List<Action> _unbindActions = new();

    private WarehouseSimRunner _runner;
    private WarehouseSimScenario _scenario;
    private WarehouseSimStrategyProfile _strategy;
    private VisualElement _form;
    private VisualElement _flowList;
    private VisualElement _weights;
    private Label _note;
    private Label _missing;
    private Label _preview;
    private Label _occupancyLabel;
    private Label _strategyNote;
    private Slider _occupancy;
    private Toggle _occupancyRandom;
    private IntegerField _seed;
    private DropdownField _placement;
    private DropdownField _port;
    private DropdownField _routing;
    private FloatField _distance;
    private FloatField _congested;
    private FloatField _total;
    private VisualElement _presetBox;
    private DropdownField _presetDropdown;
    private TextField _presetName;
    private Label _presetStatus;
    private Button _presetNew;
    private Button _presetSave;
    private Button _presetCopy;
    private Button _presetDelete;
    private WarehouseSimTaskPresetStore _presets;
    private readonly List<string> _presetNames = new();
    private bool _bound;
    private bool _syncing;
    private bool _presetSync;
    private bool _interactable = true;

    public void Bind(VisualElement root, WarehouseSimRunner runner)
    {
        if (_bound || root == null)
        {
            return;
        }

        _note = root.Q<Label>("task-note");
        _missing = root.Q<Label>("task-missing");
        _preview = root.Q<Label>("task-preview");
        _occupancyLabel = root.Q<Label>("occupancy-label");
        _strategyNote = root.Q<Label>("strategy-note");
        _form = root.Q("task-form");
        _flowList = root.Q("flow-list");
        _weights = root.Q("route-weights");
        _occupancy = root.Q<Slider>("occupancy");
        _occupancyRandom = root.Q<Toggle>("occupancy-random");
        _seed = root.Q<IntegerField>("flow-seed");
        _placement = root.Q<DropdownField>("placement");
        _port = root.Q<DropdownField>("port");
        _routing = root.Q<DropdownField>("routing");
        _distance = root.Q<FloatField>("route-distance");
        _congested = root.Q<FloatField>("route-congested");
        _total = root.Q<FloatField>("route-total");
        _presetBox = root.Q("preset-box");
        _presetDropdown = root.Q<DropdownField>("preset");
        _presetName = root.Q<TextField>("preset-name");
        _presetStatus = root.Q<Label>("preset-status");
        _presetNew = root.Q<Button>("preset-new");
        _presetSave = root.Q<Button>("preset-save");
        _presetCopy = root.Q<Button>("preset-copy");
        _presetDelete = root.Q<Button>("preset-delete");
        _runner = runner;
        _scenario = runner != null ? runner.Scenario : null;
        _bound = true;

        if (_scenario == null)
        {
            ShowMissing(true);
            return;
        }

        ShowMissing(false);
        _presets = WarehouseSimTaskPresetStore.LoadOrCreate(_scenario, _runner != null ? _runner.HardwareResourcesPath : null);
        _presets.Apply(_scenario);
        EnsurePlan();
        _strategy = _scenario.ResolvedStrategy;
        _syncing = true;
        BindStableFields(root.Q<Button>("flow-add"));
        BindPresetControls();
        LoadStableFields();
        SyncPresetFields();
        _syncing = false;
        RebuildFlowList();
        RefreshPreview();
        ApplyInteractable();
        SetPresetStatus($"当前是「{_presets.ActiveName}」。保存会写入 JSON（含 Web）。");
    }

    public void Unbind()
    {
        if (!_bound)
        {
            return;
        }

        _presetName?.Blur();
        CommitActivePreset();
        _presets?.Flush();

        for (var i = _unbindActions.Count - 1; i >= 0; i--)
        {
            _unbindActions[i]?.Invoke();
        }

        _unbindActions.Clear();
        _flowList?.Clear();
        _presets = null;
        _presetNames.Clear();
        _runner = null;
        _scenario = null;
        _strategy = null;
        _form = null;
        _flowList = null;
        _weights = null;
        _note = null;
        _missing = null;
        _preview = null;
        _occupancyLabel = null;
        _strategyNote = null;
        _occupancy = null;
        _occupancyRandom = null;
        _seed = null;
        _placement = null;
        _port = null;
        _routing = null;
        _distance = null;
        _congested = null;
        _total = null;
        _presetBox = null;
        _presetDropdown = null;
        _presetName = null;
        _presetStatus = null;
        _presetNew = null;
        _presetSave = null;
        _presetCopy = null;
        _presetDelete = null;
        _bound = false;
    }

    public void Persist()
    {
        CommitActivePreset();
        _presets?.Flush();
    }

    public void SetInteractable(bool interactable)
    {
        if (_interactable == interactable)
        {
            return;
        }

        _interactable = interactable;
        ApplyInteractable();
    }

    private void BindStableFields(Button addButton)
    {
        if (_occupancy != null)
        {
            _occupancy.lowValue = 0f;
            _occupancy.highValue = 100f;
            _occupancy.tooltip = "仿真开始时，货架可存储货位的占用百分比。";
            Listen(_occupancy, OnOccupancyChanged);
        }

        if (_occupancyRandom != null)
        {
            _occupancyRandom.tooltip = "开启后按占用率随机选货位；关闭则按遍历顺序填充。";
            Listen(_occupancyRandom, OnOccupancyRandomChanged);
        }

        if (_seed != null)
        {
            _seed.isDelayed = true;
            _seed.tooltip = "分次释放时使用。相同种子可以复现同一批随机间隔。";
            Listen(_seed, OnSeedChanged);
        }

        BindEnumDropdown(_placement, PlacementValues, PlacementLabels, OnPlacementChanged,
            "堆垛机给货物选货位的方式。");
        BindEnumDropdown(_port, PortValues, PortLabels, OnPortChanged,
            "多入库口或多出库口时，下一箱分到哪个端口。");
        BindEnumDropdown(_routing, RoutingValues, RoutingLabels, OnRoutingChanged,
            "从端口到堆垛机交互点的输送路径。");

        BindWeight(_distance, value => _strategy.RouteDistanceWeight = value, "路径评分中的距离系数。");
        BindWeight(_congested, value => _strategy.RouteCongestedCargoWeight = value, "路径评分中的拥堵箱数系数。");
        BindWeight(_total, value => _strategy.RouteTotalCargoWeight = value, "路径评分中的在途箱数系数。");

        if (addButton != null)
        {
            addButton.tooltip = "在流程计划末尾追加一段入库。";
            Action onAdd = AddEntry;
            addButton.clicked += onAdd;
            _unbindActions.Add(() => addButton.clicked -= onAdd);
        }
    }

    private void LoadStableFields()
    {
        var percent = Mathf.Clamp(_scenario.InitialOccupancyRatio, 0f, 1f) * 100f;
        _occupancy?.SetValueWithoutNotify(percent);
        SetOccupancyLabel(percent);
        _occupancyRandom?.SetValueWithoutNotify(_scenario.InitialOccupancyRandom);
        _seed?.SetValueWithoutNotify(_scenario.FlowRandomSeed);

        if (_strategy != null)
        {
            SetEnumValue(_placement, PlacementValues, PlacementLabels, _strategy.StackerPlacementStrategy);
            SetEnumValue(_port, PortValues, PortLabels, _strategy.InfeedPortSelectionStrategy);
            SetEnumValue(_routing, RoutingValues, RoutingLabels, _strategy.ConveyorRoutingStrategy);
            _distance?.SetValueWithoutNotify(_strategy.RouteDistanceWeight);
            _congested?.SetValueWithoutNotify(_strategy.RouteCongestedCargoWeight);
            _total?.SetValueWithoutNotify(_strategy.RouteTotalCargoWeight);
            ApplyRoutingExtras();
        }

        if (_strategyNote != null)
        {
            _strategyNote.text = _scenario.Strategy != null ? "策略" : "策略（内置默认）";
        }
    }

    private void OnOccupancyChanged(ChangeEvent<float> evt)
    {
        if (_syncing || _scenario == null)
        {
            return;
        }

        var percent = Mathf.Clamp(evt.newValue, 0f, 100f);
        _scenario.InitialOccupancyRatio = percent / 100f;
        SetOccupancyLabel(percent);
        RefreshPreview();
    }

    private void OnOccupancyRandomChanged(ChangeEvent<bool> evt)
    {
        if (_syncing || _scenario == null)
        {
            return;
        }

        _scenario.InitialOccupancyRandom = evt.newValue;
        RefreshPreview();
    }

    private void OnSeedChanged(ChangeEvent<int> evt)
    {
        if (_syncing || _scenario == null)
        {
            return;
        }

        _scenario.FlowRandomSeed = evt.newValue;
        NotifyChanged();
    }

    private void OnPlacementChanged(StackerSlotPlacementStrategy value)
    {
        if (_syncing || _strategy == null)
        {
            return;
        }

        _strategy.StackerPlacementStrategy = value;
        NotifyChanged();
    }

    private void OnPortChanged(InfeedPortSelectionStrategy value)
    {
        if (_syncing || _strategy == null)
        {
            return;
        }

        _strategy.InfeedPortSelectionStrategy = value;
        NotifyChanged();
    }

    private void OnRoutingChanged(ConveyorRoutingStrategy value)
    {
        if (_syncing || _strategy == null)
        {
            return;
        }

        _strategy.ConveyorRoutingStrategy = value;
        ApplyRoutingExtras();
        NotifyChanged();
    }

    private void ApplyRoutingExtras()
    {
        if (_weights == null)
        {
            return;
        }

        _weights.style.display = DisplayStyle.None;
    }

    private void EnsurePlan()
    {
        if (_scenario.FlowPlan == null || _scenario.FlowPlan.Length == 0)
        {
            _scenario.FlowPlan = new[] { SimFlowPlanDefaults.InstantInbound(100) };
            return;
        }

        for (var i = 0; i < _scenario.FlowPlan.Length; i++)
        {
            if (_scenario.FlowPlan[i] == null)
            {
                _scenario.FlowPlan[i] = SimFlowPlanDefaults.InstantInbound(100);
            }
        }
    }

    private void AddEntry()
    {
        if (_scenario == null || !_interactable)
        {
            return;
        }

        EnsurePlan();
        var list = new List<SimFlowPlanEntry>(_scenario.FlowPlan)
        {
            SimFlowPlanDefaults.InstantInbound(100),
        };
        _scenario.FlowPlan = list.ToArray();
        RebuildFlowList();
        RefreshPreview();
    }

    private void RemoveEntry(int index)
    {
        if (_scenario == null || !_interactable || _scenario.FlowPlan == null || _scenario.FlowPlan.Length <= 1)
        {
            return;
        }

        if (index < 0 || index >= _scenario.FlowPlan.Length)
        {
            return;
        }

        var list = new List<SimFlowPlanEntry>(_scenario.FlowPlan);
        list.RemoveAt(index);
        _scenario.FlowPlan = list.ToArray();
        RebuildFlowList();
        RefreshPreview();
    }

    private void RebuildFlowList()
    {
        if (_flowList == null || _scenario?.FlowPlan == null)
        {
            return;
        }

        _flowList.Clear();
        var plan = _scenario.FlowPlan;
        for (var i = 0; i < plan.Length; i++)
        {
            _flowList.Add(BuildFlowCard(plan[i], i, plan.Length));
        }

        ApplyInteractable();
    }

    private VisualElement BuildFlowCard(SimFlowPlanEntry entry, int index, int count)
    {
        var card = new VisualElement();
        card.AddToClassList("flow-card");

        var header = new VisualElement();
        header.AddToClassList("row");
        header.AddToClassList("flow-header");
        var title = new Label($"第 {index + 1} 段");
        title.AddToClassList("flow-title");
        var remove = new Button { text = "删除" };
        remove.AddToClassList("mini");
        remove.AddToClassList("task-input");
        remove.AddToClassList("flow-remove");
        remove.tooltip = count <= 1 ? "至少保留一段流程。" : "删除这一段。";
        var captured = index;
        remove.clicked += () => RemoveEntry(captured);
        header.Add(title);
        header.Add(remove);
        card.Add(header);

        var direction = CreateChoice("流向", DirectionLabels, DirectionIndex(entry.Direction),
            "入库把货物放进货架，出库从货架取出。");
        direction.RegisterValueChangedCallback(evt =>
        {
            if (_syncing)
            {
                return;
            }

            entry.Direction = evt.newValue == "出库"
                ? SimFlowDirection.Outbound
                : SimFlowDirection.Inbound;
            RefreshPreview();
        });
        card.Add(direction);

        var warehouses = new TextField("库号限定")
        {
            value = SimWarehouseRestriction.FormatList(entry.AllowedWarehouseIds),
            isDelayed = true,
        };
        StyleField(warehouses, WarehouseLimitTooltip());
        warehouses.RegisterValueChangedCallback(evt =>
        {
            if (_syncing)
            {
                return;
            }

            entry.AllowedWarehouseIds = SimWarehouseRestriction.Parse(evt.newValue);
            var formatted = SimWarehouseRestriction.FormatList(entry.AllowedWarehouseIds);
            if (formatted != evt.newValue)
            {
                warehouses.SetValueWithoutNotify(formatted);
            }

            RefreshPreview();
        });
        card.Add(warehouses);

        var quantity = new IntegerField("数量") { value = Mathf.Max(1, entry.Quantity), isDelayed = true };
        StyleField(quantity, "这一段要处理的箱数。");
        quantity.RegisterValueChangedCallback(evt =>
        {
            if (_syncing)
            {
                return;
            }

            var clamped = Mathf.Max(1, evt.newValue);
            entry.Quantity = clamped;
            if (clamped != evt.newValue)
            {
                quantity.SetValueWithoutNotify(clamped);
            }

            RefreshPreview();
        });
        card.Add(quantity);

        var delay = new FloatField("起始延迟(秒)")
        {
            value = Mathf.Max(0f, entry.StartDelaySeconds),
            isDelayed = true,
        };
        StyleField(delay, "这一段第一箱相对仿真开始的延迟。");
        delay.RegisterValueChangedCallback(evt =>
        {
            if (_syncing)
            {
                return;
            }

            var clamped = Mathf.Max(0f, evt.newValue);
            entry.StartDelaySeconds = clamped;
            if (!Mathf.Approximately(clamped, evt.newValue))
            {
                delay.SetValueWithoutNotify(clamped);
            }

            RefreshPreview();
        });
        card.Add(delay);

        var schedule = CreateChoice(
            "释放节奏",
            ScheduleLabels,
            entry.ScheduleMode == SimFlowScheduleMode.Staggered ? 1 : 0,
            "瞬间释放会在起始时刻一次性放出全部；分次释放按间隔分批放出。");
        var staggered = new VisualElement();
        staggered.style.display = entry.ScheduleMode == SimFlowScheduleMode.Staggered
            ? DisplayStyle.Flex
            : DisplayStyle.None;
        schedule.RegisterValueChangedCallback(evt =>
        {
            if (_syncing)
            {
                return;
            }

            entry.ScheduleMode = evt.newValue == "分次释放"
                ? SimFlowScheduleMode.Staggered
                : SimFlowScheduleMode.Instant;
            staggered.style.display = entry.ScheduleMode == SimFlowScheduleMode.Staggered
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            RefreshPreview();
        });
        card.Add(schedule);

        AddStaggeredFields(staggered, entry);
        card.Add(staggered);

        var tags = new TextField("加工标签")
        {
            value = FormatTags(entry.RequiredProcessTags),
            isDelayed = true,
        };
        StyleField(tags, "逗号分隔。例如 wrap 表示必须经过缠膜站点。留空表示没有额外加工。");
        tags.RegisterValueChangedCallback(evt =>
        {
            if (_syncing)
            {
                return;
            }

            entry.RequiredProcessTags = ParseTags(evt.newValue);
            NotifyChanged();
        });
        card.Add(tags);
        return card;
    }

    private void AddStaggeredFields(VisualElement parent, SimFlowPlanEntry entry)
    {
        var qtyMin = new IntegerField("每次最少")
        {
            value = Mathf.Max(1, entry.RandomQuantityMin),
            isDelayed = true,
        };
        var qtyMax = new IntegerField("每次最多")
        {
            value = Mathf.Max(qtyMin.value, entry.RandomQuantityMax),
            isDelayed = true,
        };
        StyleField(qtyMin, "分次释放时，每一批的最少箱数。与最多相等时为固定批量。");
        StyleField(qtyMax, "分次释放时，每一批的最多箱数。");
        qtyMin.RegisterValueChangedCallback(evt =>
        {
            if (_syncing)
            {
                return;
            }

            var min = Mathf.Max(1, evt.newValue);
            entry.RandomQuantityMin = min;
            if (min != evt.newValue)
            {
                qtyMin.SetValueWithoutNotify(min);
            }

            if (entry.RandomQuantityMax < min)
            {
                entry.RandomQuantityMax = min;
                qtyMax.SetValueWithoutNotify(min);
            }

            RefreshPreview();
        });
        qtyMax.RegisterValueChangedCallback(evt =>
        {
            if (_syncing)
            {
                return;
            }

            var max = Mathf.Max(entry.RandomQuantityMin, evt.newValue);
            entry.RandomQuantityMax = max;
            if (max != evt.newValue)
            {
                qtyMax.SetValueWithoutNotify(max);
            }

            RefreshPreview();
        });
        parent.Add(qtyMin);
        parent.Add(qtyMax);

        var intervalMin = new FloatField("间隔下限(秒)")
        {
            value = Mathf.Max(0.01f, entry.RandomIntervalMinSeconds),
            isDelayed = true,
        };
        var intervalMax = new FloatField("间隔上限(秒)")
        {
            value = Mathf.Max(intervalMin.value, entry.RandomIntervalMaxSeconds),
            isDelayed = true,
        };
        StyleField(intervalMin, "两批之间的最短间隔。与上限相等时为固定间隔。");
        StyleField(intervalMax, "两批之间的最长间隔。");
        intervalMin.RegisterValueChangedCallback(evt =>
        {
            if (_syncing)
            {
                return;
            }

            var min = Mathf.Max(0.01f, evt.newValue);
            entry.RandomIntervalMinSeconds = min;
            if (!Mathf.Approximately(min, evt.newValue))
            {
                intervalMin.SetValueWithoutNotify(min);
            }

            if (entry.RandomIntervalMaxSeconds < min)
            {
                entry.RandomIntervalMaxSeconds = min;
                intervalMax.SetValueWithoutNotify(min);
            }

            RefreshPreview();
        });
        intervalMax.RegisterValueChangedCallback(evt =>
        {
            if (_syncing)
            {
                return;
            }

            var max = Mathf.Max(entry.RandomIntervalMinSeconds, evt.newValue);
            entry.RandomIntervalMaxSeconds = max;
            if (!Mathf.Approximately(max, evt.newValue))
            {
                intervalMax.SetValueWithoutNotify(max);
            }

            RefreshPreview();
        });
        parent.Add(intervalMin);
        parent.Add(intervalMax);
    }

    private void RefreshPreview()
    {
        if (_preview == null || _scenario == null)
        {
            return;
        }

        var plan = SimFlowPlanResolver.Resolve(_scenario);
        _preview.text =
            SimStrategyLabels.FormatFlowPlan(plan) + "\n" +
            "初始占用 " + SimStrategyLabels.FormatInitialOccupancy(
                _scenario.InitialOccupancyRatio,
                _scenario.InitialOccupancyRandom);
        NotifyChanged();
    }

    private void SetOccupancyLabel(float percent)
    {
        if (_occupancyLabel != null)
        {
            _occupancyLabel.text = $"初始占用 {Mathf.RoundToInt(percent)}%";
        }
    }

    private void ShowMissing(bool missing)
    {
        if (_missing != null)
        {
            _missing.style.display = missing ? DisplayStyle.Flex : DisplayStyle.None;
        }

        if (_form != null)
        {
            _form.style.display = missing ? DisplayStyle.None : DisplayStyle.Flex;
        }

        if (_presetBox != null)
        {
            _presetBox.style.display = missing ? DisplayStyle.None : DisplayStyle.Flex;
        }

        if (_note != null && missing)
        {
            _note.text = "场景里的仿真运行器没有指定任务。";
        }

        if (missing && _preview != null)
        {
            _preview.text = "未指定仿真任务。";
        }
    }

    private void ApplyInteractable()
    {
        if (_form == null)
        {
            return;
        }

        var singleSegment = _scenario == null || _scenario.FlowPlan == null || _scenario.FlowPlan.Length <= 1;
        _form.Query(className: "task-input").ForEach(element =>
        {
            var allow = _interactable;
            if (element.ClassListContains("flow-remove") && singleSegment)
            {
                allow = false;
            }

            element.SetEnabled(allow);
        });

        UpdatePresetEnabled();

        if (_note != null && _scenario != null)
        {
            _note.text = _interactable
                ? "改的是当前配置档。切换档位会整套换上那一档的流程、占用和策略。"
                : "仿真或导出进行中，暂不能修改。";
        }
    }

    private void BindPresetControls()
    {
        if (_presetDropdown != null)
        {
            _presetDropdown.tooltip = "在已保存的配置档之间切换。每一档保存为 JSON（流程、占用、策略）。";
            Listen(_presetDropdown, OnPresetPicked);
        }

        if (_presetName != null)
        {
            _presetName.isDelayed = true;
            _presetName.tooltip = "这一档的名称。回车或点到别处后生效。";
            Listen(_presetName, OnPresetNameChanged);
        }

        BindPresetButton(_presetNew, NewPreset, "新建一档空白任务（默认 100 入库），再改数量后点保存。");
        BindPresetButton(_presetSave, SavePreset, "把当前流程、占用和策略写入这一档（JSON）。");
        BindPresetButton(_presetCopy, CopyPreset, "复制当前档为新 JSON。改数量后点保存，例如 100 和 1000。");
        BindPresetButton(_presetDelete, DeletePreset, "删除当前档。至少保留一档。");
    }

    private void BindPresetButton(Button button, Action action, string tooltip)
    {
        if (button == null)
        {
            return;
        }

        button.tooltip = tooltip;
        button.clicked += action;
        _unbindActions.Add(() => button.clicked -= action);
    }

    private void OnPresetPicked(ChangeEvent<string> evt)
    {
        if (_presetSync || _syncing || _presets == null || !_interactable)
        {
            return;
        }

        var index = _presetNames.IndexOf(evt.newValue);
        if (index < 0 || index == _presets.ActiveIndex)
        {
            return;
        }

        CommitActivePreset();
        if (!_presets.TrySetActive(index))
        {
            return;
        }

        ReloadFromActivePreset($"已切换到「{_presets.ActiveName}」。下次运行仿真会用这一档。");
        _presets.Flush();
    }

    private void OnPresetNameChanged(ChangeEvent<string> evt)
    {
        if (_presetSync || _syncing || _presets == null)
        {
            return;
        }

        _presets.RenameActive(evt.newValue);
        SyncPresetFields();
        _presets.Flush();
        SetPresetStatus($"当前是「{_presets.ActiveName}」。");
    }

    private void NewPreset()
    {
        if (_presets == null || _scenario == null || !_interactable)
        {
            return;
        }

        _presetName?.Blur();
        CommitActivePreset();
        _presets.CreateBlank("新建配置");
        ReloadFromActivePreset($"已新建「{_presets.ActiveName}」。改完后点保存。");
        _presets.Flush();
    }

    private void SavePreset()
    {
        if (_presets == null || _scenario == null || !_interactable)
        {
            return;
        }

        _presetName?.Blur();
        CommitActivePreset();
        SyncPresetFields();
        _presets.Flush();
        SetPresetStatus($"已保存「{_presets.ActiveName}」到 JSON。");
    }

    private void CopyPreset()
    {
        if (_presets == null || _scenario == null || !_interactable)
        {
            return;
        }

        _presetName?.Blur();
        CommitActivePreset();
        _presets.DuplicateActive(ReadPresetName() + " 副本");
        SyncPresetFields();
        _presets.Flush();
        SetPresetStatus($"已另存为「{_presets.ActiveName}」。改完数量后再点保存。");
    }

    private void DeletePreset()
    {
        if (_presets == null || _scenario == null || !_interactable || _presets.Count <= 1)
        {
            return;
        }

        var removed = _presets.ActiveName;
        if (!_presets.DeleteActive())
        {
            return;
        }

        ReloadFromActivePreset($"已删除「{removed}」，当前是「{_presets.ActiveName}」。");
        _presets.Flush();
    }

    private void ReloadFromActivePreset(string status)
    {
        _syncing = true;
        _presets.Apply(_scenario);
        EnsurePlan();
        _strategy = _scenario.ResolvedStrategy;
        LoadStableFields();
        SyncPresetFields();
        _syncing = false;
        RebuildFlowList();
        RefreshPreview();
        SetPresetStatus(status);
    }

    private void CommitActivePreset()
    {
        if (_presets == null || _scenario == null)
        {
            return;
        }

        _presets.RenameActive(ReadPresetName());
        _presets.Capture(_scenario);
    }

    private void SyncPresetFields()
    {
        if (_presets == null)
        {
            return;
        }

        _presets.CopyNames(_presetNames);
        _presetSync = true;
        if (_presetDropdown != null)
        {
            _presetDropdown.choices = new List<string>(_presetNames);
            if (_presetNames.Count > 0)
            {
                _presetDropdown.SetValueWithoutNotify(_presetNames[_presets.ActiveIndex]);
            }
        }

        _presetName?.SetValueWithoutNotify(_presets.ActiveName);
        _presetSync = false;
        UpdatePresetEnabled();
    }

    private void UpdatePresetEnabled()
    {
        var allow = _interactable && _presets != null;
        _presetDropdown?.SetEnabled(allow);
        _presetName?.SetEnabled(allow);
        _presetNew?.SetEnabled(allow);
        _presetSave?.SetEnabled(allow);
        _presetCopy?.SetEnabled(allow);
        _presetDelete?.SetEnabled(allow && _presets.Count > 1);
    }

    private string ReadPresetName()
    {
        if (_presetName == null || _presets == null)
        {
            return _presets != null ? _presets.ActiveName : "未命名";
        }

        var text = _presetName.value != null ? _presetName.value.Trim() : string.Empty;
        return text.Length > 0 ? text : _presets.ActiveName;
    }

    private void SetPresetStatus(string text)
    {
        if (_presetStatus != null)
        {
            _presetStatus.text = text;
        }
    }

    private void NotifyChanged()
    {
        if (_syncing || !_bound || _presets == null || _scenario == null)
        {
            return;
        }

        _presets.Capture(_scenario);
    }

    private void BindEnumDropdown<T>(
        DropdownField field,
        T[] values,
        string[] labels,
        Action<T> onChanged,
        string tooltip) where T : struct
    {
        if (field == null)
        {
            return;
        }

        field.choices = new List<string>(labels);
        field.tooltip = tooltip;
        EventCallback<ChangeEvent<string>> callback = evt =>
        {
            if (_syncing)
            {
                return;
            }

            var index = IndexOf(labels, evt.newValue);
            if (index >= 0)
            {
                onChanged(values[index]);
            }
        };
        field.RegisterValueChangedCallback(callback);
        _unbindActions.Add(() => field.UnregisterValueChangedCallback(callback));
    }

    private void BindWeight(FloatField field, Action<float> apply, string tooltip)
    {
        if (field == null)
        {
            return;
        }

        field.isDelayed = true;
        field.tooltip = tooltip;
        EventCallback<ChangeEvent<float>> callback = evt =>
        {
            if (_syncing || _strategy == null)
            {
                return;
            }

            var clamped = Mathf.Max(0f, evt.newValue);
            apply(clamped);
            if (!Mathf.Approximately(clamped, evt.newValue))
            {
                field.SetValueWithoutNotify(clamped);
            }

            NotifyChanged();
        };
        field.RegisterValueChangedCallback(callback);
        _unbindActions.Add(() => field.UnregisterValueChangedCallback(callback));
    }

    private void Listen<T>(INotifyValueChanged<T> field, EventCallback<ChangeEvent<T>> callback)
    {
        field.RegisterValueChangedCallback(callback);
        _unbindActions.Add(() => field.UnregisterValueChangedCallback(callback));
    }

    private static void SetEnumValue<T>(DropdownField field, T[] values, string[] labels, T current)
        where T : struct
    {
        if (field == null)
        {
            return;
        }

        field.choices = new List<string>(labels);
        var index = Array.IndexOf(values, current);
        if (index < 0)
        {
            index = 0;
        }

        field.SetValueWithoutNotify(labels[index]);
    }

    private static DropdownField CreateChoice(string label, string[] labels, int index, string tooltip)
    {
        var field = new DropdownField(label)
        {
            choices = new List<string>(labels),
            index = Mathf.Clamp(index, 0, Math.Max(0, labels.Length - 1)),
        };
        StyleField(field, tooltip);
        return field;
    }

    private static void StyleField(VisualElement field, string tooltip)
    {
        field.AddToClassList("field");
        field.AddToClassList("task-input");
        field.tooltip = tooltip;
    }

    private string WarehouseLimitTooltip()
    {
        const string baseText =
            "留空表示不限制仓库。填写库号后，这一段入库只能放到这些仓库，出库只能从这些仓库取货。多个库号用逗号或空格分隔。";
        var bindings = _scenario != null ? _scenario.ResolvedHardwareBindings : null;
        var known = bindings != null ? bindings.Warehouses : null;
        if (known == null || known.Length == 0)
        {
            return baseText;
        }

        var text = new System.Text.StringBuilder(baseText);
        text.Append(" 当前仓库：");
        var first = true;
        for (var i = 0; i < known.Length; i++)
        {
            var warehouse = known[i];
            if (warehouse == null)
            {
                continue;
            }

            if (!first)
            {
                text.Append('，');
            }

            first = false;
            text.Append(warehouse.WarehouseId);
            if (!string.IsNullOrWhiteSpace(warehouse.Name))
            {
                text.Append(' ');
                text.Append(warehouse.Name);
            }
        }

        return first ? baseText : text.ToString();
    }

    private static int DirectionIndex(SimFlowDirection direction)
    {
        return direction == SimFlowDirection.Outbound ? 1 : 0;
    }

    private static int IndexOf(string[] labels, string value)
    {
        for (var i = 0; i < labels.Length; i++)
        {
            if (labels[i] == value)
            {
                return i;
            }
        }

        return -1;
    }

    private static string FormatTags(string[] tags)
    {
        if (tags == null || tags.Length == 0)
        {
            return string.Empty;
        }

        return string.Join(", ", tags);
    }

    private static string[] ParseTags(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string>();
        }

        var parts = text.Split(new[] { ',', '，', ';', '；' }, StringSplitOptions.RemoveEmptyEntries);
        var list = new List<string>(parts.Length);
        for (var i = 0; i < parts.Length; i++)
        {
            var tag = parts[i].Trim();
            if (tag.Length > 0)
            {
                list.Add(tag);
            }
        }

        return list.ToArray();
    }
}
}
