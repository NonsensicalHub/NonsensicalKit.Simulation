using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

public class BuildInVideoPlayer : MonoBehaviour, IVideoPlayer
{
    [SerializeField] private VideoPlayer m_videoPlayer;

    [SerializeField] private Vector2Int m_renderTextureSize = new Vector2Int(1280, 720);
    private RenderTexture m_renderTexture;

    private void Awake()
    {
        m_renderTexture = new RenderTexture(m_renderTextureSize.x, m_renderTextureSize.y, 0, RenderTextureFormat.ARGB32);
        m_videoPlayer.targetTexture = m_renderTexture;
        m_videoPlayer.source = VideoSource.Url;
    }

    private void OnDestroy()
    {
        Destroy(m_videoPlayer);
        m_renderTexture.Release();
        m_renderTexture = null;
    }

    public void Init(RawImage image)
    {
        image.texture = m_renderTexture;
    }

    public void Play()
    {
        if (m_videoPlayer.url != null)
        {
            m_videoPlayer.Play();
        }
    }

    public void Open()
    {
        if (m_videoPlayer.url != null)
        {
            m_videoPlayer.Play();
        }
    }

    public void Open(string path)
    {
        m_videoPlayer.url = path;
        m_videoPlayer.Play();
    }

    public void Stop()
    {
        m_videoPlayer.Stop();
    }
}
