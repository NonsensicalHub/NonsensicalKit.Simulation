using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// 绑定内嵌网页浏览器。
/// JSON 格式：字符串 URL，或 { "Url": "...", "UrlType": "Http|StreamingAssets|File" }
/// </summary>
public class BindingWeb : BindingToken
{
    [SerializeField] private WebBrowser m_webBrowser;

    private void Reset()
    {
        m_webBrowser = GetComponent<WebBrowser>();
    }

    public override void BindToken(JToken token)
    {
        if (m_webBrowser == null)
        {
            LogWarning($"{name} 缺少 WebBrowser");
            return;
        }

        if (token == null)
        {
            m_webBrowser.URL = string.Empty;
            return;
        }

        if (token.Type == JTokenType.Object)
        {
            m_webBrowser.URL = token["Url"]?.ToString() ?? string.Empty;
            string urlType = token["UrlType"]?.ToString();
            if (!string.IsNullOrEmpty(urlType))
            {
                m_webBrowser.SetUrlType(urlType);
            }
            return;
        }

        m_webBrowser.URL = token.ToString() ?? string.Empty;
    }

    public override JToken CollectJson()
    {
        if (m_webBrowser == null)
        {
            LogWarning($"{name} 缺少 WebBrowser");
            return new JObject
            {
                ["Url"] = string.Empty,
                ["UrlType"] = UrlType.File.ToString()
            };
        }

        return new JObject
        {
            ["Url"] = m_webBrowser.RawUrl ?? string.Empty,
            ["UrlType"] = m_webBrowser.CurrentUrlType.ToString()
        };
    }
}
