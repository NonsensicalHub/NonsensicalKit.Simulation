using System.Collections;
using NonsensicalKit.UGUI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;


namespace Frame.Core.VideoPlayer
{
public class SPElementVideoPlayer : NonsensicalUI
{
    [SerializeField] private bool localtest;
    [SerializeField] private bool m_useBuildInVideoPlayer;
    [SerializeField] private float _waitForToHide = 15f;
    [SerializeField] private TMP_Text _serialNumber;
    [SerializeField] private TMP_Text _descriptionText;
    [SerializeField] private GameObject m_playerPrefab;
    [SerializeField] private RawImage m_image;

    [SerializeField] private string[] localvideoFilePath;

    [ContextMenuItem("设置文本", "SetText")]
    Coroutine _coroutine;

    private ElementVideoInfo _infoTemp;
    private string _crtID;

    private IVideoPlayer _videoPlayer;

    protected override void Awake()
    {
        Subscribe<ElementVideoInfo>("setTopforThisJKInfo", SetTopForThisJkInfo);
        if (m_useBuildInVideoPlayer)
        {
            VideoPlayerComponentUtil.DestroyNonBuildInPlayers(m_playerPrefab);
            _videoPlayer = m_playerPrefab.GetComponent<BuildInVideoPlayer>();
        }
        else
        {
            VideoPlayerComponentUtil.DestroyBuildInPlayers(m_playerPrefab);

            _videoPlayer = VideoPlayerComponentUtil.GetPlayer(m_playerPrefab);
        }

        _videoPlayer.Init(m_image);
    }

    protected override void Start()
    {
        ShowTopJk(false);
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        ShowTopJk(false);
    }


    private void SetTopForThisJkInfo(ElementVideoInfo info)
    {
        Show(info);
        _infoTemp = info;
        if (_coroutine != null)
        {
            StopCoroutine(_coroutine);
            _coroutine = null;
        }

        if (this.gameObject.activeInHierarchy == false) return;
        _coroutine = StartCoroutine(DelayToHide());
    }

    private void Show(ElementVideoInfo info)
    {
        ShowTopJk(true);
        if (this.gameObject.activeInHierarchy == false) return;
        if (_crtID == info.Serial) return;
        _crtID = info.Serial;
        _serialNumber.text = info.Serial;
        _descriptionText.text = info.Description;

        string videoPath;
        if (localtest)
        {
            videoPath = string.IsNullOrEmpty(info.Url)
                ? Application.streamingAssetsPath + localvideoFilePath[Random.Range(0, localvideoFilePath.Length)]
                : info.Url;
            // 兜底路径写回，双击打开大窗时不再带上空 Url。
            if (string.IsNullOrEmpty(info.Url))
                info.Url = videoPath;
        }
        else
        {
            videoPath = info.Url;
        }

        _videoPlayer.Open(videoPath);
    }

    public void OnDoubleClick()
    {
        if (_infoTemp == null) return;
        Publish("ShowMonitoringWindow", _infoTemp);
    }


    IEnumerator DelayToHide()
    {
        yield return new WaitForSeconds(_waitForToHide);
        ShowTopJk(false);
        _infoTemp = null;
    }

    IEnumerator DelayToHide(float waitTime)
    {
        yield return new WaitForSeconds(waitTime);
        ShowTopJk(false);
    }

    private void ShowTopJk(bool tar)
    {
        _crtID = string.Empty;
        ChangeSelf(tar);
        _videoPlayer.Stop();
    }
}
}
