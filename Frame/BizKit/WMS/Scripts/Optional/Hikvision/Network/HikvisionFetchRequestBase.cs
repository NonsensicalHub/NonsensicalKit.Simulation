using Cysharp.Threading.Tasks;
using Frame.BizKit.WMS.Hikvision;
using Frame.Core.Config;
using Frame.Core.Network;
using HikvisionAgv;
using NaughtyAttributes;
using Newtonsoft.Json;
using NonsensicalKit.Core.Service;
using NonsensicalKit.Core.Service.Config;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Frame.BizKit.WMS.Hikvision
{
/// <summary>
/// 海康 RCS 请求基类：单次 POST，成功码 code==0。
/// reqCode 为请求流水号，不是车号。不绑定 TwinDataSource Offline。
/// </summary>
public abstract class HikvisionFetchRequestBase : RequestBase
{
    [SerializeField, Label("就绪后启动轮询")]
    protected bool m_fetchOnReady = true;

    [SerializeField, Label("离线模拟（不发网络）")]
    protected bool m_simulateOffline = true;

    [SerializeField, Label("单次 HTTP 超时(秒)")]
    protected float m_httpTimeoutSeconds = 8f;

    [SerializeField, Label("轮询周期(秒)")]
    protected float m_pollIntervalSeconds = 0.5f;

    protected bool UrlsReady { get; private set; }

    private CancellationTokenSource _pollCts;
    private bool _started;
    private bool _requesting;

    protected abstract string LogTag { get; }

    protected abstract string BuildUrl();

    protected abstract string BuildPostBody();

    protected abstract string BuildSimulateJson();

    protected abstract UniTask OnDataFetchedAsync(string rawJson, CancellationToken cancellationToken);

    protected override void Awake()
    {
        base.Awake();
        ServiceCore.SafeGet<ConfigService>(OnConfigReady);
    }

    protected override void Start()
    {
        StartCoroutine(StartWhenReady());
    }

    private void OnEnable()
    {
        if (_started && m_enable && m_fetchOnReady)
            StartPolling();
    }

    private void OnDisable()
    {
        StopPolling();
    }

    public override void SetEnable(bool enable)
    {
        m_enable = enable;
        if (enable)
        {
            if (_started && m_fetchOnReady)
                StartPolling();
        }
        else
        {
            StopPolling();
        }
    }

    public override void SendRequestTest()
    {
        FetchOnceAsync().Forget();
    }

    [Button("立即拉取")]
    protected void FetchNow()
    {
        FetchOnceAsync().Forget();
    }

    /// <summary>按需拉一次。任务查询会覆写为按车号逐次请求。</summary>
    public virtual void RequestOnce()
    {
        FetchOnceAsync().Forget();
    }

    protected override void GetData(int id)
    {
        FetchOnceAsync().Forget();
    }

    protected override void OnDestroy()
    {
        StopPolling();
        base.OnDestroy();
    }

    private void OnConfigReady(ConfigService config)
    {
        if (!HikvisionAgvSite.TryBind(config) && m_log)
            Debug.LogWarning($"[{LogTag}] 未找到 HikvisionAgvSiteData，车号/地图使用内置默认值。", this);

        if (!config.TryGetConfig<URLBaseData>(out var data))
        {
            if (m_simulateOffline)
            {
                UrlsReady = true;
                if (m_log)
                    Debug.LogWarning($"[{LogTag}] 无 URLBaseData，离线模拟仍可用。", this);
                return;
            }

            Debug.LogError($"[{LogTag}] 未找到 URLBaseData 配置。", this);
            return;
        }

        URLS.Init(data);
        UrlsReady = true;
        if (m_log)
            Debug.Log($"[{LogTag}] URL 已初始化: {BuildUrl()}", this);
    }

    private IEnumerator StartWhenReady()
    {
        while (!UrlsReady && !m_simulateOffline)
            yield return null;

        if (m_simulateOffline)
            UrlsReady = true;

        _started = true;

        if (m_enable && m_fetchOnReady)
            StartPolling();
    }

    protected void StartPolling()
    {
        StopPolling();
        _pollCts = CancellationTokenSource.CreateLinkedTokenSource(
            this.GetCancellationTokenOnDestroy());
        PollLoopAsync(_pollCts.Token).Forget();

        if (m_log)
            Debug.Log($"[{LogTag}] 开始轮询，周期={Mathf.Max(0.05f, m_pollIntervalSeconds):F2}s", this);
    }

    protected void StopPolling()
    {
        if (_pollCts == null)
            return;

        try
        {
            _pollCts.Cancel();
            _pollCts.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }

        _pollCts = null;
    }

    private async UniTaskVoid PollLoopAsync(CancellationToken ct)
    {
        try
        {
            double interval = Mathf.Max(0.05f, m_pollIntervalSeconds);
            double next = Time.realtimeSinceStartupAsDouble;

            while (!ct.IsCancellationRequested)
            {
                await OnManualFetchAsync(ct);

                next += interval;
                double now = Time.realtimeSinceStartupAsDouble;
                if (now < next)
                {
                    await UniTask.Delay(
                        TimeSpan.FromSeconds(next - now), cancellationToken: ct);
                }
                else
                {
                    while (next <= now)
                        next += interval;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async UniTaskVoid FetchOnceAsync()
    {
        await OnManualFetchAsync(this.GetCancellationTokenOnDestroy());
    }

    /// <summary>手动/按需入口。默认单次 POST；任务按车号循环。</summary>
    protected virtual UniTask OnManualFetchAsync(CancellationToken cancellationToken)
    {
        return FetchAwaitableAsync(cancellationToken);
    }

    protected async UniTask FetchAwaitableAsync(CancellationToken cancellationToken)
    {
        if (_requesting)
            return;

        _requesting = true;
        CancellationTokenSource timeoutCts = null;
        try
        {
            CancellationToken ct = cancellationToken;
            if (!m_simulateOffline && m_httpTimeoutSeconds > 0f)
            {
                timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(m_httpTimeoutSeconds));
                ct = timeoutCts.Token;
            }

            string json;
            if (m_simulateOffline)
            {
                json = BuildSimulateJson();
                if (string.IsNullOrEmpty(json))
                {
                    if (m_log)
                        Debug.LogWarning($"[{LogTag}] 离线模拟 JSON 为空，跳过。", this);
                    return;
                }

                if (m_log)
                    Debug.Log($"[{LogTag}] 离线模拟 length={json.Length} \n : {json}", this);
            }
            else
            {
                if (!UrlsReady)
                {
                    if (m_log)
                        Debug.LogWarning($"[{LogTag}] URL 未就绪，跳过。", this);
                    return;
                }

                string url = BuildUrl();
                if (string.IsNullOrEmpty(url))
                {
                    Debug.LogError($"[{LogTag}] 未找到 Server Key「{m_serverKey}」对应的 BaseUrl。", this);
                    return;
                }

                string body = BuildPostBody();
                json = await HttpTool.Post(url, body, BuildHeader(), ct);
                if (string.IsNullOrEmpty(json))
                    return;

                if (m_log)
                    Debug.Log($"[{LogTag}] 离线模拟 length={json.Length} \n : {json}", this);
            }

            await OnDataFetchedAsync(json, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (m_log && !cancellationToken.IsCancellationRequested)
                Debug.LogWarning($"[{LogTag}] 请求超时。", this);
        }
        catch (Exception e)
        {
            Debug.LogError($"[{LogTag}] 请求异常: {e.Message}", this);
        }
        finally
        {
            timeoutCts?.Dispose();
            _requesting = false;
        }
    }

    protected virtual Dictionary<string, string> BuildHeader()
    {
        return new Dictionary<string, string>();
    }

    protected HikApiResponse<T> ParseResponse<T>(string json)
    {
        HikApiResponse<T> response =
            JsonConvert.DeserializeObject<HikApiResponse<T>>(json, JsonSetting);

        if (response == null)
        {
            Debug.LogError($"[{LogTag}] 响应解析失败。", this);
            return null;
        }

        if (response.code != 0)
        {
            Debug.LogError(
                $"[{LogTag}] 接口失败 code={response.code} message={response.message}",
                this);
            return null;
        }

        if (m_log)
        {
            int n = response.data?.Count ?? 0;
            Debug.Log($"[{LogTag}] 成功，data 条数={n}", this);
        }

        return response;
    }

    /// <summary>请求流水号（不是车号）。</summary>
    protected static string NewReqCode()
    {
        return Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
    }
}
}
