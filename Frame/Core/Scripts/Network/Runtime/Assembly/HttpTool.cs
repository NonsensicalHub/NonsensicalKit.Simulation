using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;


namespace Frame.Core.Network
{
public static class HttpTool
{
    #region GET

    public static async UniTask<string> Get(string url, CancellationToken cancellationToken = default(CancellationToken))
    {
        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            await request.SendWebRequest().WithCancellation(cancellationToken);
            if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError($"{request.url} 请求错误: {request.error}Return:{request.downloadHandler.text}");
                return null;
            }

            return request.downloadHandler.text;
        }
    }

    public static async UniTask<string> GetWithArgs(string url, Dictionary<string, string> fields, CancellationToken cancellationToken = default(CancellationToken))
    {
        string uri = GetArgsStr(url, fields);
        using (UnityWebRequest unityWebRequest = new UnityWebRequest(uri))
        {
            unityWebRequest.downloadHandler = new DownloadHandlerBuffer();
            await unityWebRequest.SendWebRequest().WithCancellation(cancellationToken);

            if (unityWebRequest.result == UnityWebRequest.Result.ConnectionError || unityWebRequest.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError($"{unityWebRequest.url} 请求错误: {unityWebRequest.error}Return:{unityWebRequest.downloadHandler.text}");
                return null;
            }

            return unityWebRequest.downloadHandler.text;
        }
    }

    public static async UniTask<string> GetWithArgs(string url, Dictionary<string, string> fields, Dictionary<string, string> header, CancellationToken cancellationToken = default(CancellationToken))
    {
        string uri = GetArgsStr(url, fields);
        using (UnityWebRequest unityWebRequest = new UnityWebRequest(uri))
        {
            unityWebRequest.downloadHandler = new DownloadHandlerBuffer();
            IncreaseHeader(unityWebRequest, header);
            await unityWebRequest.SendWebRequest().WithCancellation(cancellationToken);

            if (unityWebRequest.result == UnityWebRequest.Result.ConnectionError || unityWebRequest.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError($"{unityWebRequest.url} 请求错误: {unityWebRequest.error}Return:{unityWebRequest.downloadHandler.text}");
                return null;
            }

            return unityWebRequest.downloadHandler.text;
        }
    }

    #endregion

    #region Post

    public static async UniTask<string> Post(string url, string json, Dictionary<string, string> header, CancellationToken cancellationToken = default(CancellationToken))
    {
        using UnityWebRequest unityWebRequest = new UnityWebRequest(url, "POST");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
        using var a = (UploadHandler)new UploadHandlerRaw(bodyRaw);
        unityWebRequest.uploadHandler = a;
        //unityWebRequest.uploadHandler.Dispose();
        unityWebRequest.downloadHandler = (DownloadHandler)new DownloadHandlerBuffer();
        unityWebRequest.SetRequestHeader("Content-Type", "application/json");

        IncreaseHeader(unityWebRequest, header);

        await unityWebRequest.SendWebRequest().WithCancellation(cancellationToken);

        
        if (unityWebRequest.result == UnityWebRequest.Result.ConnectionError || unityWebRequest.result == UnityWebRequest.Result.ProtocolError)
        {
            Debug.LogError($"{unityWebRequest.url} 请求错误: {unityWebRequest.error} Return:{unityWebRequest.downloadHandler.text}");
            return null;
        }

        return unityWebRequest.downloadHandler.text;
    }

    public static async UniTask<string> Post(string url, WWWForm form, Dictionary<string, string> header, CancellationToken cancellationToken = default(CancellationToken))
    {
        using (UnityWebRequest unityWebRequest = UnityWebRequest.Post(url, form))
        {
            IncreaseHeader(unityWebRequest, header);

            await unityWebRequest.SendWebRequest().WithCancellation(cancellationToken);

            if (unityWebRequest.result == UnityWebRequest.Result.ConnectionError || unityWebRequest.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError($"{unityWebRequest.url} 请求错误: {unityWebRequest.error} Return:{unityWebRequest.downloadHandler.text}");
                return null;
            }

            return unityWebRequest.downloadHandler.text;
        }
    }

    public static async UniTask<string> Post(string url, Dictionary<string, string> formData, Dictionary<string, string> header, CancellationToken cancellationToken = default(CancellationToken))
    {
        return await Post(url, CreateForm(formData), header, cancellationToken);
    }

    public static async UniTask<string> PostWithArgs(string url, Dictionary<string, string> fields, Dictionary<string, string> header, string json = "", CancellationToken cancellationToken = default(CancellationToken))
    {
        var uri = GetArgsStr(url, fields);
        using UnityWebRequest unityWebRequest = new UnityWebRequest(uri, "POST");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json ?? string.Empty);
        using var uploadHandler = new UploadHandlerRaw(bodyRaw);
        unityWebRequest.uploadHandler = uploadHandler;
        unityWebRequest.downloadHandler = new DownloadHandlerBuffer();
        unityWebRequest.SetRequestHeader("Content-Type", "application/json");

        IncreaseHeader(unityWebRequest, header);

        await unityWebRequest.SendWebRequest().WithCancellation(cancellationToken);

        if (unityWebRequest.result == UnityWebRequest.Result.ConnectionError || unityWebRequest.result == UnityWebRequest.Result.ProtocolError)
        {
            Debug.LogError($"{unityWebRequest.url} 请求错误: {unityWebRequest.error} Return:{unityWebRequest.downloadHandler.text}");
            return null;
        }

        return unityWebRequest.downloadHandler.text;
    }

    #endregion

    private static WWWForm CreateForm(Dictionary<string, string> formData)
    {
        WWWForm form = new WWWForm();
        if (formData != null)
        {
            foreach (var item in formData)
            {
                form.AddField(item.Key, item.Value);
            }
        }

        return form;
    }

    private static void IncreaseHeader(UnityWebRequest unityWebRequest, Dictionary<string, string> headerData)
    {
        if (headerData != null)
        {
            foreach (var tmp in headerData)
            {
                unityWebRequest.SetRequestHeader(tmp.Key, tmp.Value);
            }
        }
    }

    private static string GetArgsStr(string baseUrl, Dictionary<string, string> fields)
    {
        if (fields != null && fields.Count > 0)
        {
            StringBuilder sb = new StringBuilder(baseUrl);
            sb.Append("?");
            foreach (var item in fields)
            {
                sb.Append(Uri.EscapeDataString(item.Key ?? string.Empty));
                sb.Append("=");
                sb.Append(Uri.EscapeDataString(item.Value ?? string.Empty));
                sb.Append("&");
            }

            sb.Remove(sb.Length - 1, 1); //去掉结尾的&
            return sb.ToString();
        }
        else
        {
            return baseUrl;
        }
    }
}
}
