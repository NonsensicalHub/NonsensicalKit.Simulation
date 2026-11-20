using Frame.BizKit.WMS.Hikvision;
using HikvisionAgv;
using NaughtyAttributes;
using NonsensicalKit.Core;
using NonsensicalKit.Core.Service;
using NonsensicalKit.Core.Service.Config;
using NonsensicalKit.DigitalTwin.Motion;
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Frame.BizKit.WMS.Hikvision
{
/// <summary>
/// 海康查询结果 → DataHub；状态另发 MotionPartUpdate 驱动 TwinActor。
/// 状态：一层一次返回多车，按 robotCode 分发。
/// 任务：一车一次，按 agvCode 只入 DataHub。
/// </summary>
public class HikvisionDataBridge : NonsensicalMono
{
    [SerializeField] private bool m_log;

    private DataHub _dataHub;

    private void Awake()
    {
        Subscribe<string, List<HikAgvStatusDto>>(HikvisionAgvStatusRequest.EventKey, OnAgvStatusRaw);
        Subscribe<string, List<HikTaskStatusDto>>(HikvisionTaskStatusRequest.EventKey, OnTaskStatusRaw);
        ServiceCore.SafeGet<DataHub>(hub => _dataHub = hub);
        ServiceCore.SafeGet<ConfigService>(cfg => HikvisionAgvSite.TryBind(cfg));
    }

    private void OnAgvStatusRaw(string mapShortName, List<HikAgvStatusDto> list)
    {
        if (list == null || list.Count == 0)
            return;

        long ticks = NowTicksMs();
        var hubBatch = new List<PointData>(list.Count * HikvisionStatusPointCodes.StatusPoints.Length);

        for (int i = 0; i < list.Count; i++)
        {
            var dto = list[i];
            if (dto == null || string.IsNullOrEmpty(dto.robotCode))
                continue;

            if (!AcceptStatusMap(mapShortName, dto))
                continue;

            var points = ToStatusPointList(dto, ticks);
            hubBatch.AddRange(points);
            PublishWithID("MotionPartUpdate", dto.robotCode, points);

            if (m_log)
            {
                Debug.Log(
                    $"[HikBridge] AGV {dto.robotCode} map={dto.mapCode} pos=({dto.posX},{dto.posY})",
                    this);
            }
        }

        FeedDataHub(hubBatch);
    }

    private void OnTaskStatusRaw(string agvCode, List<HikTaskStatusDto> list)
    {
        if (list == null || list.Count == 0)
            return;

        long ticks = NowTicksMs();
        var hubBatch = new List<PointData>(list.Count * HikvisionStatusPointCodes.TaskPoints.Length);

        for (int i = 0; i < list.Count; i++)
        {
            var dto = list[i];
            if (dto == null)
                continue;

            string code = string.IsNullOrEmpty(dto.agvCode) ? agvCode : dto.agvCode;
            if (string.IsNullOrEmpty(code))
                continue;

            hubBatch.AddRange(ToTaskPointList(code, dto, ticks));

            if (m_log)
            {
                Debug.Log(
                    $"[HikBridge] Task agv={code} status={dto.taskStatus} code={dto.taskCode}",
                    this);
            }
        }

        FeedDataHub(hubBatch);
    }

    private bool AcceptStatusMap(string mapShortName, HikAgvStatusDto dto)
    {
        if (string.IsNullOrEmpty(mapShortName))
            return true;

        int siteIndex = HikvisionAgvSite.SiteIndexOf(dto.robotCode);
        if (siteIndex >= 0)
        {
            string expected = HikvisionAgvSite.MapForRobot(dto.robotCode);
            if (!string.Equals(mapShortName, expected, StringComparison.OrdinalIgnoreCase))
            {
                if (m_log)
                {
                    Debug.LogWarning(
                        $"[HikBridge] 车 {dto.robotCode} 期望地图 {expected}，本次来自 {mapShortName}，跳过。",
                        this);
                }

                return false;
            }
        }

        if (!string.IsNullOrEmpty(dto.mapCode) &&
            !string.Equals(dto.mapCode, mapShortName, StringComparison.OrdinalIgnoreCase))
        {
            if (m_log)
            {
                Debug.LogWarning(
                    $"[HikBridge] 车 {dto.robotCode} mapCode={dto.mapCode}，请求层 {mapShortName}，跳过。",
                    this);
            }

            return false;
        }

        return true;
    }

    private void FeedDataHub(List<PointData> points)
    {
        if (_dataHub == null || points == null || points.Count == 0)
            return;

        _dataHub.ReceivePoints(points);
    }

    private static List<PointData> ToStatusPointList(HikAgvStatusDto dto, long ticks)
    {
        string partId = dto.robotCode;
        var defs = HikvisionStatusPointCodes.StatusPoints;
        var points = new List<PointData>(defs.Length);
        for (int i = 0; i < defs.Length; i++)
        {
            var def = defs[i];
            points.Add(Point(partId, def, StatusValue(dto, def.Code), ticks));
        }

        return points;
    }

    private static List<PointData> ToTaskPointList(string partId, HikTaskStatusDto dto, long ticks)
    {
        var defs = HikvisionStatusPointCodes.TaskPoints;
        var points = new List<PointData>(defs.Length);
        for (int i = 0; i < defs.Length; i++)
        {
            var def = defs[i];
            points.Add(Point(partId, def, TaskValue(dto, def.Code), ticks));
        }

        return points;
    }

    private static string StatusValue(HikAgvStatusDto dto, string code)
    {
        if (code == HikvisionStatusPointCodes.PosX.Code) return dto.posX;
        if (code == HikvisionStatusPointCodes.PosY.Code) return dto.posY;
        if (code == HikvisionStatusPointCodes.RobotDir.Code) return dto.robotDir;
        if (code == HikvisionStatusPointCodes.Speed.Code) return dto.speed;
        if (code == HikvisionStatusPointCodes.Status.Code) return dto.status;
        if (code == HikvisionStatusPointCodes.Stop.Code) return dto.stop;
        if (code == HikvisionStatusPointCodes.Battery.Code) return dto.battery;
        if (code == HikvisionStatusPointCodes.ExclType.Code) return dto.exclType;
        if (code == HikvisionStatusPointCodes.PodCode.Code) return dto.podCode;
        if (code == HikvisionStatusPointCodes.PodDir.Code) return dto.podDir;
        if (code == HikvisionStatusPointCodes.MapCode.Code) return dto.mapCode;
        return string.Empty;
    }

    private static string TaskValue(HikTaskStatusDto dto, string code)
    {
        if (code == HikvisionStatusPointCodes.TaskCode.Code) return dto.taskCode;
        if (code == HikvisionStatusPointCodes.TaskStatus.Code) return dto.taskStatus;
        if (code == HikvisionStatusPointCodes.TaskTyp.Code) return dto.taskTyp;
        return string.Empty;
    }

    private static PointData Point(string partId, HikPointDef def, string value, long ticks)
    {
        return new PointData(
            def.Name,
            $"{partId}_{def.Code}",
            def.Code,
            ticks,
            value ?? string.Empty);
    }

    private static long NowTicksMs() => DateTime.Now.Ticks / 10000;

    [Button("模拟现场全部车辆状态")]
    private void SimulateSample()
    {
        string[] maps = HikvisionAgvSite.MapShortNames;
        for (int m = 0; m < maps.Length; m++)
        {
            string map = maps[m];
            string[] codes = HikvisionAgvSite.RobotsOnMap(map);
            if (codes.Length == 0)
                continue;

            HikvisionAgvPathSampler.TryCreate(map, out var sampler);
            var list = new List<HikAgvStatusDto>(codes.Length);
            for (int i = 0; i < codes.Length; i++)
            {
                string code = codes[i];
                string posX;
                string posY;
                string dir;
                if (sampler != null)
                {
                    float spacing = sampler.Length / Mathf.Max(1, codes.Length);
                    sampler.Sample(i * spacing, out float mapX, out float mapY, out float heading);
                    posX = mapX.ToString("0.###", CultureInfo.InvariantCulture);
                    posY = mapY.ToString("0.###", CultureInfo.InvariantCulture);
                    dir = heading.ToString("0.##", CultureInfo.InvariantCulture);
                }
                else
                {
                    int site = Mathf.Max(0, HikvisionAgvSite.SiteIndexOf(code));
                    posX = (20000 + site * 500).ToString(CultureInfo.InvariantCulture);
                    posY = (20000 + site * 300).ToString(CultureInfo.InvariantCulture);
                    dir = "0";
                }

                list.Add(new HikAgvStatusDto
                {
                    robotCode = code,
                    mapCode = map,
                    posX = posX,
                    posY = posY,
                    robotDir = dir,
                    speed = "0.5",
                    status = "4",
                    stop = "0",
                    battery = "88",
                    exclType = "0",
                    podCode = "0",
                    podDir = "0"
                });
            }

            OnAgvStatusRaw(map, list);
        }
    }
}
}
