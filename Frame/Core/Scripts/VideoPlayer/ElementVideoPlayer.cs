using System;
using NaughtyAttributes;
using NonsensicalKit.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Frame.Core.Binding;

/// <summary>
/// 类似 <see cref="JKElement"/> 的“视频播放元素”：当元素滑动到视口边缘/范围内时自动取出播放器播放，离开则停止并回收。
/// </summary>

namespace Frame.Core.VideoPlayer
{
public class ElementVideoPlayer : NonsensicalMono
{
    [Header("Data")]
    [SerializeField, BindableParam("LocalTest")]
    private bool localTest;

    [SerializeField] private bool localVideoFile;
    [SerializeField] private string localTestURL;

    [SerializeField, BindableParam("VideoPaths")]
    private string[] localVideoFilePath;

    [SerializeField, BindableParam("Url")] private string url;

    [Header("Player")]
    [SerializeField] private VideoPlayerManager manager;


    [SerializeField] private RawImage renderImage;

    [Header("Edge Play")]
    [SerializeField] private RectTransform viewport;

    [SerializeField] private RectTransform rect;
    [SerializeField] private float thresholdValue = 1f;

    [SerializeField] private TMP_Text m_serial;
    [SerializeField] private TMP_Text m_description;
    private IVideoPlayer vlcPlayer;

    [ShowNonSerializedField] private bool _isElementEnter;


    // 缓存最后一次播放的 URL，避免重复创建
    private string _lastPlayedUrl;

    /// <summary>是否已进入“可播放范围”。切换时会自动开播/停播。</summary>
    public bool IsElementEnter
    {
        get => _isElementEnter;
        set
        {
            if (_isElementEnter == value) return;
            _isElementEnter = value;

            if (_isElementEnter)
            {
                if (manager == null)
                {
                    Debug.LogWarning($"{nameof(ElementVideoPlayer)}: 未指定 {nameof(manager)}，无法播放。", this);
                    return;
                }

                vlcPlayer = manager.GetVideoPlayer();
                if (vlcPlayer == null)
                {
                    Debug.LogWarning($"{nameof(ElementVideoPlayer)}: 对象池未能提供 {nameof(IVideoPlayer)}。", this);
                    return;
                }


                vlcPlayer.Init(renderImage);


                var finalUrl = ResolveUrl();
                if (!string.IsNullOrWhiteSpace(finalUrl))
                {
                    // 如果 URL 没变化且播放器已有纹理，直接播放
                    if (_lastPlayedUrl == finalUrl)
                    {
                        vlcPlayer.Play();
                    }
                    else
                    {
                        vlcPlayer.Open(finalUrl);
                        _lastPlayedUrl = finalUrl;
                    }
                    //vlcPlayer.Play();
                }
                else
                {
                    Debug.LogWarning($"{nameof(ElementVideoPlayer)}: 未指定播放地址。", this);
                }
            }
            else
            {
                if (vlcPlayer != null)
                {
                    vlcPlayer.Stop();
                    if (manager != null)
                    {
                        manager.StoreObj(((UnityEngine.Component)vlcPlayer).gameObject);
                    }

                    vlcPlayer = null;
                    _lastPlayedUrl = null; // 清空缓存
                }
            }
        }
    }

    private void Awake()
    {
        renderImage ??= GetComponentInChildren<RawImage>(true);
        rect ??= transform as RectTransform;
    }

    private void OnDisable()
    {
        // UI 被隐藏时，避免后台继续播放
        IsElementEnter = false;
    }

    protected override void OnDestroy()
    {
        // 确保回收
        base.OnDestroy();
        IsElementEnter = false;
    }

    private void Update()
    {
        if (viewport == null || rect == null) return;
        IsRectTransformNearViewport(rect, viewport);
    }


    public void OnClick()
    {
        // 预留：如果需要点击时在 UI 顶部显示/放大播放，可以在这里 Publish 事件或调用外部逻辑。
        var info = new ElementVideoInfo
        {
            Serial = m_serial.text,
            Description = m_description.text,
            Url = ResolveUrl()
        };

        Publish("setTopforThisJKInfo", info);
    }

    public void OnDoubleClick()
    {
        var info = new ElementVideoInfo
        {
            Serial = m_serial.text,
            Description = m_description.text,
            Url = ResolveUrl()
        };
        Publish("ShowMonitoringWindow", info);
    }

    public void SetUrl(string newUrl)
    {
        url = newUrl;
        if (_isElementEnter && vlcPlayer != null)
        {
            vlcPlayer.Open(ResolveUrl());
        }
    }

    public ElementVideoInfo GetVideoInfo()
    {
        return new ElementVideoInfo
        {
            Serial = m_serial.text,
            Url = ResolveUrl(),
            Description = m_description.text
        };
    }

    private string ResolveUrl()
    {
        if (localTest)
        {
            // JSON 绑定 VideoPaths + LocalTest 时未必勾选 localVideoFile，有路径就优先用本地文件。
            if (localVideoFilePath is { Length: > 0 } &&
                (localVideoFile || string.IsNullOrEmpty(url)))
            {
                if (string.IsNullOrEmpty(url))
                {
                    var idx = UnityEngine.Random.Range(0, localVideoFilePath.Length);
                    url = Application.streamingAssetsPath + localVideoFilePath[idx];
                }
            }
            else if (string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(localTestURL))
            {
                url = localTestURL;
            }
        }

        return url;
    }

    private void IsRectTransformNearViewport(RectTransform rectTransform, RectTransform targetViewport)
    {
        var corners = new Vector3[4];
        rectTransform.GetWorldCorners(corners);
        for (int i = 0; i < corners.Length; i++)
        {
            corners[i] = targetViewport.InverseTransformPoint(corners[i]);
        }

        var rectLocal = new Rect(
            corners[0].x,
            corners[0].y,
            corners[2].x - corners[0].x,
            corners[2].y - corners[0].y
        );

        CheckNear(targetViewport.rect, rectLocal);
    }

    private void CheckNear(Rect viewportRect, Rect elementRect)
    {
        // 与 JKElement 保持一致：只检查垂直方向与阈值，避免横向滑动布局误判。
        var delta1 = viewportRect.yMin - elementRect.yMax - thresholdValue; // 大框底部 - 小框顶部
        var delta2 = elementRect.yMin - viewportRect.yMax - thresholdValue; // 小框底部 - 大框顶部
        IsElementEnter = delta1 < 0 && delta2 < 0;
    }
}

public class ElementVideoInfo
{
    public string Serial;
    public string Description;
    public string Url;
}
}
