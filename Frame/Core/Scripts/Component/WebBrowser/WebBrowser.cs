using System;
using UnityEngine;

public enum UrlType
{
    Http,
    StreamingAssets,
    File
} 
////// <summary>
/// 网页浏览器 
/// </summary>
public partial class WebBrowser : MonoBehaviour
{
    [SerializeField] protected UrlType m_urlType=UrlType.StreamingAssets;
    [SerializeField] private string m_url;

    public string URL
    {
        get => DealWithUrl(m_url);
        set => m_url =value; 
    }

    /// <summary>未经过路径转换的原始 URL，供绑定反向导出。</summary>
    public string RawUrl => m_url;

    public UrlType CurrentUrlType => m_urlType;
   
    private void Awake()
    {
        Init();
    }

    public void SetUrlType(string urlType )
    {
        if (string.IsNullOrEmpty(urlType))
        {
            return;
        }

        string t=urlType.ToLower();
        m_urlType = t switch
        {
            "http" => UrlType.Http,
            "streamingassets" => UrlType.StreamingAssets,
            "file" => UrlType.File,
            _ => UrlType.File,
        };
    }
    
    public partial void LoadUrl(string url);
    private partial void Init();
    private partial string DealWithUrl(string url);
    

}
