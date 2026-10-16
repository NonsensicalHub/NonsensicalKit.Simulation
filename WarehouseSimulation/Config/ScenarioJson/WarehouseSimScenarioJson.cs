using System;
using System.Globalization;
using System.Text;
using NonsensicalKit.Tools;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using UnityEngine;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Config
{
    /// <summary>Scenario ↔ JSON DTO 互转，以及从 Resources 解析硬件/策略引用。</summary>
    public static class WarehouseSimScenarioJson
    {
        public const string CurrentSchemaVersion = "1";
        public const string DefaultHardwareResourcesPath = "WarehouseSimulation/WarehouseBindings";
        public const string DefaultStrategyResourcesPath = "WarehouseSimulation/DefaultStrategy";

        public static string ToJson(WarehouseSimScenarioDto dto, bool indented = true)
        {
            if (dto == null)
            {
                throw new ArgumentNullException(nameof(dto));
            }

            if (string.IsNullOrWhiteSpace(dto.SchemaVersion))
            {
                dto.SchemaVersion = CurrentSchemaVersion;
            }

            var json = JsonTool.SerializeObject(dto);
            return indented ? PrettyPrintJson(json) : json;
        }

        /// <summary>不依赖 Newtonsoft 程序集引用的简易缩进（仅服务可读性）。</summary>
        private static string PrettyPrintJson(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return json;
            }

            var sb = new StringBuilder(json.Length + json.Length / 4);
            var indent = 0;
            var inString = false;
            var escape = false;
            for (var i = 0; i < json.Length; i++)
            {
                var ch = json[i];
                if (inString)
                {
                    sb.Append(ch);
                    if (escape)
                    {
                        escape = false;
                    }
                    else if (ch == '\\')
                    {
                        escape = true;
                    }
                    else if (ch == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                switch (ch)
                {
                    case '"':
                        inString = true;
                        sb.Append(ch);
                        break;
                    case '{':
                    case '[':
                        sb.Append(ch);
                        sb.Append('\n');
                        indent++;
                        sb.Append(' ', indent * 2);
                        break;
                    case '}':
                    case ']':
                        sb.Append('\n');
                        indent = Math.Max(0, indent - 1);
                        sb.Append(' ', indent * 2);
                        sb.Append(ch);
                        break;
                    case ',':
                        sb.Append(ch);
                        sb.Append('\n');
                        sb.Append(' ', indent * 2);
                        break;
                    case ':':
                        sb.Append(": ");
                        break;
                    default:
                        if (!char.IsWhiteSpace(ch))
                        {
                            sb.Append(ch);
                        }

                        break;
                }
            }

            return sb.ToString();
        }

        public static WarehouseSimScenarioDto FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new ArgumentException("JSON 为空。", nameof(json));
            }

            var dto = JsonTool.DeserializeObject<WarehouseSimScenarioDto>(json);
            if (dto == null)
            {
                throw new InvalidOperationException("无法解析任务配置 JSON。");
            }

            Normalize(dto);
            return dto;
        }

        public static WarehouseSimScenarioDto FromScenario(
            WarehouseSimScenario scenario,
            string id = null,
            string hardwareResourcesPath = null,
            string strategyResourcesPath = null)
        {
            if (scenario == null)
            {
                throw new ArgumentNullException(nameof(scenario));
            }

            var dto = new WarehouseSimScenarioDto
            {
                SchemaVersion = CurrentSchemaVersion,
                Id = string.IsNullOrWhiteSpace(id) ? SanitizeId(scenario.name) : SanitizeId(id),
                DisplayName = string.IsNullOrWhiteSpace(scenario.name) ? id : scenario.name,
                HardwareResourcesPath = string.IsNullOrWhiteSpace(hardwareResourcesPath)
                    ? DefaultHardwareResourcesPath
                    : hardwareResourcesPath.Trim(),
                StrategyResourcesPath = strategyResourcesPath,
                FlowRandomSeed = scenario.FlowRandomSeed,
                InitialOccupancyRatio = scenario.InitialOccupancyRatio,
                InitialOccupancyRandom = scenario.InitialOccupancyRandom,
                Strategy = FromStrategy(scenario.ResolvedStrategy),
                FlowPlan = FromFlowPlan(scenario.FlowPlan),
            };
            Normalize(dto);
            return dto;
        }

        public static WarehouseSimScenarioDto CreateBlank(
            string id = null,
            string hardwareResourcesPath = null)
        {
            var safeId = SanitizeId(string.IsNullOrWhiteSpace(id) ? "NewScenario" : id);
            var dto = new WarehouseSimScenarioDto
            {
                SchemaVersion = CurrentSchemaVersion,
                Id = safeId,
                DisplayName = safeId,
                HardwareResourcesPath = string.IsNullOrWhiteSpace(hardwareResourcesPath)
                    ? DefaultHardwareResourcesPath
                    : hardwareResourcesPath.Trim(),
                StrategyResourcesPath = null,
                Strategy = new WarehouseSimStrategyDto(),
                FlowRandomSeed = 42,
                InitialOccupancyRatio = 0f,
                InitialOccupancyRandom = false,
                FlowPlan = new[]
                {
                    new SimFlowPlanEntryDto
                    {
                        Direction = nameof(SimFlowDirection.Inbound),
                        Quantity = 100,
                        ScheduleMode = nameof(SimFlowScheduleMode.Instant),
                    },
                },
            };
            Normalize(dto);
            return dto;
        }

        /// <summary>
        /// 从 DTO 构建可跑的运行时 Scenario（HideAndDontSave）。
        /// 硬件必须能从 Resources 加载；策略优先内联，失败则用默认。
        /// </summary>
        public static WarehouseSimScenario ToRuntimeScenario(
            WarehouseSimScenarioDto dto,
            out string error)
        {
            error = null;
            if (dto == null)
            {
                error = "DTO 为空。";
                return null;
            }

            Normalize(dto);

            if (!TryResolveHardware(dto.HardwareResourcesPath, out var hardware, out var hwError))
            {
                error = hwError;
                return null;
            }

            var scenario = ScriptableObject.CreateInstance<WarehouseSimScenario>();
            scenario.hideFlags = HideFlags.HideAndDontSave;
            scenario.name = string.IsNullOrWhiteSpace(dto.DisplayName) ? dto.Id : dto.DisplayName;
            scenario.Hardware = hardware;
            scenario.Strategy = BuildStrategy(dto);
            scenario.FlowPlan = ToFlowPlan(dto.FlowPlan);
            scenario.FlowRandomSeed = dto.FlowRandomSeed;
            scenario.InitialOccupancyRatio = Mathf.Clamp01(dto.InitialOccupancyRatio);
            scenario.InitialOccupancyRandom = dto.InitialOccupancyRandom;
            return scenario;
        }

        public static bool TryResolveHardware(
            string resourcesPath,
            out WarehouseSimulationBindingsAsset hardware,
            out string error)
        {
            hardware = null;
            error = null;
            var path = string.IsNullOrWhiteSpace(resourcesPath)
                ? DefaultHardwareResourcesPath
                : resourcesPath.Trim();

            hardware = Resources.Load<WarehouseSimulationBindingsAsset>(path);
            if (hardware == null)
            {
                error =
                    $"找不到硬件绑定 Resources「{path}」。请把绑定资产放在 Assets/Resources/{path}.asset，" +
                    "或在 JSON 里改 HardwareResourcesPath。";
                return false;
            }

            return true;
        }

        public static string SanitizeId(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return "Scenario";
            }

            var sb = new StringBuilder(raw.Length);
            foreach (var ch in raw.Trim())
            {
                if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' || ch == '.')
                {
                    sb.Append(ch);
                }
                else if (char.IsWhiteSpace(ch) || ch == '/' || ch == '\\')
                {
                    sb.Append('_');
                }
            }

            var id = sb.ToString().Trim('_');
            return string.IsNullOrEmpty(id) ? "Scenario" : id;
        }

        public static void Normalize(WarehouseSimScenarioDto dto)
        {
            if (dto == null)
            {
                return;
            }

            dto.SchemaVersion = string.IsNullOrWhiteSpace(dto.SchemaVersion)
                ? CurrentSchemaVersion
                : dto.SchemaVersion.Trim();
            dto.Id = SanitizeId(dto.Id);
            if (string.IsNullOrWhiteSpace(dto.DisplayName))
            {
                dto.DisplayName = dto.Id;
            }

            if (string.IsNullOrWhiteSpace(dto.HardwareResourcesPath))
            {
                dto.HardwareResourcesPath = DefaultHardwareResourcesPath;
            }
            else
            {
                dto.HardwareResourcesPath = dto.HardwareResourcesPath.Trim().Replace('\\', '/');
            }

            if (!string.IsNullOrWhiteSpace(dto.StrategyResourcesPath))
            {
                dto.StrategyResourcesPath = dto.StrategyResourcesPath.Trim().Replace('\\', '/');
            }

            dto.Strategy ??= new WarehouseSimStrategyDto();
            if (dto.FlowPlan == null || dto.FlowPlan.Length == 0)
            {
                dto.FlowPlan = new[]
                {
                    new SimFlowPlanEntryDto
                    {
                        Direction = nameof(SimFlowDirection.Inbound),
                        Quantity = 100,
                        ScheduleMode = nameof(SimFlowScheduleMode.Instant),
                    },
                };
            }
        }

        private static WarehouseSimStrategyDto FromStrategy(WarehouseSimStrategyProfile profile)
        {
            profile ??= SimStrategyDefaults.Instance;
            return new WarehouseSimStrategyDto
            {
                StackerPlacementStrategy = profile.StackerPlacementStrategy.ToString(),
                InfeedPortSelectionStrategy = profile.InfeedPortSelectionStrategy.ToString(),
                ConveyorRoutingStrategy = profile.ConveyorRoutingStrategy.ToString(),
                RouteDistanceWeight = profile.RouteDistanceWeight,
                RouteCongestedCargoWeight = profile.RouteCongestedCargoWeight,
                RouteTotalCargoWeight = profile.RouteTotalCargoWeight,
            };
        }

        private static WarehouseSimStrategyProfile BuildStrategy(WarehouseSimScenarioDto dto)
        {
            WarehouseSimStrategyProfile profile = null;
            if (!string.IsNullOrWhiteSpace(dto.StrategyResourcesPath))
            {
                var loaded = Resources.Load<WarehouseSimStrategyProfile>(dto.StrategyResourcesPath);
                if (loaded != null)
                {
                    profile = ScriptableObject.Instantiate(loaded);
                    profile.hideFlags = HideFlags.HideAndDontSave;
                }
            }

            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<WarehouseSimStrategyProfile>();
                profile.hideFlags = HideFlags.HideAndDontSave;
            }

            var s = dto.Strategy ?? new WarehouseSimStrategyDto();
            profile.StackerPlacementStrategy = ParseEnum(s.StackerPlacementStrategy, StackerSlotPlacementStrategy.NearestToPickup);
            profile.InfeedPortSelectionStrategy = ParseEnum(s.InfeedPortSelectionStrategy, InfeedPortSelectionStrategy.RoundRobin);
            profile.ConveyorRoutingStrategy = ParseEnum(s.ConveyorRoutingStrategy, ConveyorRoutingStrategy.ShortestDistance);
            profile.RouteDistanceWeight = s.RouteDistanceWeight;
            profile.RouteCongestedCargoWeight = s.RouteCongestedCargoWeight;
            profile.RouteTotalCargoWeight = s.RouteTotalCargoWeight;
            return profile;
        }

        private static SimFlowPlanEntryDto[] FromFlowPlan(SimFlowPlanEntry[] plan)
        {
            if (plan == null || plan.Length == 0)
            {
                return new[]
                {
                    new SimFlowPlanEntryDto
                    {
                        Direction = nameof(SimFlowDirection.Inbound),
                        Quantity = 100,
                        ScheduleMode = nameof(SimFlowScheduleMode.Instant),
                    },
                };
            }

            var result = new SimFlowPlanEntryDto[plan.Length];
            for (var i = 0; i < plan.Length; i++)
            {
                var e = plan[i] ?? new SimFlowPlanEntry();
                result[i] = new SimFlowPlanEntryDto
                {
                    Direction = e.Direction.ToString(),
                    Quantity = Math.Max(1, e.Quantity),
                    StartDelaySeconds = Math.Max(0f, e.StartDelaySeconds),
                    ScheduleMode = e.ScheduleMode.ToString(),
                    RandomQuantityMin = Math.Max(1, e.RandomQuantityMin),
                    RandomQuantityMax = Math.Max(1, e.RandomQuantityMax),
                    RandomIntervalMinSeconds = Math.Max(0.01f, e.RandomIntervalMinSeconds),
                    RandomIntervalMaxSeconds = Math.Max(0.01f, e.RandomIntervalMaxSeconds),
                    RequiredProcessTags = e.RequiredProcessTags,
                };
            }

            return result;
        }

        /// <summary>自测/预览：DTO 流程 → 运行时流程（含枚举解析）。</summary>
        public static SimFlowPlanEntry[] ConvertFlowPlan(SimFlowPlanEntryDto[] plan) => ToFlowPlan(plan);

        private static SimFlowPlanEntry[] ToFlowPlan(SimFlowPlanEntryDto[] plan)
        {
            if (plan == null || plan.Length == 0)
            {
                return SimFlowPlanDefaults.DefaultWhenEmpty;
            }

            var result = new SimFlowPlanEntry[plan.Length];
            for (var i = 0; i < plan.Length; i++)
            {
                var e = plan[i] ?? new SimFlowPlanEntryDto();
                result[i] = new SimFlowPlanEntry
                {
                    Direction = ParseEnum(e.Direction, SimFlowDirection.Inbound),
                    Quantity = Math.Max(1, e.Quantity),
                    StartDelaySeconds = Math.Max(0f, e.StartDelaySeconds),
                    ScheduleMode = ParseEnum(e.ScheduleMode, SimFlowScheduleMode.Instant),
                    RandomQuantityMin = Math.Max(1, e.RandomQuantityMin),
                    RandomQuantityMax = Math.Max(1, e.RandomQuantityMax),
                    RandomIntervalMinSeconds = Math.Max(0.01f, e.RandomIntervalMinSeconds),
                    RandomIntervalMaxSeconds = Math.Max(0.01f, e.RandomIntervalMaxSeconds),
                    RequiredProcessTags = e.RequiredProcessTags,
                };
            }

            return result;
        }

        private static TEnum ParseEnum<TEnum>(string raw, TEnum fallback) where TEnum : struct
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return fallback;
            }

            if (Enum.TryParse(raw.Trim(), ignoreCase: true, out TEnum value))
            {
                return value;
            }

            if (int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var asInt)
                && Enum.IsDefined(typeof(TEnum), asInt))
            {
                return (TEnum)(object)asInt;
            }

            return fallback;
        }
    }
}