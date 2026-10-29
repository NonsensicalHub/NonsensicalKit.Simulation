using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Runtime
{
/// <summary>
/// 多套任务配置档。写在 persistentDataPath，避免运行中改 StreamingAssets 触发资源重导入。
/// 每一档保存流程计划、初始占用和策略，切换时整套写回当前 Scenario。
/// </summary>
public sealed class WarehouseSimTaskPresetStore
{
    private readonly List<WarehouseSimTaskPreset> _presets = new();
    private bool _dirty;

    public int Count => _presets.Count;

    public int ActiveIndex { get; private set; }

    public string ActiveName => Count == 0 ? string.Empty : _presets[ActiveIndex].Name;

    /// <summary>Web / PC 通用目录下的配置档清单（与包内 WarehouseSimScenarioStore 同根）。</summary>
    public static string FilePath =>
        Path.Combine(Application.persistentDataPath, "WarehouseSim", "TaskPresets.json");

    /// <summary>旧版写在工程根目录的路径，首次启动会自动迁移。</summary>
    public static string LegacyFilePath
    {
        get
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            return Path.Combine(projectRoot, "WarehouseSimTaskPresets.json");
        }
    }

    private string _hardwareResourcesPath = WarehouseSimScenarioJson.DefaultHardwareResourcesPath;

    public static WarehouseSimTaskPresetStore LoadOrCreate(
        WarehouseSimScenario current,
        string hardwareResourcesPath = null)
    {
        var store = new WarehouseSimTaskPresetStore();
        if (!string.IsNullOrWhiteSpace(hardwareResourcesPath))
        {
            store._hardwareResourcesPath = hardwareResourcesPath.Trim().Replace('\\', '/');
        }

        if (!store.TryLoad() || store.Count == 0)
        {
            store._presets.Clear();
            if (!store.TryImportFromPackageStore())
            {
                store.Seed(current);
            }

            store._dirty = true;
            store.Flush();
        }

        return store;
    }

    public void Apply(WarehouseSimScenario scenario)
    {
        if (scenario == null || Count == 0)
        {
            return;
        }

        var preset = _presets[ActiveIndex];
        scenario.FlowRandomSeed = preset.FlowRandomSeed;
        scenario.InitialOccupancyRatio = Mathf.Clamp01(preset.InitialOccupancyRatio);
        scenario.InitialOccupancyRandom = preset.InitialOccupancyRandom;
        scenario.FlowPlan = ClonePlan(preset.FlowPlan);

        var strategy = scenario.ResolvedStrategy;
        if (strategy == null)
        {
            return;
        }

        strategy.StackerPlacementStrategy = EnumOr(
            preset.Placement,
            StackerSlotPlacementStrategy.NearestToPickup);
        strategy.InfeedPortSelectionStrategy = EnumOr(
            preset.Port,
            InfeedPortSelectionStrategy.RoundRobin);
        strategy.ConveyorRoutingStrategy = EnumOr(
            preset.Routing,
            ConveyorRoutingStrategy.ShortestDistance);
        strategy.RouteDistanceWeight = Mathf.Max(0f, preset.RouteDistanceWeight);
        strategy.RouteCongestedCargoWeight = Mathf.Max(0f, preset.RouteCongestedCargoWeight);
        strategy.RouteTotalCargoWeight = Mathf.Max(0f, preset.RouteTotalCargoWeight);
    }

    public void Capture(WarehouseSimScenario scenario)
    {
        if (scenario == null || Count == 0)
        {
            return;
        }

        var next = FromScenario(scenario);
        next.Name = _presets[ActiveIndex].Name;
        if (SameContent(_presets[ActiveIndex], next))
        {
            return;
        }

        _presets[ActiveIndex] = next;
        _dirty = true;
    }

    public void RenameActive(string name)
    {
        if (Count == 0)
        {
            return;
        }

        var unique = MakeUnique(string.IsNullOrWhiteSpace(name) ? "未命名" : name.Trim(), ActiveIndex);
        if (_presets[ActiveIndex].Name == unique)
        {
            return;
        }

        _presets[ActiveIndex].Name = unique;
        _dirty = true;
    }

    public void DuplicateActive(string preferredName)
    {
        if (Count == 0)
        {
            return;
        }

        var copy = ClonePreset(_presets[ActiveIndex]);
        var baseName = string.IsNullOrWhiteSpace(preferredName) ? copy.Name + " 副本" : preferredName.Trim();
        copy.Name = MakeUnique(baseName, -1);
        var insertAt = ActiveIndex + 1;
        _presets.Insert(insertAt, copy);
        ActiveIndex = insertAt;
        _dirty = true;
    }

    /// <summary>新建一档空白任务（默认 100 箱瞬间入库），并设为当前档。</summary>
    public void CreateBlank(string preferredName = null)
    {
        var preset = FromScenario(null);
        var baseName = string.IsNullOrWhiteSpace(preferredName) ? "新建配置" : preferredName.Trim();
        preset.Name = MakeUnique(baseName, -1);
        var insertAt = Count == 0 ? 0 : ActiveIndex + 1;
        _presets.Insert(insertAt, preset);
        ActiveIndex = insertAt;
        _dirty = true;
    }

    public bool DeleteActive()
    {
        if (Count <= 1)
        {
            return false;
        }

        _presets.RemoveAt(ActiveIndex);
        if (ActiveIndex >= Count)
        {
            ActiveIndex = Count - 1;
        }

        _dirty = true;
        return true;
    }

    public bool TrySetActive(int index)
    {
        if (index < 0 || index >= Count || index == ActiveIndex)
        {
            return false;
        }

        ActiveIndex = index;
        _dirty = true;
        return true;
    }

    public void CopyNames(List<string> destination)
    {
        destination.Clear();
        for (var i = 0; i < _presets.Count; i++)
        {
            destination.Add(_presets[i].Name);
        }
    }

    public void Flush()
    {
        if (!_dirty)
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var file = new WarehouseSimTaskPresetFile
            {
                ActiveIndex = ActiveIndex,
                Presets = _presets.ToArray(),
            };
            File.WriteAllText(FilePath, JsonUtility.ToJson(file, true));
            SyncToPackageStore();
            _dirty = false;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[WarehouseSimulation] 配置档保存失败：{exception.Message}");
        }
    }

    private bool TryLoad()
    {
        TryMigrateLegacyFile();
        if (!File.Exists(FilePath))
        {
            return false;
        }

        try
        {
            var file = JsonUtility.FromJson<WarehouseSimTaskPresetFile>(File.ReadAllText(FilePath));
            if (file?.Presets == null || file.Presets.Length == 0)
            {
                return false;
            }

            _presets.Clear();
            for (var i = 0; i < file.Presets.Length; i++)
            {
                var preset = file.Presets[i];
                if (preset == null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(preset.Name))
                {
                    preset.Name = "未命名";
                }

                if (preset.FlowPlan == null || preset.FlowPlan.Length == 0)
                {
                    preset.FlowPlan = new[] { SimFlowPlanDefaults.InstantInbound(100) };
                }

                preset.Name = MakeUnique(preset.Name, -1);
                _presets.Add(preset);
            }

            if (Count == 0)
            {
                return false;
            }

            ActiveIndex = Mathf.Clamp(file.ActiveIndex, 0, Count - 1);
            _dirty = false;
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[WarehouseSimulation] 配置档读取失败，将按场景任务重新建立：{exception.Message}");
            return false;
        }
    }

    private void Seed(WarehouseSimScenario current)
    {
        var assets = Resources.LoadAll<WarehouseSimScenario>("WarehouseSimulation");
        Array.Sort(assets, CompareSeedOrder);
        for (var i = 0; i < assets.Length; i++)
        {
            var preset = FromScenario(assets[i]);
            preset.Name = MakeUnique(FriendlyName(assets[i]), -1);
            _presets.Add(preset);
        }

        InsertInstantInbound(1000, "1000入库");

        if (current == null)
        {
            if (Count == 0)
            {
                var fallback = FromScenario(null);
                fallback.Name = "100入库";
                _presets.Add(fallback);
            }

            ActiveIndex = 0;
            return;
        }

        var sample = FromScenario(current);
        var match = FindSameContent(sample);
        if (match < 0)
        {
            sample.Name = MakeUnique(FriendlyName(current), -1);
            _presets.Add(sample);
            match = Count - 1;
        }

        ActiveIndex = match;
    }

    private void InsertInstantInbound(int quantity, string name)
    {
        for (var i = 0; i < _presets.Count; i++)
        {
            if (IsInstantInbound(_presets[i], quantity))
            {
                return;
            }
        }

        WarehouseSimTaskPreset sample = null;
        var insertAt = _presets.Count;
        for (var i = 0; i < _presets.Count; i++)
        {
            if (IsInstantInbound(_presets[i], 100))
            {
                sample = ClonePreset(_presets[i]);
                insertAt = i + 1;
                break;
            }
        }

        if (sample == null)
        {
            sample = FromScenario(null);
        }

        sample.FlowPlan = new[] { SimFlowPlanDefaults.InstantInbound(quantity) };
        sample.Name = MakeUnique(name, -1);
        _presets.Insert(insertAt, sample);
    }

    private static bool IsInstantInbound(WarehouseSimTaskPreset preset, int quantity)
    {
        var plan = preset?.FlowPlan;
        if (plan == null || plan.Length != 1 || plan[0] == null)
        {
            return false;
        }

        var entry = plan[0];
        return entry.Direction == SimFlowDirection.Inbound
            && entry.ScheduleMode == SimFlowScheduleMode.Instant
            && entry.Quantity == quantity;
    }

    private static int CompareSeedOrder(WarehouseSimScenario left, WarehouseSimScenario right)
    {
        var rank = SeedRank(left).CompareTo(SeedRank(right));
        if (rank != 0)
        {
            return rank;
        }

        return string.Compare(left != null ? left.name : string.Empty, right != null ? right.name : string.Empty, StringComparison.Ordinal);
    }

    private static int SeedRank(WarehouseSimScenario scenario)
    {
        var name = scenario != null ? scenario.name : string.Empty;
        if (name.IndexOf("Mixed", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return 1_000_000;
        }

        if (scenario?.FlowPlan != null && scenario.FlowPlan.Length > 0 && scenario.FlowPlan[0] != null)
        {
            return scenario.FlowPlan[0].Quantity;
        }

        return 0;
    }

    private int FindSameContent(WarehouseSimTaskPreset sample)
    {
        for (var i = 0; i < _presets.Count; i++)
        {
            if (SameContent(_presets[i], sample))
            {
                return i;
            }
        }

        return -1;
    }

    private string MakeUnique(string desired, int ignoreIndex)
    {
        if (string.IsNullOrWhiteSpace(desired))
        {
            desired = "未命名";
        }

        desired = desired.Trim();
        if (!NameTaken(desired, ignoreIndex))
        {
            return desired;
        }

        for (var suffix = 2; ; suffix++)
        {
            var candidate = desired + " " + suffix;
            if (!NameTaken(candidate, ignoreIndex))
            {
                return candidate;
            }
        }
    }

    private bool NameTaken(string name, int ignoreIndex)
    {
        for (var i = 0; i < _presets.Count; i++)
        {
            if (i != ignoreIndex && _presets[i].Name == name)
            {
                return true;
            }
        }

        return false;
    }

    private static WarehouseSimTaskPreset FromScenario(WarehouseSimScenario scenario)
    {
        var preset = new WarehouseSimTaskPreset
        {
            Name = "未命名",
            FlowRandomSeed = 42,
            FlowPlan = new[] { SimFlowPlanDefaults.InstantInbound(100) },
            Placement = (int)StackerSlotPlacementStrategy.NearestToPickup,
            Port = (int)InfeedPortSelectionStrategy.RoundRobin,
            Routing = (int)ConveyorRoutingStrategy.ShortestDistance,
            RouteDistanceWeight = 1f,
            RouteCongestedCargoWeight = 5f,
            RouteTotalCargoWeight = 1f,
        };

        if (scenario == null)
        {
            return preset;
        }

        preset.FlowRandomSeed = scenario.FlowRandomSeed;
        preset.InitialOccupancyRatio = scenario.InitialOccupancyRatio;
        preset.InitialOccupancyRandom = scenario.InitialOccupancyRandom;
        preset.FlowPlan = ClonePlan(scenario.FlowPlan);

        var strategy = scenario.ResolvedStrategy;
        if (strategy == null)
        {
            return preset;
        }

        preset.Placement = (int)strategy.StackerPlacementStrategy;
        preset.Port = (int)strategy.InfeedPortSelectionStrategy;
        preset.Routing = (int)strategy.ConveyorRoutingStrategy;
        preset.RouteDistanceWeight = strategy.RouteDistanceWeight;
        preset.RouteCongestedCargoWeight = strategy.RouteCongestedCargoWeight;
        preset.RouteTotalCargoWeight = strategy.RouteTotalCargoWeight;
        return preset;
    }

    private static WarehouseSimTaskPreset ClonePreset(WarehouseSimTaskPreset source)
    {
        return new WarehouseSimTaskPreset
        {
            Name = source.Name,
            FlowRandomSeed = source.FlowRandomSeed,
            InitialOccupancyRatio = source.InitialOccupancyRatio,
            InitialOccupancyRandom = source.InitialOccupancyRandom,
            Placement = source.Placement,
            Port = source.Port,
            Routing = source.Routing,
            RouteDistanceWeight = source.RouteDistanceWeight,
            RouteCongestedCargoWeight = source.RouteCongestedCargoWeight,
            RouteTotalCargoWeight = source.RouteTotalCargoWeight,
            FlowPlan = ClonePlan(source.FlowPlan),
        };
    }

    private static bool SameContent(WarehouseSimTaskPreset left, WarehouseSimTaskPreset right)
    {
        if (left == null || right == null)
        {
            return false;
        }

        if (left.FlowRandomSeed != right.FlowRandomSeed
            || left.InitialOccupancyRandom != right.InitialOccupancyRandom
            || left.Placement != right.Placement
            || left.Port != right.Port
            || left.Routing != right.Routing
            || !Nearly(left.InitialOccupancyRatio, right.InitialOccupancyRatio)
            || !Nearly(left.RouteDistanceWeight, right.RouteDistanceWeight)
            || !Nearly(left.RouteCongestedCargoWeight, right.RouteCongestedCargoWeight)
            || !Nearly(left.RouteTotalCargoWeight, right.RouteTotalCargoWeight))
        {
            return false;
        }

        var leftPlan = left.FlowPlan;
        var rightPlan = right.FlowPlan;
        var leftCount = leftPlan != null ? leftPlan.Length : 0;
        var rightCount = rightPlan != null ? rightPlan.Length : 0;
        if (leftCount != rightCount)
        {
            return false;
        }

        for (var i = 0; i < leftCount; i++)
        {
            if (!SameEntry(leftPlan[i], rightPlan[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SameEntry(SimFlowPlanEntry left, SimFlowPlanEntry right)
    {
        if (left == null || right == null)
        {
            return left == right;
        }

        if (left.Direction != right.Direction
            || left.Quantity != right.Quantity
            || left.ScheduleMode != right.ScheduleMode
            || left.RandomQuantityMin != right.RandomQuantityMin
            || left.RandomQuantityMax != right.RandomQuantityMax
            || !Nearly(left.StartDelaySeconds, right.StartDelaySeconds)
            || !Nearly(left.RandomIntervalMinSeconds, right.RandomIntervalMinSeconds)
            || !Nearly(left.RandomIntervalMaxSeconds, right.RandomIntervalMaxSeconds))
        {
            return false;
        }

        var leftTags = left.RequiredProcessTags;
        var rightTags = right.RequiredProcessTags;
        var leftCount = leftTags != null ? leftTags.Length : 0;
        var rightCount = rightTags != null ? rightTags.Length : 0;
        if (leftCount != rightCount)
        {
            return false;
        }

        for (var i = 0; i < leftCount; i++)
        {
            if (leftTags[i] != rightTags[i])
            {
                return false;
            }
        }

        return SimWarehouseRestriction.Same(left.AllowedWarehouseIds, right.AllowedWarehouseIds);
    }

    private static SimFlowPlanEntry[] ClonePlan(SimFlowPlanEntry[] plan)
    {
        if (plan == null || plan.Length == 0)
        {
            return new[] { SimFlowPlanDefaults.InstantInbound(100) };
        }

        var clone = new SimFlowPlanEntry[plan.Length];
        for (var i = 0; i < plan.Length; i++)
        {
            clone[i] = CloneEntry(plan[i]);
        }

        return clone;
    }

    private static SimFlowPlanEntry CloneEntry(SimFlowPlanEntry entry)
    {
        if (entry == null)
        {
            return SimFlowPlanDefaults.InstantInbound(100);
        }

        string[] tags = null;
        if (entry.RequiredProcessTags != null)
        {
            tags = new string[entry.RequiredProcessTags.Length];
            Array.Copy(entry.RequiredProcessTags, tags, tags.Length);
        }

        return new SimFlowPlanEntry
        {
            Direction = entry.Direction,
            Quantity = entry.Quantity,
            StartDelaySeconds = entry.StartDelaySeconds,
            ScheduleMode = entry.ScheduleMode,
            RandomQuantityMin = entry.RandomQuantityMin,
            RandomQuantityMax = entry.RandomQuantityMax,
            RandomIntervalMinSeconds = entry.RandomIntervalMinSeconds,
            RandomIntervalMaxSeconds = entry.RandomIntervalMaxSeconds,
            RequiredProcessTags = tags,
            AllowedWarehouseIds = SimWarehouseRestriction.Copy(entry.AllowedWarehouseIds),
        };
    }

    private void TryMigrateLegacyFile()
    {
        try
        {
            if (File.Exists(FilePath) || !File.Exists(LegacyFilePath))
            {
                return;
            }

            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.Copy(LegacyFilePath, FilePath, overwrite: false);
            Debug.Log("[WarehouseSimulation] 已迁移配置档到 persistentDataPath。");
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[WarehouseSimulation] 配置档迁移失败：" + exception.Message);
        }
    }

    private bool TryImportFromPackageStore()
    {
        try
        {
            var ids = WarehouseSimScenarioStore.ListIds();
            if (ids == null || ids.Count == 0)
            {
                return false;
            }

            for (var i = 0; i < ids.Count; i++)
            {
                var dto = WarehouseSimScenarioStore.Load(ids[i], out _);
                if (dto == null)
                {
                    continue;
                }

                var preset = FromDto(dto);
                preset.Name = MakeUnique(string.IsNullOrWhiteSpace(dto.DisplayName) ? dto.Id : dto.DisplayName, -1);
                _presets.Add(preset);
            }

            if (Count == 0)
            {
                return false;
            }

            ActiveIndex = 0;
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[WarehouseSimulation] 从包 JSON 存档导入失败：" + exception.Message);
            return false;
        }
    }

    private void SyncToPackageStore()
    {
        try
        {
            for (var i = 0; i < _presets.Count; i++)
            {
                var dto = ToDto(_presets[i]);
                WarehouseSimScenarioStore.Save(dto, out var error);
                if (!string.IsNullOrEmpty(error))
                {
                    Debug.LogWarning("[WarehouseSimulation] 同步配置档失败：" + error);
                }
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[WarehouseSimulation] 同步包 JSON 存档失败：" + exception.Message);
        }
    }

    private WarehouseSimScenarioDto ToDto(WarehouseSimTaskPreset preset)
    {
        var id = WarehouseSimScenarioJson.SanitizeId(preset.Name);
        var dto = WarehouseSimScenarioJson.CreateBlank(id, _hardwareResourcesPath);
        dto.DisplayName = preset.Name;
        dto.FlowRandomSeed = preset.FlowRandomSeed;
        dto.InitialOccupancyRatio = preset.InitialOccupancyRatio;
        dto.InitialOccupancyRandom = preset.InitialOccupancyRandom;
        dto.Strategy = new WarehouseSimStrategyDto
        {
            StackerPlacementStrategy = EnumOr(preset.Placement, StackerSlotPlacementStrategy.NearestToPickup).ToString(),
            InfeedPortSelectionStrategy = EnumOr(preset.Port, InfeedPortSelectionStrategy.RoundRobin).ToString(),
            ConveyorRoutingStrategy = EnumOr(preset.Routing, ConveyorRoutingStrategy.ShortestDistance).ToString(),
            RouteDistanceWeight = preset.RouteDistanceWeight,
            RouteCongestedCargoWeight = preset.RouteCongestedCargoWeight,
            RouteTotalCargoWeight = preset.RouteTotalCargoWeight,
        };
        dto.FlowPlan = ToFlowDtos(preset.FlowPlan);
        WarehouseSimScenarioJson.Normalize(dto);
        return dto;
    }

    private static WarehouseSimTaskPreset FromDto(WarehouseSimScenarioDto dto)
    {
        WarehouseSimScenarioJson.Normalize(dto);
        var preset = FromScenario(null);
        preset.Name = string.IsNullOrWhiteSpace(dto.DisplayName) ? dto.Id : dto.DisplayName;
        preset.FlowRandomSeed = dto.FlowRandomSeed;
        preset.InitialOccupancyRatio = dto.InitialOccupancyRatio;
        preset.InitialOccupancyRandom = dto.InitialOccupancyRandom;
        if (dto.Strategy != null)
        {
            preset.Placement = (int)ParseEnum(dto.Strategy.StackerPlacementStrategy, StackerSlotPlacementStrategy.NearestToPickup);
            preset.Port = (int)ParseEnum(dto.Strategy.InfeedPortSelectionStrategy, InfeedPortSelectionStrategy.RoundRobin);
            preset.Routing = (int)ParseEnum(dto.Strategy.ConveyorRoutingStrategy, ConveyorRoutingStrategy.ShortestDistance);
            preset.RouteDistanceWeight = dto.Strategy.RouteDistanceWeight;
            preset.RouteCongestedCargoWeight = dto.Strategy.RouteCongestedCargoWeight;
            preset.RouteTotalCargoWeight = dto.Strategy.RouteTotalCargoWeight;
        }

        preset.FlowPlan = FromFlowDtos(dto.FlowPlan);
        return preset;
    }

    private static SimFlowPlanEntryDto[] ToFlowDtos(SimFlowPlanEntry[] plan)
    {
        plan = ClonePlan(plan);
        var result = new SimFlowPlanEntryDto[plan.Length];
        for (var i = 0; i < plan.Length; i++)
        {
            var e = plan[i];
            result[i] = new SimFlowPlanEntryDto
            {
                Direction = e.Direction.ToString(),
                Quantity = e.Quantity,
                StartDelaySeconds = e.StartDelaySeconds,
                ScheduleMode = e.ScheduleMode.ToString(),
                RandomQuantityMin = e.RandomQuantityMin,
                RandomQuantityMax = e.RandomQuantityMax,
                RandomIntervalMinSeconds = e.RandomIntervalMinSeconds,
                RandomIntervalMaxSeconds = e.RandomIntervalMaxSeconds,
                RequiredProcessTags = e.RequiredProcessTags,
                AllowedWarehouseIds = e.AllowedWarehouseIds,
            };
        }

        return result;
    }

    private static SimFlowPlanEntry[] FromFlowDtos(SimFlowPlanEntryDto[] plan)
    {
        if (plan == null || plan.Length == 0)
        {
            return new[] { SimFlowPlanDefaults.InstantInbound(100) };
        }

        return WarehouseSimScenarioJson.ConvertFlowPlan(plan);
    }

    private static T ParseEnum<T>(string raw, T fallback) where T : struct
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        return Enum.TryParse(raw.Trim(), ignoreCase: true, out T value) ? value : fallback;
    }
    private static string FriendlyName(WarehouseSimScenario scenario)
    {
        var name = scenario != null ? scenario.name : "未命名";
        const string prefix = "Scenario_";
        if (name.StartsWith(prefix, StringComparison.Ordinal))
        {
            name = name.Substring(prefix.Length);
        }

        return name
            .Replace("MixedFlow", "混合流程")
            .Replace("Inbound", "入库")
            .Replace("Outbound", "出库");
    }

    private static bool Nearly(float left, float right)
    {
        return Mathf.Abs(left - right) < 0.0001f;
    }

    private static T EnumOr<T>(int value, T fallback) where T : struct
    {
        return Enum.IsDefined(typeof(T), value) ? (T)Enum.ToObject(typeof(T), value) : fallback;
    }
}

[Serializable]
public sealed class WarehouseSimTaskPresetFile
{
    public int ActiveIndex;
    public WarehouseSimTaskPreset[] Presets;
}

[Serializable]
public sealed class WarehouseSimTaskPreset
{
    public string Name;
    public int FlowRandomSeed = 42;
    public float InitialOccupancyRatio;
    public bool InitialOccupancyRandom;
    public int Placement;
    public int Port;
    public int Routing;
    public float RouteDistanceWeight = 1f;
    public float RouteCongestedCargoWeight = 5f;
    public float RouteTotalCargoWeight = 1f;
    public SimFlowPlanEntry[] FlowPlan;
}
}
