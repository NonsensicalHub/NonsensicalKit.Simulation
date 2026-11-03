#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using System.IO;
using NonsensicalKit.UGUI;
using NonsensicalKit.WebGL;
using UnityEngine;

public partial class WebBrowser
{
    private RectTransform _rect;
    private string _canvasID;

    private Vector3[] _vs = new Vector3[2];
    private Vector3 _x, _y;

    private partial string DealWithUrl(string url)
    {
        return m_urlType switch
        {
            UrlType.Http => url,
            UrlType.StreamingAssets => Path.Combine(Application.streamingAssetsPath, url),
            UrlType.File => url,
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private partial void Init()
    {
        _rect = GetComponent<RectTransform>();
        _canvasID = System.Guid.NewGuid().ToString();
        WebIframe.Instance.Create(_canvasID);
    }

    public partial void LoadUrl(string url)
    {
        if (string.IsNullOrEmpty(url) || _rect == null)
        {
            Debug.LogWarning("url is null or empty or 画布不存在 ");
            return;
        }

        if (string.IsNullOrEmpty(_canvasID))
        {
            _canvasID = System.Guid.NewGuid().ToString();
        }

        URL = url;
        GetPoint();
        WebIframe.Instance.Change(_x.x, _x.y, _y.x, _y.y, URL, _canvasID);
    }

    private void OnEnable()
    {
        if (string.IsNullOrEmpty(URL) || _rect == null)
        {
            return;
        }

        GetPoint();
        WebIframe.Instance.Change(_x.x, _x.y, _y.x, _y.y, URL, _canvasID);
    }

    private void OnDisable()
    {
        if (!string.IsNullOrEmpty(_canvasID) && WebIframe.Instance != null)
        {
            WebIframe.Instance.Close(_canvasID);
        }
    }

    private void OnRectTransformDimensionsChange()
    {
        if (string.IsNullOrEmpty(URL) || _rect == null)
        {
            return;
        }

        GetPoint();
        WebIframe.Instance.Change(_x.x, _x.y, _y.x, _y.y, URL, _canvasID);
    }

    private void GetPoint()
    {
        if (_rect == null)
            return;
        _rect.GetWorldMinMax(ref _vs);
        _x = new Vector3(_vs[0].x / Screen.width, _vs[0].y / Screen.height);
        _y = new Vector3(_vs[1].x / Screen.width, _vs[1].y / Screen.height);
    }
}
#endif
