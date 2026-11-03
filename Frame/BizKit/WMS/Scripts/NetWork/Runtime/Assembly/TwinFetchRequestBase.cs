using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NaughtyAttributes;
using Newtonsoft.Json;
using NonsensicalKit.Core.Service;
using NonsensicalKit.Core.Service.Config;
using UnityEngine;

/// <summary>
/// 孪生中台接口请求基类：负责 URL 初始化、鉴权头、防重入与取数到反序列化。
/// 子类实现具体 URL / 参数 / 拿到 data 后的处理（默认仅打日志，不接业务）。
/// </summary>
public abstract class TwinFetchRequestBase : RequestBase
{
    [SerializeField, Label("就绪后立即拉取一次")]
    protected bool m_fetchOnReady;

    protected bool UrlsReady { get; private set; }
    private bool _requesting;

    protected bool IsRequesting => _requesting;

    protected abstract string LogTag { get; }

    /// <summary>额外就绪条件（如 Warehouse 初始化），默认已就绪。</summary>
    protected virtual bool IsExtraReady => true;

    /// <summary>拼出本次请求完整 URL（不含 query）。</summary>
    protected abstract string BuildUrl();

    /// <summary>可选：GET query；返回 null 表示无参数。</summary>
    protected virtual Dictionary<string, string> BuildQueryArgs() => null;

    /// <summary>可选：POST JSON body；非空则走 POST，否则走 GET。</summary>
    protected virtual string BuildPostBody() => null;

    /// <summary>拿到原始 JSON 后的处理（可 async；防重入会等到本方法结束）。</summary>
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

    public override void SendRequestTest()
    {
        FetchAsync();
    }

    [Button("立即拉取")]
    protected void FetchNow()
    {
        FetchAsync();
    }

    protected override void GetData(int id)
    {
        FetchAsync();
    }

    private void OnConfigReady(ConfigService config)
    {
        if (!config.TryGetConfig<URLBaseData>(out var data))
        {
            Debug.LogError($"[{LogTag}] 未找到 URLBaseData 配置。", this);
            return;
        }

        URLS.Init(data);
        TwinDataSource.TryInit(config, this);
        UrlsReady = true;
        if (m_log)
        {
            Debug.Log(
                $"[{LogTag}] URL 已初始化: {BuildUrl()} 数据源={TwinDataSource.Mode}",
                this);
        }
    }

    private IEnumerator StartWhenReady()
    {
        while (!UrlsReady || !IsExtraReady)
        {
            yield return null;
        }

        base.Start();

        if (m_fetchOnReady)
        {
            FetchAsync();
        }
    }

    /// <summary>Fire-and-forget 拉取。</summary>
    protected void FetchAsync()
    {
        FetchAwaitableAsync().Forget();
    }

    /// <summary>可 await 的拉取；业务侧动态传参后等待结果时使用。</summary>
    protected async UniTask FetchAwaitableAsync()
    {
        if (_requesting)
        {
            return;
        }

        if (TwinDataSource.IsOffline)
        {
            if (m_log)
            {
                Debug.Log($"[{LogTag}] 当前为离线模式，跳过 Twin 请求。", this);
            }

            return;
        }

        if (!UrlsReady || !IsExtraReady)
        {
            if (m_log)
            {
                Debug.LogWarning($"[{LogTag}] 尚未就绪，跳过本次请求。", this);
            }

            return;
        }

        string url = BuildUrl();
        if (string.IsNullOrEmpty(url))
        {
            Debug.LogError($"[{LogTag}] 未找到 Server Key「{m_serverKey}」对应的 BaseUrl。", this);
            return;
        }

        _requesting = true;
        try
        {
            CancellationToken ct = this.GetCancellationTokenOnDestroy();
            Dictionary<string, string> header = BuildAuthHeader();
            string postBody = BuildPostBody();
            string json;

            if (postBody != null)
            {
                json = await HttpTool.Post(url, postBody, header, ct);
            }
            else
            {
                json = await HttpTool.GetWithArgs(url, BuildQueryArgs(), header, ct);
            }

            if (string.IsNullOrEmpty(json))
            {
                return;
            }

            if (m_log)
            {
                Debug.Log($"[{LogTag}] 原始响应长度={json.Length}", this);
            }

            await OnDataFetchedAsync(json, ct);
        }
        catch (OperationCanceledException)
        {
            // 组件销毁时取消，忽略。
        }
        finally
        {
            _requesting = false;
        }
    }

    protected Dictionary<string, string> BuildAuthHeader()
    {
        var header = new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(URLS.Authorization))
        {
            header["Authorization"] = URLS.Authorization;
        }

        return header;
    }

    /// <summary>
    /// 反序列化并校验统一响应；失败返回 null。
    /// </summary>
    protected TwinApiResponse<T> ParseResponse<T>(string json)
    {
        TwinApiResponse<T> response =
            JsonConvert.DeserializeObject<TwinApiResponse<T>>(json, JsonSetting);

        if (response == null || response.data == null)
        {
            Debug.LogError($"[{LogTag}] 响应解析失败。", this);
            return null;
        }

        // 文档约定：成功须同时满足 success=true 且 code=200
        if (!response.success || response.code != 200)
        {
            Debug.LogError(
                $"[{LogTag}] 接口失败 success={response.success} code={response.code} message={response.message}",
                this);
            return null;
        }

        if (m_log)
        {
            Debug.Log($"[{LogTag}] 获取到 {response.data.Count} 条数据。", this);
        }

        return response;
    }

    protected static void AddQueryIfSet(Dictionary<string, string> args, string key, string value)
    {
        if (args == null || string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value))
        {
            return;
        }

        args[key] = value;
    }
}
