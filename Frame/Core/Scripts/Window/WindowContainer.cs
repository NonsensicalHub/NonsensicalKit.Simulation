using System;
using NonsensicalKit.UGUI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class WindowContainer : MonoBehaviour, IPointerClickHandler
{
    [Header("标题栏")]
    [SerializeField] private Image m_iconImage;

    [SerializeField] private TextMeshProUGUI m_titleText;
    [SerializeField] private Button m_closeButton;

    [SerializeField] private bool m_allowSwitchLevel = true;

    [Header("全屏按钮")]
    [Tooltip("标题栏右侧、关闭按钮左侧的全屏切换按钮（需在预制体中添加）")]
    [SerializeField] private Button m_fullscreenButton;

    [SerializeField] private TextMeshProUGUI m_fullscreenButtonText;

    [Header("窗口容器")]
    [SerializeField] private RectTransform m_windowRoot;

    [SerializeField] private RectTransform m_contentParent;

    private WindowConfig _config;
    private object _contentArgs;
    private GameObject _loadedContentInstance;
    private IWindowContent _windowContent;
    private string _windowHandle;
    private GameObject _contentPrefab;
    private bool _isFullscreen, _isFocus;

    private Vector2 _originalAnchorMin;
    private Vector2 _originalPivot;
    private Vector2 _originalAnchorMax;
    private Vector2 _originalAnchoredPosition;
    private Vector2 _originalSizeDelta;
    private Transform _originalParent;
    private int _originalSiblingIndex;

    public bool IsFullscreen => _isFullscreen;
    public bool IsOpened => gameObject.activeSelf;
    public bool IsFocus => _isFocus;
    public string WindowHandle => _windowHandle;
    public GameObject ContentPrefab => _contentPrefab;
    public object ContentArgs => _contentArgs;

    public event Action<WindowContainer> OnClosed;
    public event Action<WindowContainer> OnFocusRequested;

    private void Awake()
    {
        EnsureWindowHandle();
        m_closeButton.onClick.AddListener(OnCloseButtonClicked);

        if (m_fullscreenButton != null)
            m_fullscreenButton.onClick.AddListener(OnFullscreenButtonClicked);
    }

    private void OnDestroy()
    {
        m_closeButton.onClick.RemoveListener(OnCloseButtonClicked);

        if (m_fullscreenButton != null)
            m_fullscreenButton.onClick.RemoveListener(OnFullscreenButtonClicked);
    }


    /// <summary>
    /// 用指定配置打开窗口，并加载预制体内容
    /// </summary>
    public void Open(WindowConfig config, GameObject contentPrefab, object contentArgs = null)
    {
        _config = config;
        EnsureWindowHandle();
        _contentPrefab = contentPrefab;
        _contentArgs = contentArgs;

        // 确保先退出上一次可能残留的全屏状态
        if (_isFullscreen)
            ExitFullscreenImmediate();

        ApplyConfig(config, contentArgs);
        LoadContent(contentPrefab, contentArgs);

        gameObject.SetActive(true);
        SetFocusState(false);
    }

    /// <summary>
    /// 关闭窗口并清理内容
    /// </summary>
    public void Close()
    {
        if (_windowContent != null)
        {
            if (!_windowContent.OnContentClose())
                return;
        }

        // 关闭前退出全屏，恢复原始状态
        if (_isFullscreen)
            ExitFullscreenImmediate();

        UnloadContent();
        _isFocus = false;
        _config.OnCloseCallback?.Invoke();
        gameObject.SetActive(false);
        OnClosed?.Invoke(this);
    }

    /// <summary>
    /// 仅更新窗口配置（不重新加载内容）
    /// </summary>
    public void ApplyConfig(WindowConfig config, object contentArgs = null)
    {
        // 标题
        m_titleText.text = config.Title;
        var iconRoot = m_iconImage.transform.parent.gameObject;

        // 图标
        if (config.Icon != null)
        {
            m_iconImage.sprite = config.Icon;
            iconRoot.SetActive(true);
        }
        else
        {
            iconRoot.SetActive(false);
        }

        m_windowRoot.sizeDelta = new Vector2(config.Width, config.Height);

        if (m_fullscreenButton != null)
        {
            m_fullscreenButton.gameObject.SetActive(config.AllowFullscreen);
        }

        // 确保按钮文字为初始状态
        UpdateFullscreenButtonLabel(false);

        if (_windowContent is IWindowHandleReceiver handleReceiver)
        {
            handleReceiver.SetWindowHandle(_windowHandle);
        }

        if (contentArgs != null)
            _contentArgs = contentArgs;
    }

    /// <summary>
    /// 手动切换全屏/还原
    /// </summary>
    public void ToggleFullscreen()
    {
        if (_isFullscreen)
            ExitFullscreen();
        else
            EnterFullscreen();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (!m_allowSwitchLevel)
            return;

        RequestFocus();
    }

    /// <summary>
    /// 更改窗口标题名称
    /// </summary>
    /// <param name="newTitle"></param>
    public void ChangTitle(string newTitle)
    {
        m_titleText.text = newTitle;
    }

    #region 全屏逻辑

    private void EnterFullscreen()
    {
        if (!_config.AllowFullscreen || _isFullscreen)
            return;

        // 记录原始状态
        _originalPivot = m_windowRoot.pivot;
        _originalAnchorMin = m_windowRoot.anchorMin;
        _originalAnchorMax = m_windowRoot.anchorMax;
        _originalAnchoredPosition = m_windowRoot.anchoredPosition;
        _originalSizeDelta = m_windowRoot.sizeDelta;
        _originalParent = m_windowRoot.parent;
        _originalSiblingIndex = m_windowRoot.GetSiblingIndex();

        var canvas = m_windowRoot.GetComponentInParent<Canvas>();
        if (canvas == null)
        {
            Debug.LogWarning("[WindowContainer] 未找到可用于全屏的 Canvas，取消进入全屏。");
            return;
        }

        m_windowRoot.SetParent(canvas.transform, false);
        m_windowRoot.SetAsLastSibling();

        m_windowRoot.Stretch();

        _isFullscreen = true;
        UpdateFullscreenButtonLabel(true);
    }


    private void ExitFullscreen()
    {
        if (!_isFullscreen)
            return;

        ExitFullscreenImmediate();
    }

    /// <summary>
    /// 无条件退出全屏（内部使用，不做状态判断）
    /// </summary>
    private void ExitFullscreenImmediate()
    {
        if (_originalParent != null)
        {
            m_windowRoot.SetParent(_originalParent, false);
            var siblingIndex = Mathf.Clamp(_originalSiblingIndex, 0, _originalParent.childCount - 1);
            m_windowRoot.SetSiblingIndex(siblingIndex);
        }

        m_windowRoot.pivot = _originalPivot;
        m_windowRoot.anchorMin = _originalAnchorMin;
        m_windowRoot.anchorMax = _originalAnchorMax;
        m_windowRoot.anchoredPosition = _originalAnchoredPosition;
        m_windowRoot.sizeDelta = _originalSizeDelta;

        _isFullscreen = false;
        UpdateFullscreenButtonLabel(false);
    }

    /// <summary>
    /// 更新全屏按钮文字（可选，若未配置 Text 组件则跳过）
    /// </summary>
    private void UpdateFullscreenButtonLabel(bool fullscreen)
    {
        if (m_fullscreenButtonText != null)
        {
            m_fullscreenButtonText.text = fullscreen ? "日" : "口";
        }
    }

    #endregion

    private void LoadContent(GameObject prefab, object args)
    {
        UnloadContent();
        _loadedContentInstance = Instantiate(prefab, m_contentParent);
        _loadedContentInstance.SetActive(true);
        _loadedContentInstance.transform.localPosition = Vector3.zero;
        _loadedContentInstance.transform.localScale = Vector3.one;
        if (_loadedContentInstance.TryGetComponent<RectTransform>(out var rectTransform))
        {
            rectTransform.Stretch();
        }


        _windowContent = _loadedContentInstance.GetComponent<IWindowContent>();
        if (_windowContent != null)
        {
            if (_windowContent is IWindowHandleReceiver handleReceiver)
            {
                handleReceiver.SetWindowHandle(_windowHandle);
            }

            _windowContent.OnContentInit(args);
        }
        else
        {
            Debug.LogWarning($"[WindowContainer] 预制体 '{prefab.name}' 上未找到 IWindowContent 组件。");
        }
    }

    private void EnsureWindowHandle()
    {
        if (string.IsNullOrEmpty(_windowHandle))
        {
            _windowHandle = Guid.NewGuid().ToString("N");
        }
    }

    private void UnloadContent()
    {
        if (_loadedContentInstance != null)
        {
            Destroy(_loadedContentInstance);
            _loadedContentInstance = null;
            _windowContent = null;
        }
    }

    private void OnFucus()
    {
        this.transform.SetAsLastSibling();
    }

    public void RequestFocus()
    {
        OnFocusRequested?.Invoke(this);
    }

    public void SetFocusState(bool isFocus)
    {
        _isFocus = isFocus;
    }
    // ============ 按钮回调 ============

    private void OnCloseButtonClicked()
    {
        Close();
    }

    private void OnFullscreenButtonClicked()
    {
        ToggleFullscreen();
    }
}
