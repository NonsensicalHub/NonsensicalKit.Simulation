using System;
using System.Collections.Generic;
using NonsensicalKit.Core.Service.Config;
using UnityEngine;
#if UNITY_EDITOR
using HikvisionAgv;
using NaughtyAttributes;
using NonsensicalKit.DigitalTwin.Motion;
using UnityEditor;
#endif


namespace Frame.BizKit.WMS.Hikvision
{
[CreateAssetMenu(fileName = "HikvisionAgvSiteConfig", menuName = "ScriptableObjects/HikvisionAgvSiteConfig")]
public class HikvisionAgvSiteConfig : ConfigObject
{
    public HikvisionAgvSiteData data = HikvisionAgvSiteData.CreateDefault();

    public override ConfigData GetData()
    {
        return data;
    }

    public override void SetData(ConfigData cd)
    {
        data = cd as HikvisionAgvSiteData;
        data?.Rebuild();
        HikvisionAgvSite.Bind(data);
    }

#if UNITY_EDITOR
    private const string AgvPartConfigFolder = "Assets/Resources/PartConfigs";

    [Button("根据配置生成 AGV PartConfig")]
    private void GenerateAgvPartConfigs()
    {
        if (data == null)
        {
            Debug.LogError("[HikvisionAgvSiteConfig] data 为空。", this);
            return;
        }

        data.Rebuild();
        string[] robotCodes = data.GetRobotCodes();
        if (robotCodes == null || robotCodes.Length == 0)
        {
            Debug.LogWarning("[HikvisionAgvSiteConfig] 未配置任何车号。", this);
            return;
        }

        if (!AssetDatabase.IsValidFolder(AgvPartConfigFolder))
        {
            System.IO.Directory.CreateDirectory(
                System.IO.Path.Combine(Application.dataPath, "Resources/PartConfigs"));
            AssetDatabase.Refresh();
        }

        int created = 0;
        int updated = 0;
        try
        {
            AssetDatabase.StartAssetEditing();
            for (int i = 0; i < robotCodes.Length; i++)
            {
                string code = robotCodes[i];
                string map = data.MapForRobot(code);
                string assetPath = $"{AgvPartConfigFolder}/AGV_{code}.asset";
                var asset = AssetDatabase.LoadAssetAtPath<PartConfigData>(assetPath);
                bool isNew = asset == null;
                if (isNew)
                {
                    asset = ScriptableObject.CreateInstance<PartConfigData>();
                    asset.data = new PartConfig
                    {
                        ConfigID = "ID" + Guid.NewGuid().ToString("N")[..8],
                        pointConfigs = new List<PointConfig>()
                    };
                    AssetDatabase.CreateAsset(asset, assetPath);
                    created++;
                }
                else
                {
                    updated++;
                    if (asset.data == null)
                        asset.data = new PartConfig();
                    if (string.IsNullOrEmpty(asset.data.ConfigID))
                        asset.data.ConfigID = "ID" + Guid.NewGuid().ToString("N")[..8];
                    asset.data.pointConfigs ??= new List<PointConfig>();
                }

                asset.data.partID = code;
                asset.data.partName = string.IsNullOrEmpty(map)
                    ? $"海康AGV_{code}"
                    : $"海康AGV_{code}({map})";
                asset.data.pointConfigs.Clear();
                for (int p = 0; p < HikvisionStatusPointCodes.All.Length; p++)
                {
                    HikPointDef def = HikvisionStatusPointCodes.All[p];
                    asset.data.pointConfigs.Add(new PointConfig
                    {
                        pointName = def.Name,
                        pointID = def.Code,
                        pointUnit = string.Empty,
                        dataType = (PointDataType)(int)def.ValueType
                    });
                }

                EditorUtility.SetDirty(asset);
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log(
            $"[HikvisionAgvSiteConfig] AGV PartConfig 完成：新建 {created}，更新 {updated}，合计 {robotCodes.Length}。输出 {AgvPartConfigFolder}",
            this);
        EditorUtility.DisplayDialog(
            "生成完成",
            $"已写入 {robotCodes.Length} 个 AGV PartConfig（新建 {created}，更新 {updated}）到\n{AgvPartConfigFolder}",
            "确定");
    }
#endif
}

/// <summary>一层 RCS 地图及其所属车号。车不跨层。</summary>
[Serializable]
public class HikvisionAgvFloorEntry
{
    [Tooltip("RCS queryAgvStatus 的 mapShortName，如 AA / BB")]
    public string MapShortName;

    [Tooltip("该层车号（= PartConfig.partID / robotCode）")]
    public string[] RobotCodes;
}

[Serializable]
public class HikvisionAgvSiteData : ConfigData
{
    [Tooltip("按楼层配置 RCS 地图简称与车号。现场变更只改这份配置。")]
    public List<HikvisionAgvFloorEntry> Floors = new List<HikvisionAgvFloorEntry>();

    [NonSerialized] private string[] _mapShortNames = Array.Empty<string>();
    [NonSerialized] private string[] _robotCodes = Array.Empty<string>();
    [NonSerialized] private Dictionary<string, string> _robotToMap;
    [NonSerialized] private Dictionary<string, int> _robotIndex;
    [NonSerialized] private Dictionary<string, string[]> _robotsOnMap;

    public static HikvisionAgvSiteData CreateDefault()
    {
        var data = new HikvisionAgvSiteData
        {
            ConfigID = "IDc8e41a72",
            Floors = new List<HikvisionAgvFloorEntry>
            {
                new HikvisionAgvFloorEntry
                {
                    MapShortName = "AA",
                    RobotCodes = new[] { "12497" }
                },
                new HikvisionAgvFloorEntry
                {
                    MapShortName = "BB",
                    RobotCodes = new[] { "30395", "30396", "30397", "30399", "30400", "30402" }
                }
            }
        };
        data.Rebuild();
        return data;
    }

    public void Rebuild()
    {
        var maps = new List<string>();
        var robots = new List<string>();
        var robotToMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var robotIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        var robotsOnMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        if (Floors != null)
        {
            for (int i = 0; i < Floors.Count; i++)
            {
                var floor = Floors[i];
                if (floor == null || string.IsNullOrWhiteSpace(floor.MapShortName))
                    continue;

                string map = floor.MapShortName.Trim();
                if (!robotsOnMap.ContainsKey(map))
                {
                    maps.Add(map);
                    robotsOnMap[map] = new List<string>();
                }

                var floorRobots = robotsOnMap[map];
                if (floor.RobotCodes == null)
                    continue;

                for (int r = 0; r < floor.RobotCodes.Length; r++)
                {
                    if (string.IsNullOrWhiteSpace(floor.RobotCodes[r]))
                        continue;

                    string code = floor.RobotCodes[r].Trim();
                    if (!robotToMap.ContainsKey(code))
                    {
                        robotToMap[code] = map;
                        robotIndex[code] = robots.Count;
                        robots.Add(code);
                        floorRobots.Add(code);
                    }
                }
            }
        }

        _mapShortNames = maps.ToArray();
        _robotCodes = robots.ToArray();
        _robotToMap = robotToMap;
        _robotIndex = robotIndex;
        _robotsOnMap = new Dictionary<string, string[]>(robotsOnMap.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var kv in robotsOnMap)
            _robotsOnMap[kv.Key] = kv.Value.ToArray();
    }

    public string[] GetMapShortNames()
    {
        EnsureCache();
        return _mapShortNames;
    }

    public string[] GetRobotCodes()
    {
        EnsureCache();
        return _robotCodes;
    }

    public string MapForRobot(string robotCode)
    {
        EnsureCache();
        if (string.IsNullOrEmpty(robotCode) || _robotToMap == null)
            return string.Empty;

        return _robotToMap.TryGetValue(robotCode, out string map) ? map : string.Empty;
    }

    public string[] RobotsOnMap(string mapShortName)
    {
        EnsureCache();
        if (string.IsNullOrEmpty(mapShortName) || _robotsOnMap == null)
            return Array.Empty<string>();

        return _robotsOnMap.TryGetValue(mapShortName, out string[] codes)
            ? codes
            : Array.Empty<string>();
    }

    public int SiteIndexOf(string robotCode)
    {
        EnsureCache();
        if (string.IsNullOrEmpty(robotCode) || _robotIndex == null)
            return -1;

        return _robotIndex.TryGetValue(robotCode, out int index) ? index : -1;
    }

    private void EnsureCache()
    {
        if (_robotToMap == null)
            Rebuild();
    }
}

/// <summary>运行时读取现场 AGV 楼层/车号配置。未绑定时回落到资产默认值。</summary>
public static class HikvisionAgvSite
{
    private static HikvisionAgvSiteData _data;
    private static readonly HikvisionAgvSiteData Fallback = HikvisionAgvSiteData.CreateDefault();

    public static HikvisionAgvSiteData Current => _data;

    public static string[] MapShortNames => Resolved.GetMapShortNames();

    public static string[] RobotCodes => Resolved.GetRobotCodes();

    public static void Bind(HikvisionAgvSiteData data)
    {
        _data = data;
        _data?.Rebuild();
    }

    public static bool TryBind(ConfigService config)
    {
        if (config != null && config.TryGetConfig<HikvisionAgvSiteData>(out var data) && data != null)
        {
            Bind(data);
            return true;
        }

        return false;
    }

    public static string MapForRobot(string robotCode) => Resolved.MapForRobot(robotCode);

    public static string[] RobotsOnMap(string mapShortName) => Resolved.RobotsOnMap(mapShortName);

    public static int SiteIndexOf(string robotCode) => Resolved.SiteIndexOf(robotCode);

    private static HikvisionAgvSiteData Resolved => _data ?? Fallback;
}
}
