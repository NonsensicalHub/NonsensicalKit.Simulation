using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

public class WindowPlayerContent : MonoBehaviour, IWindowContent
{
    [SerializeField] private RawImage m_image;
    [SerializeField] private bool m_useBuildInVideoPlayer;

    private string _crtID;
    private IVideoPlayer _videoPlayer;

    private bool _isInit;

    private void Init()
    {
        if (_isInit) return;
        if (m_useBuildInVideoPlayer)
        {
            Destroy(GetComponentInChildren<Frame.VideoPlayer.CustomVLCPlayer>());
            _videoPlayer = GetComponentInChildren<BuildInVideoPlayer>();
        }
        else
        {
            Destroy(GetComponentInChildren<BuildInVideoPlayer>());
            Destroy(GetComponentInChildren<VideoPlayer>());

            _videoPlayer = GetComponentInChildren<Frame.VideoPlayer.CustomVLCPlayer>();
        }

        _videoPlayer.Init(m_image);
        _isInit = true;
    }

    public void OnContentInit(object args)
    {
        Init();
        if (args is ElementVideoInfo info)
        {
            if (string.IsNullOrEmpty(info.Url))
            {
                Debug.LogError($"info {info.Serial}+ {info.Description}  URL为空" );
                return;
            }
            _videoPlayer.Open(info.Url);
        }
    }

    public bool OnContentClose()
    {
        _videoPlayer.Stop();
        return true;
    }
}
