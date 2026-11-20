using Cysharp.Threading.Tasks;
using Frame.BizKit.WMS.Hikvision;
using Frame.BizKit.WMS.Network;
using Frame.Core.Network;
using HikvisionAgv;
using NaughtyAttributes;
using Newtonsoft.Json;
using NonsensicalKit.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using UnityEngine;

namespace Frame.BizKit.WMS.Hikvision
{
/// <summary>
/// 轮询 queryAgvStatus：一个组件覆盖多层；每层一次 POST，返回该层全部车。
/// reqCode = 请求流水号；mapShortName 默认读 HikvisionAgvSiteConfig。
/// </summary>
public class HikvisionAgvStatusRequest : HikvisionFetchRequestBase
{
    public const string EventKey = HikvisionAgvEvents.StatusRaw;

    [SerializeField, Label("clientCode")]
    private string m_clientCode = "";

    [SerializeField, Label("tokenCode")]
    private string m_tokenCode = "";

    [SerializeField, Label("离线沿路网走")]
    private bool m_simulateAlongPath = true;

    [SerializeField, Label("模拟速度(地图单位/秒)"), Tooltip("与海康 MapX/MapY 同单位，常见为毫米")]
    private float m_simulateSpeed = 1200f;

    private string _activeMap = "AA";
    private bool _batching;
    private float _simLastTime = -1f;
    private int _simFrame = -1;
    private float _simStepDt;
    private readonly Dictionary<string, float> _simDistance = new Dictionary<string, float>();
    private readonly Dictionary<string, HikvisionAgvPathSampler> _simSampler =
        new Dictionary<string, HikvisionAgvPathSampler>();
    private readonly HashSet<string> _simMissingPath = new HashSet<string>();

    protected override string LogTag => $"HikAgvStatus[{_activeMap}]";

    private void Reset()
    {
        m_serverKey = "海康接口";
        m_enable = true;
        m_fetchOnReady = true;
        m_simulateOffline = true;
        m_pollIntervalSeconds = 0.5f;
        m_httpTimeoutSeconds = 8f;
    }

    protected override string BuildUrl() => WmsUrls.PostQueryAgvStatus(m_serverKey);

    protected override string BuildPostBody()
    {
        var body = new Dictionary<string, string>
        {
            ["reqCode"] = NewReqCode(),
            ["reqTime"] = string.Empty,
            ["clientCode"] = m_clientCode ?? string.Empty,
            ["tokenCode"] = m_tokenCode ?? string.Empty,
            ["mapShortName"] = _activeMap ?? string.Empty
        };
        return JsonConvert.SerializeObject(body, JsonSetting);
    }

    protected override string BuildSimulateJson()
    {
        string map = _activeMap ?? string.Empty;
        string[] codes = HikvisionAgvSite.RobotsOnMap(map);
        float dt = SimDeltaTime();
        HikvisionAgvPathSampler sampler = GetSimSampler(map);

        var sb = new StringBuilder(256 + codes.Length * 180);
        sb.Append("{\"code\":0,\"message\":\"成功\",\"reqCode\":\"SIM\",\"data\":[");
        for (int i = 0; i < codes.Length; i++)
        {
            if (i > 0)
                sb.Append(',');
            string code = codes[i];
            SampleSimPose(sampler, code, i, codes.Length, dt, out string x, out string y, out string dir);
            sb.Append("{\"battery\":\"88\",\"exclType\":\"0\",\"mapCode\":\"").Append(map)
                .Append("\",\"podCode\":\"0\",\"podDir\":\"0\",")
                .Append("\"posX\":\"").Append(x).Append("\",\"posY\":\"").Append(y)
                .Append("\",\"robotCode\":\"").Append(code)
                .Append("\",\"robotDir\":\"").Append(dir)
                .Append("\",\"robotIp\":\"\",\"speed\":\"0.5\",\"status\":\"4\",\"stop\":\"0\"}");
        }

        sb.Append("]}");
        return sb.ToString();
    }

    private float SimDeltaTime()
    {
        int frame = Time.frameCount;
        if (frame == _simFrame)
            return _simStepDt;

        float now = Time.realtimeSinceStartup;
        float dt = _simLastTime < 0f ? 0f : now - _simLastTime;
        _simLastTime = now;
        _simFrame = frame;
        _simStepDt = Mathf.Clamp(dt, 0f, 1f);
        return _simStepDt;
    }

    private HikvisionAgvPathSampler GetSimSampler(string map)
    {
        if (!m_simulateAlongPath)
            return null;

        if (_simSampler.TryGetValue(map, out var cached) && cached != null)
            return cached;

        if (HikvisionAgvPathSampler.TryCreate(map, out var sampler) && sampler != null)
        {
            _simSampler[map] = sampler;
            return sampler;
        }

        if (_simMissingPath.Add(map) && m_log)
        {
            Debug.LogWarning(
                $"[{LogTag}] 未找到 {map} 的路网（地图框填 MapShortName），模拟位置回退到占位坐标。",
                this);
        }

        return null;
    }

    private void SampleSimPose(
        HikvisionAgvPathSampler sampler,
        string code,
        int index,
        int count,
        float dt,
        out string x,
        out string y,
        out string dir)
    {
        if (sampler != null && sampler.Length > 1e-3f)
        {
            if (!_simDistance.TryGetValue(code, out float travel))
            {
                float spacing = sampler.Length / Mathf.Max(1, count);
                travel = index * spacing;
            }

            travel += dt * Mathf.Max(0f, m_simulateSpeed);
            _simDistance[code] = travel;
            sampler.Sample(travel, out float mapX, out float mapY, out float heading);
            x = mapX.ToString("0.###", CultureInfo.InvariantCulture);
            y = mapY.ToString("0.###", CultureInfo.InvariantCulture);
            dir = heading.ToString("0.##", CultureInfo.InvariantCulture);
            return;
        }

        int site = Mathf.Max(0, HikvisionAgvSite.SiteIndexOf(code));
        x = (20000 + site * 500).ToString(CultureInfo.InvariantCulture);
        y = (20000 + site * 300).ToString(CultureInfo.InvariantCulture);
        dir = "0";
    }

    protected override UniTask OnManualFetchAsync(CancellationToken cancellationToken)
    {
        return FetchAllMapsAwaitable(cancellationToken);
    }

    protected override async UniTask OnDataFetchedAsync(string rawJson, CancellationToken cancellationToken)
    {
        var response = ParseResponse<HikAgvStatusDto>(rawJson);
        if (response?.data == null || response.data.Count == 0)
            return;

        IOCC.Publish(EventKey, _activeMap, response.data);
        await UniTask.CompletedTask;
    }

    private async UniTask FetchAllMapsAwaitable(CancellationToken cancellationToken)
    {
        if (_batching)
            return;

        string[] maps = HikvisionAgvSite.MapShortNames;
        if (maps.Length == 0)
        {
            if (m_log)
                Debug.LogWarning($"[{LogTag}] HikvisionAgvSiteConfig 未配置 mapShortName。", this);
            return;
        }

        _batching = true;
        try
        {
            foreach (var t in maps)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                _activeMap = t;
                await FetchAwaitableAsync(cancellationToken);
            }
        }
        finally
        {
            _batching = false;
        }
    }
}
}
