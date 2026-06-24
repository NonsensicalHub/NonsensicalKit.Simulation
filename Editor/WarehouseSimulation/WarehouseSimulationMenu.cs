#if UNITY_EDITOR

using UnityEditor;
using UnityEngine;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Editor
{
    public static class WarehouseSimulationMenu
    {
        private const string Root = "Assets/Resources/WarehouseSimulation";

        [MenuItem("Assets/Create/Warehouse Simulation/默认配置包", priority = 0)]
        public static void CreateDefaultPack()
        {
            EnsureFolder(Root);

            var conveyorMap = CreateOrLoad<WarehouseConveyorMap>($"{Root}/WarehouseConveyorMap_Default.asset");
            EnsureDefaultConveyorMap(conveyorMap);

            var bindings = CreateOrLoad<DefaultWarehouseSimulationBindingsAsset>($"{Root}/WarehouseBindings.asset");
            bindings.ConveyorMap = conveyorMap;
            bindings.Fleet.StackerCount = 3;
            bindings.ResourcePolicy.MaxInfeedReservationsPerPort = 2;

            var strategy = CreateOrLoad<WarehouseSimStrategyProfile>($"{Root}/DefaultStrategy.asset");
            EditorUtility.SetDirty(conveyorMap);
            EditorUtility.SetDirty(bindings);
            EditorUtility.SetDirty(strategy);

            CreateScenario($"{Root}/Scenario_100Inbound.asset", bindings, strategy, 100);
            CreateScenario($"{Root}/Scenario_10000Inbound.asset", bindings, strategy, 10000);
            CreateMixedFlowScenario($"{Root}/Scenario_MixedFlow.asset", bindings, strategy);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            ConveyorMapEditorWindow.Open(conveyorMap);
            Debug.Log($"[WarehouseSimulation] 已创建默认配置：{Root}");
        }

        private static void CreateScenario(
            string path,
            WarehouseSimulationBindingsAsset bindings,
            WarehouseSimStrategyProfile strategy,
            int count)
        {
            var scenario = CreateOrLoad<WarehouseSimScenario>(path);
            scenario.Hardware = bindings;
            scenario.Strategy = strategy;
            scenario.FlowPlan = new[] { SimFlowPlanDefaults.InstantInbound(count) };
            scenario.InitialOccupancyRatio = 0f;
            scenario.InitialOccupancyRandom = false;
            EditorUtility.SetDirty(scenario);
        }

        private static void CreateMixedFlowScenario(
            string path,
            WarehouseSimulationBindingsAsset bindings,
            WarehouseSimStrategyProfile strategy)
        {
            var scenario = CreateOrLoad<WarehouseSimScenario>(path);
            scenario.Hardware = bindings;
            scenario.Strategy = strategy;
            scenario.InitialOccupancyRatio = 0.5f;
            scenario.InitialOccupancyRandom = true;
            scenario.FlowPlan = new[]
            {
                new SimFlowPlanEntry
                {
                    Direction = SimFlowDirection.Inbound,
                    Quantity = 50,
                    ScheduleMode = SimFlowScheduleMode.Staggered,
                    RandomIntervalMinSeconds = 15f,
                    RandomIntervalMaxSeconds = 15f,
                    RandomQuantityMin = 5,
                    RandomQuantityMax = 5,
                },
                new SimFlowPlanEntry
                {
                    Direction = SimFlowDirection.Outbound,
                    Quantity = 30,
                    StartDelaySeconds = 30f,
                    ScheduleMode = SimFlowScheduleMode.Staggered,
                    RandomIntervalMinSeconds = 8f,
                    RandomIntervalMaxSeconds = 20f,
                    RandomQuantityMin = 1,
                    RandomQuantityMax = 3,
                },
            };
            EditorUtility.SetDirty(scenario);
        }

        private static T CreateOrLoad<T>(string path) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null)
            {
                return existing;
            }

            var asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static void EnsureDefaultConveyorMap(WarehouseConveyorMap map)
        {
            if (map.Nodes != null && map.Nodes.Length > 0)
            {
                return;
            }

            const float segDistance = 4f;
            map.DefaultSpeedMetersPerSecond = 0.6f;
            map.CargoUnitLengthMeters = 1.2f;
            map.DefaultEdgeDistanceMeters = segDistance;
            map.Nodes = new[]
            {
                Infeed("IN1"),
                Infeed("IN2"),
                Junction("J00"),
                Junction("J01"),
                Junction("J10"),
                Junction("J11"),
                Pickup("P1", stacker: 0, column: 2, row: 0),
                Pickup("P2", stacker: 0, column: 3, row: 0),
                Pickup("P3", stacker: 1, column: 6, row: 0),
                Pickup("P4", stacker: 1, column: 7, row: 0),
                Pickup("P5", stacker: 2, column: 10, row: 0),
                Pickup("P6", stacker: 2, column: 11, row: 0),
                Outfeed("OUT1"),
                Outfeed("OUT2"),
            };

            var nodeIds = BuildNodeIdMap(map.Nodes);
            map.Edges = ConcatEdges(
                BidirectionalEdge(nodeIds, "J00", "J01", segDistance),
                BidirectionalEdge(nodeIds, "J10", "J11", segDistance),
                BidirectionalEdge(nodeIds, "J00", "J10", segDistance),
                BidirectionalEdge(nodeIds, "J01", "J11", segDistance),
                OneWayEdge(nodeIds, "IN1", "J10", segDistance),
                OneWayEdge(nodeIds, "IN2", "J11", segDistance),
                OneWayEdge(nodeIds, "J00", "OUT1", segDistance),
                OneWayEdge(nodeIds, "J01", "OUT2", segDistance),
                BidirectionalEdge(nodeIds, "J10", "P1", segDistance),
                BidirectionalEdge(nodeIds, "J10", "P2", segDistance),
                BidirectionalEdge(nodeIds, "J11", "P3", segDistance),
                BidirectionalEdge(nodeIds, "J11", "P4", segDistance),
                BidirectionalEdge(nodeIds, "J01", "P5", segDistance),
                BidirectionalEdge(nodeIds, "J01", "P6", segDistance));

            map.SetNodePosition(nodeIds["IN1"], new Vector2(0f, 360f));
            map.SetNodePosition(nodeIds["IN2"], new Vector2(200f, 360f));
            map.SetNodePosition(nodeIds["J00"], new Vector2(0f, 0f));
            map.SetNodePosition(nodeIds["J01"], new Vector2(200f, 0f));
            map.SetNodePosition(nodeIds["J10"], new Vector2(0f, 120f));
            map.SetNodePosition(nodeIds["J11"], new Vector2(200f, 120f));
            map.SetNodePosition(nodeIds["P1"], new Vector2(-40f, 220f));
            map.SetNodePosition(nodeIds["P2"], new Vector2(40f, 220f));
            map.SetNodePosition(nodeIds["P3"], new Vector2(160f, 220f));
            map.SetNodePosition(nodeIds["P4"], new Vector2(240f, 220f));
            map.SetNodePosition(nodeIds["P5"], new Vector2(160f, 100f));
            map.SetNodePosition(nodeIds["P6"], new Vector2(240f, 100f));
            map.SetNodePosition(nodeIds["OUT1"], new Vector2(0f, -80f));
            map.SetNodePosition(nodeIds["OUT2"], new Vector2(200f, -80f));
        }

        private static SimConveyorMapNode Outfeed(string logicalId) => new()
        {
            NodeId = SimEntityNaming.NewNodeGuid(),
            LogicalId = logicalId,
            Kind = SimConveyorNodeKind.OutfeedPort,
        };

        private static SimConveyorMapNode Infeed(string logicalId) => new()
        {
            NodeId = SimEntityNaming.NewNodeGuid(),
            LogicalId = logicalId,
            Kind = SimConveyorNodeKind.InfeedPort,
        };

        private static SimConveyorMapNode Junction(string logicalId) => new()
        {
            NodeId = SimEntityNaming.NewNodeGuid(),
            LogicalId = logicalId,
            Kind = SimConveyorNodeKind.Junction,
        };

        private static SimConveyorMapNode Pickup(string logicalId, int stacker, int column, int row) => new()
        {
            NodeId = SimEntityNaming.NewNodeGuid(),
            LogicalId = logicalId,
            Kind = SimConveyorNodeKind.PickupPoint,
            StackerId = stacker,
            PickupColumn = column,
            PickupRow = row,
        };

        private static System.Collections.Generic.Dictionary<string, string> BuildNodeIdMap(
            SimConveyorMapNode[] nodes)
        {
            var map = new System.Collections.Generic.Dictionary<string, string>();
            for (var i = 0; i < nodes.Length; i++)
            {
                var logicalId = nodes[i].LogicalId?.Trim();
                if (!string.IsNullOrEmpty(logicalId))
                {
                    map[logicalId] = nodes[i].NodeId;
                }
            }

            return map;
        }

        private static SimConveyorMapEdge[] OneWayEdge(
            System.Collections.Generic.Dictionary<string, string> nodeIds,
            string from,
            string to,
            float distanceMeters) => new[]
        {
            new SimConveyorMapEdge
            {
                FromNodeId = nodeIds[from],
                ToNodeId = nodeIds[to],
                DistanceMeters = distanceMeters,
            },
        };

        private static SimConveyorMapEdge[] BidirectionalEdge(
            System.Collections.Generic.Dictionary<string, string> nodeIds,
            string a,
            string b,
            float distanceMeters) => new[]
        {
            new SimConveyorMapEdge
            {
                FromNodeId = nodeIds[a],
                ToNodeId = nodeIds[b],
                DistanceMeters = distanceMeters,
            },
            new SimConveyorMapEdge
            {
                FromNodeId = nodeIds[b],
                ToNodeId = nodeIds[a],
                DistanceMeters = distanceMeters,
            },
        };

        private static SimConveyorMapEdge[] ConcatEdges(params SimConveyorMapEdge[][] groups)
        {
            var count = 0;
            for (var i = 0; i < groups.Length; i++)
            {
                count += groups[i].Length;
            }

            var result = new SimConveyorMapEdge[count];
            var offset = 0;
            for (var i = 0; i < groups.Length; i++)
            {
                groups[i].CopyTo(result, offset);
                offset += groups[i].Length;
            }

            return result;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            var parts = path.Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }
    }
}

#endif
