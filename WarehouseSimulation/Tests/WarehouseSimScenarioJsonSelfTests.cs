using System;
using System.IO;
using System.Text;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using UnityEngine;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Tests
{
    /// <summary>任务配置 JSON 往返自测（不依赖磁盘上的真实硬件资产也能测序列化）。</summary>
    public static class WarehouseSimScenarioJsonSelfTests
    {
        public static string RunAll()
        {
            var sb = new StringBuilder();
            var failed = 0;
            failed += Run(sb, "RoundTrip_SerializeDeserialize", TestRoundTrip);
            failed += Run(sb, "SanitizeId_RemovesBadChars", TestSanitize);
            failed += Run(sb, "CreateBlank_HasInbound100", TestBlank);
            failed += Run(sb, "Store_SaveLoadDelete", TestStore);
            failed += Run(sb, "EnumParse_CaseInsensitive", TestEnumParseViaRoundTrip);

            if (failed == 0)
            {
                sb.AppendLine("ALL PASSED");
            }
            else
            {
                sb.AppendLine($"FAILED: {failed}");
            }

            return sb.ToString();
        }

        private static int Run(StringBuilder sb, string name, Func<string> test)
        {
            try
            {
                var err = test();
                if (string.IsNullOrEmpty(err))
                {
                    sb.AppendLine($"[PASS] {name}");
                    return 0;
                }

                sb.AppendLine($"[FAIL] {name}: {err}");
                return 1;
            }
            catch (Exception ex)
            {
                sb.AppendLine($"[FAIL] {name}: exception {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
        }

        private static string TestRoundTrip()
        {
            var dto = WarehouseSimScenarioJson.CreateBlank("RoundTrip_Test");
            dto.DisplayName = "往返测试";
            dto.FlowRandomSeed = 7;
            dto.InitialOccupancyRatio = 0.35f;
            dto.InitialOccupancyRandom = true;
            dto.Strategy.ConveyorRoutingStrategy = "LeastCongestion";
            dto.Strategy.RouteCongestedCargoWeight = 9f;
            dto.FlowPlan = new[]
            {
                new SimFlowPlanEntryDto
                {
                    Direction = "Inbound",
                    Quantity = 50,
                    ScheduleMode = "Staggered",
                    RandomIntervalMinSeconds = 2f,
                    RandomIntervalMaxSeconds = 4f,
                    RandomQuantityMin = 2,
                    RandomQuantityMax = 3,
                    RequiredProcessTags = new[] { "wrap" },
                    AllowedWarehouseIds = new[] { 1, 0, 1 },
                },
                new SimFlowPlanEntryDto
                {
                    Direction = "Outbound",
                    Quantity = 20,
                    StartDelaySeconds = 10f,
                    ScheduleMode = "Instant",
                },
            };

            var json = WarehouseSimScenarioJson.ToJson(dto);
            var back = WarehouseSimScenarioJson.FromJson(json);
            if (back.Id != "RoundTrip_Test")
            {
                return $"Id={back.Id}";
            }

            if (!string.Equals(back.DisplayName, "往返测试", StringComparison.Ordinal))
            {
                return $"DisplayName={back.DisplayName}";
            }

            if (back.FlowRandomSeed != 7 || Math.Abs(back.InitialOccupancyRatio - 0.35f) > 1e-5f || !back.InitialOccupancyRandom)
            {
                return "occupancy/seed mismatch";
            }

            if (back.FlowPlan == null || back.FlowPlan.Length != 2)
            {
                return "flow count";
            }

            if (back.FlowPlan[0].Quantity != 50 || back.FlowPlan[1].Direction != "Outbound")
            {
                return "flow content";
            }

            if (back.Strategy.RouteCongestedCargoWeight != 9f)
            {
                return "strategy weight";
            }

            if (back.FlowPlan[0].RequiredProcessTags == null
                || back.FlowPlan[0].RequiredProcessTags.Length != 1
                || back.FlowPlan[0].RequiredProcessTags[0] != "wrap")
            {
                return "tags";
            }

            if (!SimWarehouseRestriction.Same(back.FlowPlan[0].AllowedWarehouseIds, new[] { 0, 1 }))
            {
                return "warehouses";
            }

            var runtime = WarehouseSimScenarioJson.ConvertFlowPlan(back.FlowPlan);
            if (runtime == null
                || runtime.Length != 2
                || !SimWarehouseRestriction.Same(runtime[0].AllowedWarehouseIds, new[] { 0, 1 })
                || SimWarehouseRestriction.IsRestricted(runtime[1].AllowedWarehouseIds))
            {
                return "runtime warehouses";
            }

            return null;
        }

        private static string TestSanitize()
        {
            var id = WarehouseSimScenarioJson.SanitizeId("a/b\\c 测试*.json");
            if (string.IsNullOrEmpty(id) || id.Contains("/") || id.Contains("\\") || id.Contains("*"))
            {
                return id;
            }

            return null;
        }

        private static string TestBlank()
        {
            var dto = WarehouseSimScenarioJson.CreateBlank();
            if (dto.FlowPlan == null || dto.FlowPlan.Length != 1)
            {
                return "flow";
            }

            if (dto.FlowPlan[0].Quantity != 100 || dto.FlowPlan[0].Direction != "Inbound")
            {
                return "defaults";
            }

            return null;
        }

        private static string TestStore()
        {
            var id = "SelfTest_Temp_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var dto = WarehouseSimScenarioJson.CreateBlank(id);
            dto.DisplayName = "store-test";
            var path = WarehouseSimScenarioStore.Save(dto, out var saveErr);
            if (path == null)
            {
                return "save: " + saveErr;
            }

            try
            {
                if (!WarehouseSimScenarioStore.Exists(id))
                {
                    return "exists false";
                }

                var loaded = WarehouseSimScenarioStore.Load(id, out var loadErr);
                if (loaded == null)
                {
                    return "load: " + loadErr;
                }

                if (loaded.DisplayName != "store-test")
                {
                    return "display";
                }

                if (!WarehouseSimScenarioStore.Delete(id, out var delErr))
                {
                    return "delete: " + delErr;
                }

                if (WarehouseSimScenarioStore.Exists(id))
                {
                    return "still exists";
                }
            }
            finally
            {
                var leftover = WarehouseSimScenarioStore.GetFilePath(id);
                if (File.Exists(leftover))
                {
                    File.Delete(leftover);
                }
            }

            return null;
        }

        private static string TestEnumParseViaRoundTrip()
        {
            var dto = WarehouseSimScenarioJson.CreateBlank("EnumCase");
            dto.FlowPlan[0].Direction = "outbound";
            dto.FlowPlan[0].ScheduleMode = "staggered";
            var json = WarehouseSimScenarioJson.ToJson(dto);
            var back = WarehouseSimScenarioJson.FromJson(json);
            var plan = WarehouseSimScenarioJson.ConvertFlowPlan(back.FlowPlan);
            if (plan == null || plan.Length != 1)
            {
                return "plan";
            }

            if (plan[0].Direction != SimFlowDirection.Outbound)
            {
                return "direction=" + plan[0].Direction;
            }

            if (plan[0].ScheduleMode != SimFlowScheduleMode.Staggered)
            {
                return "schedule=" + plan[0].ScheduleMode;
            }

            return null;
        }
    }
}