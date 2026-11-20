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
using System.Threading;
using UnityEngine;

namespace Frame.BizKit.WMS.Hikvision
{
/// <summary>
/// queryTaskStatus：必须带车号，按需逐车请求（不自动轮询）。
/// reqCode = 请求流水号；agvCode = 车号。车号列表读 HikvisionAgvSiteConfig。
/// </summary>
public class HikvisionTaskStatusRequest : HikvisionFetchRequestBase
{
    public const string EventKey = HikvisionAgvEvents.TaskRaw;

    [SerializeField, Label("clientCode")]
    private string m_clientCode = "";

    [SerializeField, Label("tokenCode")]
    private string m_tokenCode = "";

    private string _activeAgvCode = "";
    private bool _batching;

    protected override string LogTag =>
        string.IsNullOrEmpty(_activeAgvCode) ? "HikTaskStatus" : $"HikTaskStatus[{_activeAgvCode}]";

    private void Reset()
    {
        m_serverKey = "海康接口";
        m_enable = true;
        m_fetchOnReady = false;
        m_simulateOffline = true;
        m_pollIntervalSeconds = 0.5f;
        m_httpTimeoutSeconds = 8f;
    }

    public override void RequestOnce()
    {
        FetchAllAgvsAsync().Forget();
    }

    public void RequestVehicle(string agvCode)
    {
        if (_batching || string.IsNullOrWhiteSpace(agvCode))
            return;

        FetchOneAgvAsync(agvCode.Trim()).Forget();
    }

    protected override string BuildUrl() => WmsUrls.PostQueryTaskStatus(m_serverKey);

    protected override string BuildPostBody()
    {
        var body = new Dictionary<string, object>
        {
            ["reqCode"] = NewReqCode(),
            ["reqTime"] = string.Empty,
            ["clientCode"] = m_clientCode ?? string.Empty,
            ["tokenCode"] = m_tokenCode ?? string.Empty,
            ["agvCode"] = _activeAgvCode ?? string.Empty,
            ["taskCodes"] = Array.Empty<string>()
        };
        return JsonConvert.SerializeObject(body, JsonSetting);
    }

    protected override string BuildSimulateJson()
    {
        string code = _activeAgvCode ?? string.Empty;
        return
            "{\"code\":0,\"message\":\"成功\",\"reqCode\":\"SIM\",\"data\":[" +
            "{\"agvCode\":\"" + code + "\",\"taskCode\":\"SIM_TASK_" + code +
            "\",\"taskStatus\":\"2\",\"taskTyp\":\"A01\"}]}";
    }

    protected override UniTask OnManualFetchAsync(CancellationToken cancellationToken)
    {
        return FetchAllAgvsAwaitable(cancellationToken);
    }

    protected override async UniTask OnDataFetchedAsync(string rawJson, CancellationToken cancellationToken)
    {
        string vehicleCode = _activeAgvCode;
        var response = ParseResponse<HikTaskStatusDto>(rawJson);
        if (response?.data == null || response.data.Count == 0)
            return;

        var matched = new List<HikTaskStatusDto>(response.data.Count);
        for (int i = 0; i < response.data.Count; i++)
        {
            var dto = response.data[i];
            if (dto == null)
                continue;

            if (!string.IsNullOrEmpty(dto.agvCode) &&
                !string.IsNullOrEmpty(vehicleCode) &&
                !string.Equals(dto.agvCode, vehicleCode, StringComparison.Ordinal))
            {
                Debug.LogError(
                    $"[{LogTag}] data.agvCode={dto.agvCode} 与请求车号 {vehicleCode} 不一致，跳过。",
                    this);
                continue;
            }

            if (string.IsNullOrEmpty(dto.agvCode))
                dto.agvCode = vehicleCode;

            matched.Add(dto);
        }

        if (matched.Count == 0)
            return;

        IOCC.Publish(EventKey, vehicleCode, matched);
        await UniTask.CompletedTask;
    }

    private async UniTaskVoid FetchAllAgvsAsync()
    {
        await FetchAllAgvsAwaitable(this.GetCancellationTokenOnDestroy());
    }

    private async UniTaskVoid FetchOneAgvAsync(string agvCode)
    {
        _activeAgvCode = agvCode;
        await FetchAwaitableAsync(this.GetCancellationTokenOnDestroy());
    }

    private async UniTask FetchAllAgvsAwaitable(CancellationToken cancellationToken)
    {
        if (_batching)
            return;

        string[] codes = HikvisionAgvSite.RobotCodes;
        if (codes.Length == 0)
        {
            if (m_log)
                Debug.LogWarning($"[{LogTag}] HikvisionAgvSiteConfig 未配置车号。", this);
            return;
        }

        _batching = true;
        try
        {
            for (int i = 0; i < codes.Length; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                _activeAgvCode = codes[i];
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
